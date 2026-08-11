using ElectricalProgressive.RecipeSystem;
using ElectricalProgressive.RecipeSystem.Recipe;
using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using MachineConstruct = global::ElectricalProgressive.Construction.BEBehaviorMachineConstruct;

namespace ElectricalProgressive.Content.Block.EBlastFurnace
{
    public class BlockEntityEBlastFurnace : BlockEntityGenericTypedContainer, ITexPositionSource
    {
        internal InventoryEBlastFurnace inventory;
        private GuiDialogEBlastFurnace _clientDialog;
        public override string InventoryClassName => "eblastfurnace";
        private readonly int _maxConsumption;
        private ICoreClientAPI _capi;
        private bool _wasCraftingLastTick;

        public BlastFurnaceRecipe CurrentRecipe;
        public string CurrentRecipeName;
        public float RecipeProgress;
        public int AccumulatedEnergy { get; set; }

        /// <summary>
        /// Машина готова к работе (сборка Core MachineConstruct завершена / formed).
        /// </summary>
        public bool StructureComplete
        {
            get
            {
                var construct = GetBehavior<MachineConstruct>();
                if (construct != null && construct.HasConstruction)
                    return construct.IsReady;
                return true;
            }
            set { /* совместимость; состояние в BEBehaviorMachineConstruct */ }
        }

        public bool IsFormed => Block?.Variant?["state"] == "formed";

        // Новые поля для нагрева
        private const float MAX_TEMP = 2000f;
        private const float HEATING_POWER = 100f; // Мощность нагрева в градусах в секунду при полной энергии
        
        // Слоты (2 входа, 2 выхода)
        public ItemSlot InputSlot1 => inventory[0];
        public ItemSlot InputSlot2 => inventory[1];
        public ItemSlot OutputSlot1 => inventory[2];
        public ItemSlot OutputSlot2 => inventory[3];
        
        public override string DialogTitle => Lang.Get("eblastfurnace-title-gui");
        public override InventoryBase Inventory => inventory;

        private static MeshData? _mesh;
        private static Shape? _resultingShape;
        
        private BlockEntityAnimationUtil AnimUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil!;
        
        private MeshData?[] _meshes;
        private Shape? _nowTesselatingShape;
        private CollectibleObject _nowTesselatingObj;

        private Facing _facing = Facing.None;
        public BEBehaviorElectricalProgressive ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();

        public Facing Facing
        {
            get => this._facing;
            set
            {
                if (value != this._facing)
                {
                    this.ElectricalProgressive!.Connection = FacingHelper.FullFace(this._facing = value);
                }
            }
        }

        private AssetLocation _soundDoorOpen;
        private AssetLocation _soundDoorClose;
        private bool _isDoorOpen = false;

        public BlockEntityEBlastFurnace()
        {
            _maxConsumption = MyMiniLib.GetAttributeInt(Block, "maxConsumption", 800);
            this.inventory = new InventoryEBlastFurnace(4, InventoryClassName, null, null, null, this);
            inventory.SlotModified += OnSlotModified;
        }

        #region Анимации

        public void OpenLid()
        {
            if (_isDoorOpen) return;
            
            if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("close") == true)
            {
                AnimUtil?.StopAnimation("close");
            }

