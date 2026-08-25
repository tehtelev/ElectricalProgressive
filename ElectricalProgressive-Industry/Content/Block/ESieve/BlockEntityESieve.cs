// BlockEntityESieve.cs (с 25 выходными слотами)
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

namespace ElectricalProgressive.Content.Block.ESieve
{
    public class BlockEntityESieve : BlockEntityGenericTypedContainer, ITexPositionSource
    {
        public const int OUTPUT_SLOTS_COUNT = 25; // 25 выходных слотов
        public const int TOTAL_SLOTS = 1 + OUTPUT_SLOTS_COUNT; // 1 вход + 25 выходов = 26 слотов

        internal InventorySieve inventory;
        private GuiDialogSieve _clientDialog;
        public override string InventoryClassName => "esieve";
        private readonly int _maxConsumption;
        private ICoreClientAPI _capi;
        private bool _wasSievingLastTick;

        public SieveRecipe CurrentRecipe;
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
        }

        public bool IsFormed => Block?.Variant?["state"] == "formed";

        public ItemSlot InputSlot => inventory[0];
        public ItemSlot GetOutputSlot(int index) => inventory[1 + index]; // index 0-24
        public override string DialogTitle => Lang.Get("esieve-title-gui");
        public override InventoryBase Inventory => inventory;

        private static MeshData? _mesh;
        private static Shape? _resultingShape;
        private BlockEntityAnimationUtil? AnimUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;
        private bool _animatorReadyForFormed;
        private int _lastSoundFrame = -1;
        private long _lastAnimationCheckTime;

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

        private AssetLocation _soundSieve;

        public BlockEntityESieve()
        {
            _maxConsumption = MyMiniLib.GetAttributeInt(Block, "maxConsumption", 100);
            this.inventory = new InventorySieve(TOTAL_SLOTS, InventoryClassName, null, null, null, this);
            inventory.SlotModified += OnSlotModified;
        }

        public void AddEnergy(int amount)
        {
            if (!StructureComplete)
                return;

            if (CurrentRecipe == null || InputSlot.Empty)
                return;
    
            if (amount <= 0)
                return;
        
            int maxNeeded = (int)CurrentRecipe.EnergyOperation - AccumulatedEnergy;
            if (maxNeeded <= 0)
            {
                while (AccumulatedEnergy >= CurrentRecipe.EnergyOperation && HasRequiredItems())
                {
                    AccumulatedEnergy -= (int)CurrentRecipe.EnergyOperation;
                    ProcessCompletedSieving();
            
                    if (!HasRequiredItems() || CurrentRecipe == null)
                        break;
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
                    ProcessCompletedSieving();
            
                    if (!HasRequiredItems() || CurrentRecipe == null)
                        break;
                }
            }
    
            MarkDirty(true);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            this.inventory.LateInitialize(InventoryClassName + "-" + this.Pos.X.ToString() + "/" + this.Pos.Y.ToString() + "/" + this.Pos.Z.ToString(), api);
            this.RegisterGameTickListener(Every1000Ms, 1000);

            if (api.Side == EnumAppSide.Client)
            {
                _capi = api as ICoreClientAPI;
                _meshes = new MeshData[this.inventory.Count];

                this.inventory.SlotModified += slotId => UpdateMeshes();
                UpdateMeshes();

                _soundSieve = new AssetLocation("electricalprogressiveindustry:sounds/esieve/sieve.ogg");
                this.RegisterGameTickListener(CheckAnimationFrame, 50);
                UpdateMeshes();
                EnsureAnimatorReady();
            }
        }

        private void EnsureAnimatorReady()
        {
            if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
                return;

            if (!IsFormed)
                return;

            if (_animatorReadyForFormed && AnimUtil.animator != null)
                return;

            PrepareAnimUtil(Api, InventoryClassName);
            AnimUtil.InitializeAnimator(
                InventoryClassName,
                _mesh,
                _resultingShape,
                new Vec3f(0, GetRotation(), 0f));
            _animatorReadyForFormed = true;
        }

        private Vintagestory.API.Common.Block? GetFormedBlockForAnim(ICoreAPI api)
        {
            if (Block?.Variant == null || !Block.Variant.ContainsKey("state"))
                return Block;

            return api.World.GetBlock(Block.CodeWithVariant("state", "formed")) ?? Block;
        }

        private void PrepareAnimUtil(ICoreAPI api, string cacheDictKey)
        {
            if (AnimUtil == null)
                return;

            var shapeBlock = GetFormedBlockForAnim(api) ?? Block;
            if (shapeBlock?.Shape?.Base == null)
                return;

            AssetLocation shapePath = shapeBlock.Shape.Base.Clone()
                .WithPathPrefixOnce("shapes/")
                .WithPathAppendixOnce(".json");

            Shape shape = Shape.TryGet(api, shapePath);
            if (shape == null)
                return;

            _mesh = AnimUtil.CreateMesh(cacheDictKey + "-formed", shape, out _resultingShape, null);
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

            if (slotid == 0)
            {
                RecipeProgress = 0f;
                AccumulatedEnergy = 0;
                UpdateState(RecipeProgress);
            }

            if (slotid == 0 && Api.Side == EnumAppSide.Client)
            {
                UpdateMesh(0);
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
            if (textureAtlasPosition != null)
                return textureAtlasPosition;

            var pos = texturePath.Path.IndexOf("++");
            if (pos >= 0)
                texturePath.Path = texturePath.Path.Substring(0, pos);

            var asset = _capi.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
            if (asset != null)
            {
                _capi.BlockTextureAtlas.GetOrInsertTexture(texturePath, out var num, out textureAtlasPosition, null, 0.005f);
            }

            return textureAtlasPosition;
        }

        public Size2i AtlasSize => _capi.BlockTextureAtlas.Size;

        public void UpdateMesh(int slotid)
        {
            if (Api == null || Api.Side == EnumAppSide.Server || _capi == null)
                return;

            if (slotid >= this.inventory.Count)
                return;

            if (slotid != 0)
            {
                _meshes[slotid] = null;
                return;
            }

            if (this.inventory[slotid].Empty)
            {
                _meshes[slotid] = null;
                return;
            }

            var stack = this.inventory[slotid].Itemstack;

            if (stack == null || stack.Collectible == null)
            {
                _meshes[slotid] = null;
                return;
            }

            var meshData = GenMesh(inventory[slotid]);
            if (meshData != null)
            {
                TranslateMesh(meshData, slotid);
                _meshes[slotid] = meshData;
            }
            else
            {
                _meshes[slotid] = null;
            }
        }

        public void TranslateMesh(MeshData? meshData, int slotId)
        {
            if (meshData == null || slotId != 0)
                return;

            var stack = this.inventory[slotId].Itemstack;
            var origin = new Vec3f(0.5f, 0, 0.5f);
            var orientationRotate = Block.Shape.rotateY;

            if (stack.Class == EnumItemClass.Item)
            {
                var scaleX = MyMiniLib.GetAttributeFloat(stack.Item, "scaleX", 0.6F);
                var scaleY = MyMiniLib.GetAttributeFloat(stack.Item, "scaleY", 0.6F);
                var scaleZ = MyMiniLib.GetAttributeFloat(stack.Item, "scaleZ", 0.6F);
                var translateX = MyMiniLib.GetAttributeFloat(stack.Item, "translateX", 0F);
                var translateY = MyMiniLib.GetAttributeFloat(stack.Item, "translateY", 0.2F);
                var translateZ = MyMiniLib.GetAttributeFloat(stack.Item, "translateZ", 0F);
                var rotateX = MyMiniLib.GetAttributeFloat(stack.Item, "rotateX", 0F);
                var rotateY = MyMiniLib.GetAttributeFloat(stack.Item, "rotateY", 0F);
                var rotateZ = MyMiniLib.GetAttributeFloat(stack.Item, "rotateZ", 0F);

                meshData.Scale(origin, scaleX, scaleY, scaleZ);
                meshData.Translate(translateX, translateY + 0.3f, translateZ);
                meshData.Rotate(origin, rotateX * GameMath.DEG2RAD, rotateY * GameMath.DEG2RAD, rotateZ * GameMath.DEG2RAD);
            }
            else
            {
                meshData.Scale(origin, 0.7f, 0.7f, 0.7f);
                meshData.Translate(0.5f, 0.3f, 0.5f);
            }
    
            meshData.Rotate(origin, 0, orientationRotate * GameMath.DEG2RAD, 0);
        }

        public MeshData? GenMesh(ItemSlot slot)
        {
            var stack = slot.Itemstack;
            if (stack == null)
                return null;

            MeshData meshData;
            try
            {
                var meshSource = stack.Collectible as IContainedMeshSource;

                if (meshSource != null)
                {
                    meshData = meshSource.GenMesh(slot, _capi.BlockTextureAtlas, Pos);
                    meshData.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, Block.Shape.rotateY * 0.0174532924f, 0f);
                }
                else
                {
                    if (stack.Class == EnumItemClass.Block)
                    {
                        meshData = _capi.TesselatorManager.GetDefaultBlockMesh(stack.Block).Clone();
                    }
                    else
                    {
                        _nowTesselatingObj = stack.Collectible;
                        _nowTesselatingShape = null;

                        if (stack.Item.Shape != null)
                            _nowTesselatingShape = _capi.TesselatorManager.GetCachedShape(stack.Item.Shape.Base);

                        _capi.Tesselator.TesselateItem(stack.Item, out meshData, this);
                        meshData.RenderPassesAndExtraBits.Fill((short)2);
                    }
                }
            }
            catch (Exception e)
            {
                Api.World.Logger.Error("Не удалось выполнить тесселяцию предмета {0}: {1}", stack.Item.Code, e.Message);
                meshData = null;
            }

            return meshData;
        }