            if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("open") == false)
            {
                AnimUtil?.StartAnimation(new AnimationMetaData()
                {
                    Animation = "open",
                    Code = "open",
                    AnimationSpeed = 1.4f,
                    EaseOutSpeed = 10,
                    EaseInSpeed = 10
                });

                _capi?.World.PlaySoundAt(_soundDoorOpen, Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F);
                _isDoorOpen = true;
            }
        }

        public void CloseLid()
        {
            if (!_isDoorOpen) return;
            
            if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("open") == true)
            {
                AnimUtil?.StopAnimation("open");
            }

            if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("close") == false)
            {
                AnimUtil?.StartAnimation(new AnimationMetaData()
                {
                    Animation = "close",
                    Code = "close",
                    AnimationSpeed = 1.4f,
                    EaseOutSpeed = 10,
                    EaseInSpeed = 10
                });
            }

            _capi?.World.PlaySoundAt(_soundDoorClose, Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F);
            _isDoorOpen = false;
        }

        public void StartWorkingAnim()
        {
            if (Api?.Side != EnumAppSide.Client || AnimUtil == null || CurrentRecipe == null)
                return;

            var beh = GetBehavior<BEBehaviorEBlastFurnace>();
            if (beh == null) return;

            if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("work-on") == false)
            {
                AnimUtil.StartAnimation(new AnimationMetaData()
                {
                    Animation = "work-on",
                    Code = "work-on",
                    AnimationSpeed = 1F,
                    EaseOutSpeed = 2.0f,
                    EaseInSpeed = 1f
                });
            }
        }

        public void StopWorkingAnim()
        {
            if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("work-on") == true)
            {
                AnimUtil?.StopAnimation("work-on");
            }
        }

        #endregion

        #region Температура и нагрев

        /// <summary>
        /// Получение температуры плавления из предмета
        /// </summary>
        public float GetMeltingPointFromStack(ItemStack stack)
        {
            if (stack?.Collectible == null) return 0f;
    
            var collectible = stack.Collectible;
            var code = collectible.Code?.ToString() ?? "";
    
            // Пытаемся получить через CombustibleProps (стандартный механизм Vintagestory)
            if (collectible is Vintagestory.API.Common.Item item)
            {
                // Проверяем свойство CombustibleProps
                var combustibleProps = item.CombustibleProps;
                if (combustibleProps != null)
                {
                    return combustibleProps.MeltingPoint;
                }
            }
            
            return 0f;
        }
        
        /// <summary>
        /// Получение текущей температуры предмета
        /// </summary>
        public float GetCurrentTemperature()
        {
            if (InputSlot1?.Itemstack == null) return 20f;
            return InputSlot1.Itemstack.Collectible.GetTemperature(this.Api.World, InputSlot1.Itemstack);
        }

        /// <summary>
        /// Установка температуры предмета
        /// </summary>
        public void SetTemperature(float temperature)
        {
            if (InputSlot1?.Itemstack != null)
            {
                InputSlot1.Itemstack.Collectible.SetTemperature(this.Api.World, InputSlot1.Itemstack, temperature);
            }
        }

        #endregion

        #region Обработка энергии

        public void AddEnergy(int amount)
        {
            if (!StructureComplete)
                return;

            if (CurrentRecipe == null || InputSlot1.Empty || InputSlot2.Empty)
                return;
            
            if (amount <= 0) return;
            
            float meltingPoint = GetMeltingPointFromStack(InputSlot1.Itemstack);
            float currentTemp = GetCurrentTemperature();
            
            // Если нет температуры плавления или предмет уже достаточно горячий - плавим
            if (meltingPoint <= 0 || currentTemp >= meltingPoint)
            {
                ProcessSmelting(amount);
            }
            else
            {
                ProcessHeating(amount, currentTemp, meltingPoint);
            }
            
            MarkDirty(true);
        }

        private void ProcessHeating(int amount, float currentTemp, float meltingPoint)
        {
            var beh = GetBehavior<BEBehaviorEBlastFurnace>();
            if (beh != null)
                beh.CurrentState = BEBehaviorEBlastFurnace.FurnaceState.Heating;

            // Рассчитываем нагрев: энергия конвертируется в тепло
            // При полной мощности (800W) нагреваем на HEATING_POWER градусов в секунду
            float powerPercent = amount / (float)_maxConsumption;
            float heatingRate = HEATING_POWER * powerPercent;
            
            // Нагрев за тик (1 секунда, так как AddEnergy вызывается раз в секунду)
            float newTemp = currentTemp + heatingRate;
            newTemp = Math.Min(newTemp, meltingPoint);
            
            SetTemperature(newTemp);
            
            // Обновляем прогресс-бар для отображения нагрева
            float heatProgress = newTemp / meltingPoint;
            UpdateState(heatProgress);
        }

        private void ProcessSmelting(int amount)
        {
            var beh = GetBehavior<BEBehaviorEBlastFurnace>();
            if (beh != null)
                beh.CurrentState = BEBehaviorEBlastFurnace.FurnaceState.Smelting;
            
            // Поддерживаем температуру выше температуры плавления
            float meltingPoint = GetMeltingPointFromStack(InputSlot1.Itemstack);
            if (meltingPoint > 0)
            {
                float currentTemp = GetCurrentTemperature();
                if (currentTemp < meltingPoint)
                {
                    // Если остыл - возвращаемся к нагреву
                    ProcessHeating(amount, currentTemp, meltingPoint);
                    return;
                }
                
                // Поддерживаем температуру (немного подогреваем если нужно)
                if (currentTemp < meltingPoint + 100)
                {
                    float newTemp = Math.Min(currentTemp + 10, meltingPoint + 200);
                    SetTemperature(newTemp);
                }
            }
            
            int maxNeeded = (int)CurrentRecipe.EnergyOperation - AccumulatedEnergy;
            if (maxNeeded <= 0)
            {
                while (AccumulatedEnergy >= CurrentRecipe.EnergyOperation && HasRequiredItems())
                {
                    AccumulatedEnergy -= (int)CurrentRecipe.EnergyOperation;
                    ProcessCompletedCraft();
                    
                    if (!HasRequiredItems() || CurrentRecipe == null) break;
                }
                return;
            }
            
            int energyToAdd = Math.Min(amount, maxNeeded);
            AccumulatedEnergy += energyToAdd;
            
            if (CurrentRecipe != null && CurrentRecipe.EnergyOperation > 0)
            {
                RecipeProgress = AccumulatedEnergy / (float)CurrentRecipe.EnergyOperation;
                UpdateState(RecipeProgress);
            }
            
            if (AccumulatedEnergy >= CurrentRecipe.EnergyOperation)
            {
                while (AccumulatedEnergy >= CurrentRecipe.EnergyOperation && HasRequiredItems())
                {
                    AccumulatedEnergy -= (int)CurrentRecipe.EnergyOperation;
                    ProcessCompletedCraft();
                    
                    if (!HasRequiredItems() || CurrentRecipe == null) break;
                }
            }
        }

        #endregion

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            this.inventory.LateInitialize(InventoryClassName + "-" + this.Pos.X + "/" + this.Pos.Y + "/" + this.Pos.Z, api);
            this.RegisterGameTickListener(new Action<float>(this.Every1000Ms), 1000);

            if (api.Side == EnumAppSide.Client)
            {
                _capi = api as ICoreClientAPI;
                _meshes = new MeshData[this.inventory.Count];

                this.inventory.SlotModified += slotId => UpdateMeshes();
                UpdateMeshes();

                if (AnimUtil != null)
                {
                    PrepareAnimUtil(api, InventoryClassName);
                    AnimUtil.InitializeAnimator(InventoryClassName, _mesh, _resultingShape, new Vec3f(0, GetRotation(), 0f));
                }

                _soundDoorOpen = new AssetLocation("game:sounds/block/cokeovendoor-open");
                _soundDoorClose = new AssetLocation("game:sounds/block/cokeovendoor-close");
            }
        }

        private void PrepareAnimUtil(ICoreAPI api, string cacheDictKey)
        {
            if (_mesh == null || _resultingShape == null)
            {
                AssetLocation shapePath = Block.Shape.Base.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
                Shape _shape = Shape.TryGet(api, shapePath);
                _mesh = AnimUtil.CreateMesh(cacheDictKey, _shape, out _resultingShape, null);
            }
        }

        public int GetRotation()
        {
            var side = Block.Variant["side"];
            var adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
            return adjustedIndex * 90;
        }

        private void OnSlotModified(int slotid)
        {
            if (Api is ICoreClientAPI)
                _clientDialog?.Update(RecipeProgress);

            if (slotid < 2)
            {
                RecipeProgress = 0f;
                AccumulatedEnergy = 0;
                UpdateState(RecipeProgress);
            }

            MarkDirty();
        }

        public TextureAtlasPosition this[string textureCode]
        {
            get
            {
                var assetLocation = default(AssetLocation?);

                if (_nowTesselatingObj is Vintagestory.API.Common.Item item)
                {
                    if (item.Textures.TryGetValue(textureCode, out var compositeTexture))
                        assetLocation = compositeTexture.Baked.BakedName;
                    else if (item.Textures.TryGetValue("all", out compositeTexture))
                        assetLocation = compositeTexture.Baked.BakedName;
                }
                else if (_nowTesselatingObj is Vintagestory.API.Common.Block block)
                {
                    if (block.Textures.TryGetValue(textureCode, out var compositeTexture))
                        assetLocation = compositeTexture.Baked.BakedName;
                    else if (block.Textures.TryGetValue("all", out compositeTexture))
                        assetLocation = compositeTexture.Baked.BakedName;
                }

                if (assetLocation == null && _nowTesselatingShape != null)
                    _nowTesselatingShape.Textures.TryGetValue(textureCode, out assetLocation);

                if (assetLocation == null)
                {
                    var domain = _nowTesselatingObj.Code.Domain;
                    assetLocation = new(domain, "textures/item/" + textureCode);
                }

                return GetOrCreateTexPos(assetLocation);
            }
        }

        private TextureAtlasPosition? GetOrCreateTexPos(AssetLocation texturePath)
        {
            var textureAtlasPosition = _capi.BlockTextureAtlas[texturePath];
            if (textureAtlasPosition != null) return textureAtlasPosition;

            var pos = texturePath.Path.IndexOf("++");
            if (pos >= 0) texturePath.Path = texturePath.Path.Substring(0, pos);

            var asset = _capi.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
            if (asset != null)
                _capi.BlockTextureAtlas.GetOrInsertTexture(texturePath, out _, out textureAtlasPosition, null, 0.005f);

            return textureAtlasPosition;
        }

        public Size2i AtlasSize => _capi.BlockTextureAtlas.Size;

        public void UpdateMesh(int slotid)
        {
            if (Api == null || Api.Side == EnumAppSide.Server || _capi == null) return;
            if (slotid >= this.inventory.Count) return;
            _meshes[slotid] = null;
        }

        public void UpdateMeshes()
        {
            for (var i = 0; i < this.inventory.Count; i++)
                UpdateMesh(i);
            MarkDirty(true);
        }

        #region Логика рецептов

        public static bool FindMatchingRecipe(ref BlastFurnaceRecipe currentRecipe, ref string currentRecipeName, InventoryEBlastFurnace inventory)
        {
            currentRecipe = null;
            currentRecipeName = string.Empty;

            foreach (var recipe in ElectricalProgressiveRecipeManager.BlastFurnaceRecipes)
            {
                if (MatchesRecipe(recipe, inventory))
                {
                    currentRecipe = recipe;
                    if (recipe.Outputs.Length > 0 && recipe.Outputs[0].ResolvedItemstack != null)
                        currentRecipeName = recipe.Outputs[0].ResolvedItemstack.GetName();
                    return true;
                }
            }
            return false;
        }

        private static bool MatchesRecipe(BlastFurnaceRecipe recipe, InventoryEBlastFurnace inventory)
        {
            var usedSlots = new List<int>();

            for (var ingredIndex = 0; ingredIndex < recipe.Ingredients.Length && ingredIndex < 2; ingredIndex++)
            {
                var ingred = recipe.Ingredients[ingredIndex];
                var foundSlot = false;

                for (var slotIndex = 0; slotIndex < 2; slotIndex++)
                {
                    if (usedSlots.Contains(slotIndex)) continue;

                    var slot = inventory[slotIndex];
                    if (!slot.Empty && ingred.SatisfiesAsIngredient(slot.Itemstack))
                    {
                        usedSlots.Add(slotIndex);
                        foundSlot = true;
                        break;
                    }
                }

                if (!foundSlot) return false;
            }

            return true;
        }

        private bool HasRequiredItems()
        {
            if (CurrentRecipe == null) return false;

            for (var i = 0; i < CurrentRecipe.Ingredients.Length && i < 2; i++)
            {
                var ingred = CurrentRecipe.Ingredients[i];
                var slot = GetInputSlot(i);
                if (slot.Empty || !ingred.SatisfiesAsIngredient(slot.Itemstack))
                    return false;
            }
            return true;
        }

        private void ProcessCompletedCraft()
        {
            if (CurrentRecipe == null || Api == null || CurrentRecipe.Outputs == null || CurrentRecipe.Outputs.Length == 0)
                return;

            try
            {
                var usedSlots = new List<int>();

                for (int i = 0; i < CurrentRecipe.Outputs.Length; i++)
                {
                    var output = CurrentRecipe.Outputs[i];
                    if (Api.World.Rand.NextDouble() > output.Chance) continue;

                    var outputItem = output.ResolvedItemstack?.Clone();
                    if (outputItem == null) continue;

                    outputItem.Collectible.SetTemperature(this.Api.World, outputItem, 2000f);

                    if (i == 0)
                        TryMergeOrSpawn(outputItem, OutputSlot1);
                    else if (i == 1)
                        TryMergeOrSpawn(outputItem, OutputSlot2);
                    else
                        Api.World.SpawnItemEntity(outputItem, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }

                foreach (var ingred in CurrentRecipe.Ingredients)
                {
                    for (var slotIndex = 0; slotIndex < 2; slotIndex++)
                    {
                        if (usedSlots.Contains(slotIndex)) continue;

                        var slot = GetInputSlot(slotIndex);
                        if (!slot.Empty && ingred.SatisfiesAsIngredient(slot.Itemstack))
                        {
                            slot.TakeOut(ingred.Quantity);
                            slot.MarkDirty();
                            usedSlots.Add(slotIndex);
                            break;
                        }
                    }
                }

                if (HasRequiredItems() && CurrentRecipe != null)
                {
                    if (!FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory))
                    {
                        CurrentRecipe = null;
                        AccumulatedEnergy = 0;
                        RecipeProgress = 0;
                    }
                }
                else
                {
                    CurrentRecipe = null;
                    RecipeProgress = 0;
                }
            }
            catch (Exception ex)
            {
                Api.Logger.Error($"Smelting error in EBlastFurnace at {Pos}: {ex}");
            }
        }

        private void TryMergeOrSpawn(ItemStack stack, ItemSlot targetSlot)
        {
            if (targetSlot.Empty)
            {
                targetSlot.Itemstack = stack;
            }
            else if (targetSlot.Itemstack.Collectible == stack.Collectible &&
                     targetSlot.Itemstack.StackSize < targetSlot.Itemstack.Collectible.MaxStackSize)
            {
                var freeSpace = targetSlot.Itemstack.Collectible.MaxStackSize - targetSlot.Itemstack.StackSize;
                var toAdd = Math.Min(freeSpace, stack.StackSize);

                targetSlot.Itemstack.StackSize += toAdd;
                stack.StackSize -= toAdd;

                if (stack.StackSize > 0)
                    Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }
            else
            {
                Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }
            targetSlot.MarkDirty();
        }

        private ItemSlot GetInputSlot(int index) => index switch
        {
            0 => InputSlot1,
            1 => InputSlot2,
            _ => throw new ArgumentOutOfRangeException()
        };

        #endregion

        #region Основной цикл работы

        private void Every1000Ms(float dt)
        {
            var beh = GetBehavior<BEBehaviorEBlastFurnace>();
            if (beh == null || !StructureComplete)
            {
                StopWorkingAnim();
                return;
            }

            var hasPower = beh.PowerSetting >= _maxConsumption * 0.1f;
            var hasRecipe = !InputSlot1.Empty && !InputSlot2.Empty && FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory);
            
            var isActive = hasPower && hasRecipe && CurrentRecipe != null;
            
            if (isActive)
            {
                if (!_wasCraftingLastTick)
                {
                    StartWorkingAnim();
                }
            }
            else if (_wasCraftingLastTick)
            {
                StopWorkingAnim();
                MarkDirty(true);
            }
            
            _wasCraftingLastTick = isActive;
        }

        protected virtual void UpdateState(float progress)
        {
            if (Api?.Side == EnumAppSide.Client && _clientDialog?.IsOpened() == true)
                _clientDialog.Update(progress);
            MarkDirty(true);
        }

        #endregion

        #region GUI и взаимодействие

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (!StructureComplete)
                return true;

            if (Api.Side == EnumAppSide.Client)
            {
                OpenLid();
                
                toggleInventoryDialogClient(byPlayer, () =>
                {
                    _clientDialog = new GuiDialogEBlastFurnace(DialogTitle, Inventory, Pos, _capi);
                    _clientDialog.Update(RecipeProgress);
                    _clientDialog.OnDialogClosed += () => CloseLid();
                    return _clientDialog;
                });
            }
            return true;
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            base.OnReceivedClientPacket(player, packetid, data);
            ElectricalProgressive?.OnReceivedClientPacket(player, packetid, data);
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            base.OnReceivedServerPacket(packetid, data);
            ElectricalProgressive?.OnReceivedServerPacket(packetid, data);

            if (packetid != 1001) return;
            (this.Api.World as IClientWorldAccessor).Player.InventoryManager.CloseInventory((IInventory)this.Inventory);
            this.invDialog?.TryClose();
            this.invDialog?.Dispose();
            this.invDialog = null;
            
            CloseLid();
        }

        #endregion

        #region Сохранение состояния

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            Inventory.FromTreeAttributes(tree.GetTreeAttribute("_inventory"));
            RecipeProgress = tree.GetFloat("PowerCurrent");
            AccumulatedEnergy = tree.GetInt("accumulatedEnergy");

            if (Api != null) Inventory.AfterBlocksLoaded(Api.World);
            if (Api is ICoreClientAPI) UpdateMeshes();

            if (Api?.Side == EnumAppSide.Client && _clientDialog != null)
                _clientDialog.Update(RecipeProgress);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            ITreeAttribute invTree = new TreeAttribute();
            Inventory.ToTreeAttributes(invTree);
            tree["_inventory"] = invTree;
            tree.SetFloat("PowerCurrent", RecipeProgress);
            tree.SetInt("accumulatedEnergy", AccumulatedEnergy);
            tree.SetBool("structureComplete", StructureComplete);
        }

        #endregion

        #region Жизненный цикл

        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);
            if (ElectricalProgressive == null || byItemStack == null) return;
            LoadEProperties.Load(this.Block, this);
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
        {
            // Incomplete: только blueprint — иначе return false дорисует обычный incomplete shape
            var construct = GetBehavior<MachineConstruct>();
            if (construct is { IsRenderingBlueprint: true })
                return base.OnTesselation(mesher, tesselator);

            // MachineConstruct (Core) рисует stage-mesh сам через BEBehavior.OnTesselation
            base.OnTesselation(mesher, tesselator);

            if (_meshes != null)
            {
                for (var i = 0; i < _meshes.Length; i++)
                    if (_meshes[i] != null) mesher.AddMeshData(_meshes[i]);
            }
            
            if (AnimUtil?.activeAnimationsByAnimCode.Count == 0)
            {
                return false;
            }
            
            return true;
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            if (ElectricalProgressive != null) ElectricalProgressive.Connection = Facing.None;
            if (this.Api is ICoreClientAPI && this._clientDialog != null)
            {
                this._clientDialog?.TryClose();
                this._clientDialog = null;
            }
            StopWorkingAnim();
            if (this.Api.Side == EnumAppSide.Client && this.AnimUtil != null) this.AnimUtil?.Dispose();
            _mesh?.Dispose();
            _resultingShape = null;
            _meshes = null;
            _nowTesselatingShape = null;
            _nowTesselatingObj = null;
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            this._clientDialog?.TryClose();
            _mesh?.Dispose();
            _resultingShape = null;
            _meshes = null;
            _nowTesselatingShape = null;
            _nowTesselatingObj = null;
            _capi = null;
        }

        #endregion
    }
}