        public void UpdateMeshes()
        {
            for (var i = 0; i < this.inventory.Count; i++)
                UpdateMesh(i);

            MarkDirty(true);
        }

        #region Логика рецептов

        public static bool FindMatchingRecipe(ref SieveRecipe currentRecipe, ref string currentRecipeName, InventorySieve inventory)
        {
            currentRecipe = null;
            currentRecipeName = string.Empty;

            foreach (var recipe in ElectricalProgressiveRecipeManager.SieveRecipes)
            {
                if (MatchesRecipe(recipe, inventory))
                {
                    currentRecipe = recipe;
                    if (recipe.Outputs.Length > 0 && recipe.Outputs[0].ResolvedItemstack != null)
                    {
                        currentRecipeName = recipe.Outputs[0].ResolvedItemstack.GetName();
                    }
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesRecipe(SieveRecipe recipe, InventorySieve inventory)
        {
            var inputSlot = inventory[0];
            
            if (inputSlot.Empty) return false;
            
            if (recipe.Ingredients.Length > 0)
            {
                var ingred = recipe.Ingredients[0];
                if (ingred.Quantity > 0)
                {
                    if (!ingred.SatisfiesAsIngredient(inputSlot.Itemstack) ||
                        inputSlot.Itemstack.StackSize < ingred.Quantity)
                        return false;
                }
                else if (ingred.Quantity == 0)
                {
                    if (!ingred.SatisfiesAsIngredient(inputSlot.Itemstack))
                        return false;
                }
            }
            
            return true;
        }

        private bool HasRequiredItems()
        {
            if (CurrentRecipe == null) return false;

            if (CurrentRecipe.Ingredients.Length > 0)
            {
                var ingred = CurrentRecipe.Ingredients[0];
                var slot = InputSlot;

                if (ingred.Quantity == 0)
                {
                    if (slot.Empty || !ingred.SatisfiesAsIngredient(slot.Itemstack))
                        return false;
                }
                else if (ingred.Quantity > 0)
                {
                    if (slot.Empty || !ingred.SatisfiesAsIngredient(slot.Itemstack) ||
                        slot.Itemstack.StackSize < ingred.Quantity)
                        return false;
                }
            }

            return true;
        }

        private void ProcessCompletedSieving()
        {
            if (CurrentRecipe == null || Api == null || CurrentRecipe.Outputs == null || CurrentRecipe.Outputs.Length == 0)
                return;

            try
            {
                // Распределяем выходы по 25 слотам
                for (int i = 0; i < CurrentRecipe.Outputs.Length && i < OUTPUT_SLOTS_COUNT; i++)
                {
                    var output = CurrentRecipe.Outputs[i];
                    if (output == null) continue;
                    
                    if (Api.World.Rand.NextDouble() <= output.Chance)
                    {
                        var outputItem = output.ResolvedItemstack?.Clone();
                        if (outputItem != null && outputItem.StackSize > 0)
                        {
                            TryMergeOrSpawn(outputItem, GetOutputSlot(i));
                        }
                    }
                }

                // Если в рецепте больше выходов чем слотов - выкидываем в мир
                for (int i = OUTPUT_SLOTS_COUNT; i < CurrentRecipe.Outputs.Length; i++)
                {
                    var output = CurrentRecipe.Outputs[i];
                    if (output != null && Api.World.Rand.NextDouble() <= output.Chance)
                    {
                        var outputItem = output.ResolvedItemstack?.Clone();
                        if (outputItem != null)
                        {
                            Api.World.SpawnItemEntity(outputItem, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                        }
                    }
                }

                // Расход ингредиентов
                if (CurrentRecipe.Ingredients.Length > 0)
                {
                    var ingred = CurrentRecipe.Ingredients[0];
                    if (ingred.Quantity > 0)
                    {
                        InputSlot.TakeOut(ingred.Quantity);
                        InputSlot.MarkDirty();
                    }
                }

                // Проверяем, можно ли продолжить с тем же рецептом
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
                Api.Logger.Error($"Sieving error in ESieve at {Pos}: {ex}");
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
                {
                    Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
            }
            else
            {
                Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
            }
            targetSlot.MarkDirty();
        }

        #endregion

        #region Основной цикл работы

        private void Every1000Ms(float dt)
        {
            var beh = GetBehavior<BEBehaviorESieve>();
            if (beh == null || !StructureComplete)
            {
                StopAnimation();
                return;
            }

            var hasPower = beh.PowerSetting >= _maxConsumption * 0.1f;
            var hasRecipe = !InputSlot.Empty && FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory);
            var isSievingNow = hasPower && hasRecipe && CurrentRecipe != null;

            if (isSievingNow)
            {
                if (!_wasSievingLastTick)
                {
                    StartAnimation();
                }

                if (CurrentRecipe != null && CurrentRecipe.EnergyOperation > 0)
                {
                    RecipeProgress = AccumulatedEnergy / (float)CurrentRecipe.EnergyOperation;
                    UpdateState(RecipeProgress);
                }
            }
            else if (_wasSievingLastTick)
            {
                StopAnimation();
                MarkDirty(true);
            }

            _wasSievingLastTick = isSievingNow;
        }

        protected virtual void UpdateState(float progress)
        {
            if (Api?.Side == EnumAppSide.Client && _clientDialog?.IsOpened() == true)
                _clientDialog.Update(progress);

            MarkDirty(true);
        }

        #endregion

        #region Визуальные эффекты

        private void StartAnimation()
        {
            if (Api?.Side != EnumAppSide.Client || CurrentRecipe == null)
                return;

            EnsureAnimatorReady();
            if (AnimUtil == null) return;

            var beh = GetBehavior<BEBehaviorESieve>();
            if (beh == null) return;

            if (!AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on"))
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

        private void StopAnimation()
        {
            if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
                return;

            if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on"))
            {
                AnimUtil.StopAnimation("work-on");
            }
        }
        
        private void CheckAnimationFrame(float dt)
        {
            if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
                return;

            const int startFrame = 280;
            if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on") && AnimUtil.animator != null)
            {
                var currentTime = Api.World.ElapsedMilliseconds;
                _lastAnimationCheckTime = currentTime;

                var currentFrame = AnimUtil.animator.Animations[0].CurrentFrame;
                if (currentFrame >= startFrame && _lastSoundFrame != startFrame)
                {
                    PlaySieveSound();
                    _lastSoundFrame = startFrame;
                }
                else if ((int)currentFrame < startFrame)
                {
                    _lastSoundFrame = -1;
                }
            }
            else
            {
                _lastSoundFrame = -1;
            }
        }

        private void PlaySieveSound()
        {
            if (Api?.Side != EnumAppSide.Client)
                return;

            var capi = Api as ICoreClientAPI;
            capi.World.PlaySoundAt(
                _soundSieve,
                Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5,
                null,
                false,
                32,
                1f
            );
        }

        #endregion

        #region GUI и взаимодействие

        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (!StructureComplete)
                return true;

            if (Api.Side == EnumAppSide.Client)
            {
                toggleInventoryDialogClient(byPlayer, () =>
                {
                    _clientDialog = new GuiDialogSieve(DialogTitle, Inventory, Pos, _capi);
                    _clientDialog.Update(RecipeProgress);
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

            if (packetid != 1001)
                return;
            (this.Api.World as IClientWorldAccessor).Player.InventoryManager.CloseInventory((IInventory)this.Inventory);
            this.invDialog?.TryClose();
            this.invDialog?.Dispose();
            this.invDialog = (GuiDialogBlockEntity)null!;
        }

        #endregion

        #region Сохранение состояния

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            Inventory.FromTreeAttributes(tree.GetTreeAttribute("_inventory"));
            RecipeProgress = tree.GetFloat("PowerCurrent");
            AccumulatedEnergy = tree.GetInt("accumulatedEnergy");

            if (Api != null)
                Inventory.AfterBlocksLoaded(Api.World);

            if (Api is ICoreClientAPI)
            {
                UpdateMeshes();
                EnsureAnimatorReady();
            }

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

            if (ElectricalProgressive == null! || byItemStack == null)
                return;

            LoadEProperties.Load(this.Block, this);
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
        {
            var construct = GetBehavior<MachineConstruct>();
            if (construct is { IsRenderingBlueprint: true })
                return base.OnTesselation(mesher, tesselator);

            if (IsFormed)
                EnsureAnimatorReady();

            base.OnTesselation(mesher, tesselator);

            if (_meshes != null!)
            {
                for (var i = 0; i < _meshes.Length; i++)
                {
                    if (_meshes[i] != null)
                        mesher.AddMeshData(_meshes[i]);
                }
            }

            if (AnimUtil?.activeAnimationsByAnimCode == null ||
                !AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on"))
            {
                return false;
            }

            return true;
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();

            if (ElectricalProgressive != null!)
            {
                ElectricalProgressive.Connection = Facing.None;
            }

            if (this.Api is ICoreClientAPI && this._clientDialog != null!)
            {
                this._clientDialog?.TryClose();
                this._clientDialog = null;
            }

            StopAnimation();

            if (this.Api.Side == EnumAppSide.Client && this.AnimUtil != null!)
            {
                this.AnimUtil?.Dispose();
            }

            _mesh?.Dispose();
            _resultingShape = null;

            _meshes = null!;
            _nowTesselatingShape = null!;
            _nowTesselatingObj = null!;
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            this._clientDialog?.TryClose();

            _mesh?.Dispose();
            _resultingShape = null;

            _meshes = null!;
            _nowTesselatingShape = null!;
            _nowTesselatingObj = null!;
            _capi = null!;
        }

        #endregion
    }
}