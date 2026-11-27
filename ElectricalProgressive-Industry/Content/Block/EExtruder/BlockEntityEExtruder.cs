using ElectricalProgressive.Content.Block;
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

namespace ElectricalProgressive.Content.Block.EExtruder
{
    public class BlockEntityEExtruder : BlockEntityGenericTypedContainer, ITexPositionSource
    {
        // Конфигурация
        internal InventoryExtruder inventory;
        private GuiDialogExtruder _clientDialog;
        public override string InventoryClassName => "eextruder";
        private readonly int _maxConsumption;
        private ICoreClientAPI _capi;
        private bool _wasCraftingLastTick;

        // Состояние крафта
        public ExtruderRecipe CurrentRecipe;
        public string CurrentRecipeName;
        public float RecipeProgress;
        
        /// <summary>
        /// Накопленная энергия для текущего рецепта (целые единицы)
        /// </summary>
        public int AccumulatedEnergy { get; set; }

        /// <summary>
        /// Экструзия идёт (нагрев слитка до 900°C закончен).
        /// </summary>
        public bool IsForging { get; private set; }

        private static float _maxTargetTemp = 1350f;
        public const float CraftStartTemp = 900f;
        private const float HeatPerEnergy = 0.5f;
        private enum ExtruderAnimPhase
        {
            Idle,
            Lever,
            Load,
            Press,
            Unload
        }

        private const string AnimLever = "work-lever";
        private const string AnimLoad = "work-load";
        private const string AnimPress = "work-press";
        private const string AnimUnload = "work-unload";
        private const int PressFrames = 100;
        private const float PressImpactFrame = 90f;
        private const float OneShotSpeed = 2f;
        private const float LeverDoneFrame = 36f;
        private const float LoadDoneFrame = 80f;
        private const float UnloadDoneFrame = 259f;
        private const float PressCompletePeak = 0.8f;

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

        // Слоты (2 входа, 2 выхода)
        public ItemSlot InputSlot1 => inventory[0];
        public ItemSlot InputSlot2 => inventory[1];
        public ItemSlot OutputSlot1 => inventory[2];
        public ItemSlot OutputSlot2 => inventory[3];
        public override string DialogTitle => Lang.Get("eextruder-title-gui");
        public override InventoryBase Inventory => inventory;

        private static MeshData? _mesh;
        private static Shape? _resultingShape;
        private BlockEntityAnimationUtil? AnimUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;
        private bool _animatorReadyForFormed;
        private int _lastSoundFrame = -1;
        private ExtruderAnimPhase _phase;
        private float _pressPeak;

        // Новые поля для системы мешей (как в холодильнике)
        private MeshData?[] _meshes;
        private Shape? _nowTesselatingShape;
        private CollectibleObject _nowTesselatingObj;

        //------------------------------------------------------------------------------------------------------------------
        // Электрические параметры
        private Facing _facing = Facing.None;
        public BEBehaviorElectricalProgressive ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();

        public Facing Facing
        {
            get => this._facing;
            set
            {
                if (value != this._facing)
                {
                    this.ElectricalProgressive!.Connection =
                        FacingHelper.FullFace(this._facing = value);
                }
            }
        }

        //----------------------------------------------------------------------------------------------------------------------------

        private AssetLocation _soundPress;

        public BlockEntityEExtruder()
        {
            _maxConsumption = MyMiniLib.GetAttributeInt(Block, "maxConsumption", 100);
            this.inventory = new InventoryExtruder(4, InventoryClassName, (string)null, (ICoreAPI)null, null, this);
            inventory.SlotModified += OnSlotModified;
        }

        /// <summary>
        /// Добавить энергию: сначала нагрев слитка до 900°C, затем прогресс крафта.
        /// </summary>
        public void AddEnergy(int amount)
        {
            if (!StructureComplete)
                return;

            if (CurrentRecipe == null || InputSlot1.Empty || InputSlot2.Empty)
                return;

            if (amount <= 0)
                return;

            var beh = GetBehavior<BEBehaviorEExtruder>();
            if (beh == null) return;

            float currentPower = beh.PowerSetting;
            if (currentPower <= 0) return;

            int maxAddPerTick = Math.Max(1, (int)(currentPower / 20f));
            int safeAmount = Math.Min(amount, maxAddPerTick);

            float currentTemp = GetInputTemperature();
            if (currentTemp < CraftStartTemp)
            {
                float newTemp = Math.Min(currentTemp + safeAmount * HeatPerEnergy, CraftStartTemp);
                SetInputTemperature(newTemp);
                RecipeProgress = 0f;
                SetForging(false);
                UpdateState(RecipeProgress);
                MarkDirty();
                return;
            }

            SetInputTemperature(Math.Min(Math.Max(currentTemp, CraftStartTemp), _maxTargetTemp));
            SetForging(true);

            int maxNeeded = (int)CurrentRecipe.EnergyOperation - AccumulatedEnergy;
            if (maxNeeded <= 0)
            {
                while (AccumulatedEnergy >= CurrentRecipe.EnergyOperation && HasRequiredItems())
                {
                    AccumulatedEnergy -= (int)CurrentRecipe.EnergyOperation;
                    ProcessCompletedCraft();
                    if (!HasRequiredItems() || CurrentRecipe == null)
                        break;
                }
                return;
            }

            int energyToAdd = Math.Min(safeAmount, maxNeeded);
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
                    if (!HasRequiredItems() || CurrentRecipe == null)
                        break;
                }
            }

            MarkDirty();
        }

        public float GetInputTemperature()
        {
            var stack = FindIngotSlot()?.Itemstack;
            if (stack?.Collectible == null || Api?.World == null)
                return 0f;
            return stack.Collectible.GetTemperature(Api.World, stack);
        }

        private void SetInputTemperature(float temperature)
        {
            var stack = FindIngotSlot()?.Itemstack;
            if (stack?.Collectible == null || Api?.World == null)
                return;
            stack.Collectible.SetTemperature(Api.World, stack, temperature);
        }

        private void SetForging(bool forging)
        {
            if (IsForging == forging)
                return;
            IsForging = forging;
            MarkDirty(true);
        }

        private static bool IsGauge(ItemStack? stack)
        {
            return stack?.Collectible?.Code?.Path?.Contains("gauge") == true;
        }

        private ItemSlot? FindGaugeSlot()
        {
            for (var i = 0; i < 2; i++)
            {
                if (!inventory[i].Empty && IsGauge(inventory[i].Itemstack))
                    return inventory[i];
            }
            return null;
        }

        public ItemSlot? FindIngotSlot()
        {
            for (var i = 0; i < 2; i++)
            {
                if (!inventory[i].Empty && !IsGauge(inventory[i].Itemstack))
                    return inventory[i];
            }
            return null;
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            this.inventory.LateInitialize(InventoryClassName + "-" + this.Pos.X.ToString() + "/" + this.Pos.Y.ToString() + "/" + this.Pos.Z.ToString(), api);
            this.RegisterGameTickListener(new Action<float>(this.Every1000Ms), 1000);

            if (api.Side == EnumAppSide.Client)
            {
                _capi = api as ICoreClientAPI;

                // Инициализируем массив мешей как в холодильнике
                _meshes = new MeshData[this.inventory.Count];

                // Подписываемся на изменения инвентаря
                this.inventory.SlotModified += slotId =>
                {
                    UpdateMeshes();
                };

                // Первоначальное создание мешей
                UpdateMeshes();

                _soundPress = new AssetLocation("electricalprogressiveindustry:sounds/eextruder/extruder.ogg");

                this.RegisterGameTickListener(new Action<float>(this.CheckAnimationFrame), 50);
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
            int rotationDeg = GetRotation();
            AnimUtil.InitializeAnimator(
                InventoryClassName,
                _mesh,
                _resultingShape,
                new Vec3f(0, rotationDeg, 0f));
            if (AnimUtil.renderer != null)
                AnimUtil.renderer.CustomTransform = FormedPlacement(rotationDeg);
            _animatorReadyForFormed = true;
        }

        // Сдвиг -1, 0, 1 снаружи сустава. В вершинах он крутит шестерни вокруг блока.
        private static float[] FormedPlacement(float rotationDegY)
        {
            float[] m = Mat4f.Create();
            Mat4f.Translate(m, m, 0.5f, 0f, 0.5f);
            Mat4f.RotateY(m, m, rotationDegY * GameMath.DEG2RAD);
            Mat4f.Translate(m, m, -0.5f, 0f, -0.5f);
            Mat4f.Translate(m, m, -1f, 0f, 1f);
            return m;
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

            // CreateMesh кэширует меш. Клон не даёт чужому экструдеру перезаписать уже залитые вершины.
            var src = AnimUtil.CreateMesh(cacheDictKey + "-formed", shape, out _resultingShape, null);
            _mesh = src?.Clone();
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

            if (slotid < 2 && Api.Side == EnumAppSide.Client)
            {
                UpdateMeshes();
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
                    {
                        assetLocation = compositeTexture.Baked.BakedName;
                    }
                    else if (item.Textures.TryGetValue("all", out compositeTexture))
                    {
                        assetLocation = compositeTexture.Baked.BakedName;
                    }
                }
                else if (_nowTesselatingObj is Vintagestory.API.Common.Block block)
                {
                    if (block.Textures.TryGetValue(textureCode, out var compositeTexture))
                    {
                        assetLocation = compositeTexture.Baked.BakedName;
                    }
                    else if (block.Textures.TryGetValue("all", out compositeTexture))
                    {
                        assetLocation = compositeTexture.Baked.BakedName;
                    }
                }

                if (assetLocation == null && _nowTesselatingShape != null)
                {
                    _nowTesselatingShape.Textures.TryGetValue(textureCode, out assetLocation);
                }

                if (assetLocation == null)
                {
                    var domain = _nowTesselatingObj.Code.Domain;
                    assetLocation = new(domain, "textures/item/" + textureCode);
                    Api.World.Logger.Warning("Текстура {0} не найдена в текстурах предмета или формы, используется путь: {1}", textureCode, assetLocation);
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
            else
            {
                Api.World.Logger.Warning("Текстура не найдена по пути: {0}", texturePath);
            }

            return textureAtlasPosition;
        }

        public Size2i AtlasSize => _capi.BlockTextureAtlas.Size;

        public void UpdateMesh(int slotid)
        {
            if (slotid < 2)
                UpdateMeshes();
        }

        public void TranslateMesh(MeshData? meshData, ItemSlot slot)
        {
            if (meshData == null || slot?.Itemstack == null)
                return;

            var stack = slot.Itemstack;
            var origin = new Vec3f(0.5f, 0, 0.5f);
            var orientationRotate = Block.Shape.rotateY;

            if (stack.Class == EnumItemClass.Item)
            {
                var scaleX = MyMiniLib.GetAttributeFloat(stack.Item, "scaleX", 0.8F);
                var scaleY = MyMiniLib.GetAttributeFloat(stack.Item, "scaleY", 0.8F);
                var scaleZ = MyMiniLib.GetAttributeFloat(stack.Item, "scaleZ", 0.8F);
                var translateX = MyMiniLib.GetAttributeFloat(stack.Item, "translateX", 0F);
                var translateY = MyMiniLib.GetAttributeFloat(stack.Item, "translateY", 0F);
                var translateZ = MyMiniLib.GetAttributeFloat(stack.Item, "translateZ", 0F);
                var rotateX = MyMiniLib.GetAttributeFloat(stack.Item, "rotateX", 0F);
                var rotateY = MyMiniLib.GetAttributeFloat(stack.Item, "rotateY", 0F);
                var rotateZ = MyMiniLib.GetAttributeFloat(stack.Item, "rotateZ", 0F);

                meshData.Scale(origin, scaleX, scaleY, scaleZ);
                meshData.Translate(translateX, translateY + 0.95f, translateZ);
                meshData.Rotate(origin, rotateX * GameMath.DEG2RAD, rotateY * GameMath.DEG2RAD, rotateZ * GameMath.DEG2RAD);
            }
            else
            {
                meshData.Scale(origin, 0.8f, 0.8f, 0.8f);
                meshData.Translate(0.97f, 0.95f, -0.59f);
            }

            meshData.Translate(-1f, 0f, 1f);
            meshData.Rotate(origin, 0, orientationRotate * GameMath.DEG2RAD, 0);
        }

        private void UpdateItemParticleOffset(MeshData? mesh)
        {
            var ep = ElectricalProgressive;
            if (ep?.ParticlesOffsetPos == null)
                return;

            var center = GetMeshCenter(mesh) ?? new Vec3d(0.5, 1.01, 0.5);

            if (ep.ParticlesOffsetPos.Count == 0)
            {
                ep.ParticlesOffsetPos.Add(center);
                return;
            }

            for (var i = 0; i < ep.ParticlesOffsetPos.Count; i++)
                ep.ParticlesOffsetPos[i] = center.Clone();
        }

        private static Vec3d? GetMeshCenter(MeshData? mesh)
        {
            if (mesh?.xyz == null || mesh.VerticesCount <= 0)
                return null;

            var xyz = mesh.xyz;
            var n = mesh.VerticesCount;
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

            for (var i = 0; i < n; i++)
            {
                var o = i * 3;
                var x = xyz[o];
                var y = xyz[o + 1];
                var z = xyz[o + 2];
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (z < minZ) minZ = z;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
                if (z > maxZ) maxZ = z;
            }

            return new Vec3d((minX + maxX) * 0.5, (minY + maxY) * 0.5, (minZ + maxZ) * 0.5);
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

        private string? _inventoryVisualKey;

        public void UpdateMeshes()
        {
            if (Api == null || Api.Side == EnumAppSide.Server || _capi == null || _meshes == null)
                return;

            for (var i = 0; i < _meshes.Length; i++)
                _meshes[i] = null;

            MeshData? gaugeMesh = null;
            var gaugeSlot = FindGaugeSlot();
            if (gaugeSlot != null)
            {
                gaugeMesh = GenMesh(gaugeSlot);
                if (gaugeMesh != null)
                {
                    TranslateMesh(gaugeMesh, gaugeSlot);
                    var idx = gaugeSlot == InputSlot1 ? 0 : 1;
                    _meshes[idx] = gaugeMesh;
                }
            }

            UpdateItemParticleOffset(gaugeMesh);
            if (InventoryVisual.Changed(ref _inventoryVisualKey, inventory))
                MarkDirty(true);
        }

        #region Логика рецептов

        public static bool FindMatchingRecipe(ref ExtruderRecipe currentRecipe, ref string currentRecipeName, InventoryExtruder inventory)
        {
            currentRecipe = null;
            currentRecipeName = string.Empty;

            foreach (var recipe in ElectricalProgressiveRecipeManager.ExtruderRecipes)
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

        private static bool MatchesRecipe(ExtruderRecipe recipe, InventoryExtruder inventory)
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

                    if (ingred.Quantity == 0)
                    {
                        if (!slot.Empty && ingred.SatisfiesAsIngredient(slot.Itemstack))
                        {
                            usedSlots.Add(slotIndex);
                            foundSlot = true;
                            break;
                        }
                    }
                    else if (ingred.Quantity > 0)
                    {
                        if (!slot.Empty && ingred.SatisfiesAsIngredient(slot.Itemstack) &&
                            slot.Itemstack.StackSize >= ingred.Quantity)
                        {
                            usedSlots.Add(slotIndex);
                            foundSlot = true;
                            break;
                        }
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

        private void ProcessCompletedCraft()
        {
            if (CurrentRecipe == null || Api == null || CurrentRecipe.Outputs == null || CurrentRecipe.Outputs.Length == 0)
                return;

            try
            {
                float inputTemp = GetInputTemperature();
                var usedSlots = new List<int>();

                for (int i = 0; i < CurrentRecipe.Outputs.Length; i++)
                {
                    var output = CurrentRecipe.Outputs[i];

                    if (Api.World.Rand.NextDouble() > output.Chance)
                        continue;

                    var outputItem = output.ResolvedItemstack?.Clone();
                    if (outputItem == null)
                        continue;

                    outputItem.Collectible.SetTemperature(this.Api.World, outputItem, inputTemp);

                    if (i == 0)
                    {
                        TryMergeOrSpawn(outputItem, OutputSlot1);
                    }
                    else if (i == 1)
                    {
                        TryMergeOrSpawn(outputItem, OutputSlot2);
                    }
                    else
                    {
                        Api.World.SpawnItemEntity(outputItem, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                    }
                }

                foreach (var ingred in CurrentRecipe.Ingredients)
                {
                    if (ingred.Quantity <= 0) continue;

                    for (var slotIndex = 0; slotIndex < 2; slotIndex++)
                    {
                        if (usedSlots.Contains(slotIndex)) continue;

                        var slot = GetInputSlot(slotIndex);
                        if (!slot.Empty && ingred.SatisfiesAsIngredient(slot.Itemstack))
                        {
                            slot.TakeOut(ingred.Quantity);
                            if (!slot.Empty)
                                slot.Itemstack.Collectible.SetTemperature(Api.World, slot.Itemstack, inputTemp);
                            slot.MarkDirty();
                            usedSlots.Add(slotIndex);
                            break;
                        }
                    }
                }

                AccumulatedEnergy = 0;
                RecipeProgress = 0;

                if (HasRequiredItems() && CurrentRecipe != null)
                {
                    if (!FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory))
                    {
                        CurrentRecipe = null;
                        SetForging(false);
                        StopAnimation();
                    }
                }
                else
                {
                    CurrentRecipe = null;
                    SetForging(false);
                    StopAnimation();
                }

                UpdateState(RecipeProgress);
                MarkDirty();
            }
            catch (Exception ex)
            {
                Api.Logger.Error($"Crafting error in EExtruder at {Pos}: {ex}");
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
            var beh = GetBehavior<BEBehaviorEExtruder>();
            if (beh == null || !StructureComplete)
            {
                StopAnimation();
                return;
            }

            var hasPower = beh.PowerSetting >= _maxConsumption * 0.1f;
            var hasRecipe = !InputSlot1.Empty && !InputSlot2.Empty && FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory);

            if (Api.Side == EnumAppSide.Server)
            {
                var isHotEnough = GetInputTemperature() >= CraftStartTemp;
                SetForging(hasPower && hasRecipe && CurrentRecipe != null && isHotEnough);
            }

            var isCraftingNow = hasRecipe && CurrentRecipe != null && IsForging;

            if (isCraftingNow)
            {
                if (!_wasCraftingLastTick)
                    StartAnimation();

                if (CurrentRecipe != null && CurrentRecipe.EnergyOperation > 0)
                {
                    RecipeProgress = AccumulatedEnergy / (float)CurrentRecipe.EnergyOperation;
                    UpdateState(RecipeProgress);
                }
            }
            else if (_wasCraftingLastTick)
            {
                RequestStop();
                MarkDirty(true);
            }

            _wasCraftingLastTick = isCraftingNow;
        }

        protected virtual void UpdateState(float progress)
        {
            if (Api?.Side == EnumAppSide.Client && _clientDialog?.IsOpened() == true)
                _clientDialog.Update(progress);

            MarkDirty();
        }

        #endregion

        #region Визуальные эффекты

        private void StartAnimation()
        {
            if (Api?.Side != EnumAppSide.Client || CurrentRecipe == null)
                return;

            EnsureAnimatorReady();
            if (AnimUtil == null || _phase != ExtruderAnimPhase.Idle)
                return;

            if (ClipActive(AnimLever) || ClipActive(AnimLoad) || ClipActive(AnimPress) || ClipActive(AnimUnload))
                return;

            _phase = ExtruderAnimPhase.Lever;
            _pressPeak = 0f;
            _lastSoundFrame = -1;
            StartClip(AnimLever, OneShotSpeed);
        }

        /// <summary>
        /// Ход поршня длится столько же, сколько набор энергии рецепта.
        /// Энергия копится порциями Power/20 на тик сети.
        /// </summary>
        private float PressAnimationSpeed()
        {
            var energy = CurrentRecipe?.EnergyOperation ?? 0;
            if (energy <= 0)
                return 1f;

            var power = Math.Max(1, GetBehavior<BEBehaviorEExtruder>()?.PowerSetting ?? 1);
            var ticksPerSecond = Math.Max(1, global::ElectricalProgressive.ElectricalProgressive.speedOfElectricity);
            return (float)(PressFrames * power * ticksPerSecond / (20.0 * energy * 30.0));
        }

        private void RequestStop()
        {
            if (Api?.Side != EnumAppSide.Client || _phase == ExtruderAnimPhase.Unload)
                return;

            if (_phase == ExtruderAnimPhase.Press && CraftFinished())
            {
                BeginUnload();
                return;
            }

            StopAnimation();
        }

        private bool CraftFinished()
        {
            return _pressPeak >= PressCompletePeak && RecipeProgress <= _pressPeak - 0.5f;
        }

        private void BeginUnload()
        {
            if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
                return;

            if (_phase == ExtruderAnimPhase.Unload || ClipActive(AnimUnload))
                return;

            StopClip(AnimLever);
            StopClip(AnimLoad);
            StopClip(AnimPress);
            _phase = ExtruderAnimPhase.Unload;
            _pressPeak = 0f;
            _lastSoundFrame = -1;
            StartClip(AnimUnload, OneShotSpeed);
        }

        private void StopAnimation()
        {
            if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
            {
                _phase = ExtruderAnimPhase.Idle;
                _pressPeak = 0f;
                _lastSoundFrame = -1;
                return;
            }

            StopClip(AnimUnload);
            StopClip(AnimPress);
            StopClip(AnimLoad);
            StopClip(AnimLever);
            _phase = ExtruderAnimPhase.Idle;
            _pressPeak = 0f;
            _lastSoundFrame = -1;
        }

        private bool MachineAnimRunning()
        {
            if (ClipActive(AnimLever) || ClipActive(AnimLoad) || ClipActive(AnimPress) || ClipActive(AnimUnload))
                return true;

            var animator = AnimUtil?.animator;
            if (animator == null)
                return false;

            var lever = animator.GetAnimationState(AnimLever);
            var load = animator.GetAnimationState(AnimLoad);
            var press = animator.GetAnimationState(AnimPress);
            var unload = animator.GetAnimationState(AnimUnload);
            return lever is { Running: true } || load is { Running: true }
                || press is { Running: true } || unload is { Running: true };
        }

        private void CheckAnimationFrame(float dt)
        {
            var animator = AnimUtil?.animator;
            if (Api?.Side != EnumAppSide.Client || animator == null || _phase == ExtruderAnimPhase.Idle)
                return;

            if (!IsForging && _phase != ExtruderAnimPhase.Unload)
            {
                RequestStop();
                return;
            }

            switch (_phase)
            {
                case ExtruderAnimPhase.Lever:
                    var lever = animator.GetAnimationState(AnimLever);
                    if (lever is { Active: true } && lever.CurrentFrame >= LeverDoneFrame)
                        StartLoad();
                    break;
                case ExtruderAnimPhase.Load:
                    var load = animator.GetAnimationState(AnimLoad);
                    if (load is { Active: true } && load.CurrentFrame >= LoadDoneFrame)
                        StartPress();
                    break;
                case ExtruderAnimPhase.Press:
                    AdvancePress();
                    break;
                case ExtruderAnimPhase.Unload:
                    FinishUnloadIfDone();
                    break;
            }
        }

        private void StartLoad()
        {
            if (_phase == ExtruderAnimPhase.Load || ClipActive(AnimLoad))
                return;

            _phase = ExtruderAnimPhase.Load;
            StartClip(AnimLoad, OneShotSpeed);
        }

        private void StartPress()
        {
            if (_phase == ExtruderAnimPhase.Press || ClipActive(AnimPress))
                return;

            _phase = ExtruderAnimPhase.Press;
            _pressPeak = Math.Max(0f, RecipeProgress);
            _lastSoundFrame = -1;
            StartClip(AnimPress, PressAnimationSpeed());
        }

        private void AdvancePress()
        {
            _pressPeak = Math.Max(_pressPeak, RecipeProgress);
            if (CraftFinished())
            {
                BeginUnload();
                return;
            }

            var util = AnimUtil;
            var press = util?.animator?.GetAnimationState(AnimPress);
            if (util == null || press is not { Active: true })
            {
                _lastSoundFrame = -1;
                return;
            }

            if (util.activeAnimationsByAnimCode.TryGetValue(AnimPress, out var meta))
                meta.AnimationSpeed = PressAnimationSpeed();

            var frame = Math.Clamp(RecipeProgress, 0f, 1f) * PressFrames;
            press.CurrentFrame = frame;

            if (frame >= PressImpactFrame && _lastSoundFrame < 0)
            {
                PlayPressSound();
                _lastSoundFrame = (int)PressImpactFrame;
            }
            else if (frame < PressImpactFrame)
            {
                _lastSoundFrame = -1;
            }
        }

        private void FinishUnloadIfDone()
        {
            if (!ClipActive(AnimUnload))
            {
                _phase = ExtruderAnimPhase.Idle;
                if (IsForging && CurrentRecipe != null)
                    StartAnimation();
                return;
            }

            var unload = AnimUtil?.animator?.GetAnimationState(AnimUnload);
            if (unload is not { Active: true } || unload.CurrentFrame < UnloadDoneFrame)
                return;

            StopClip(AnimUnload);
            _phase = ExtruderAnimPhase.Idle;
            if (IsForging && CurrentRecipe != null)
                StartAnimation();
        }

        private bool ClipActive(string code)
        {
            return AnimUtil?.activeAnimationsByAnimCode?.ContainsKey(code) == true;
        }

        private void StartClip(string code, float speed)
        {
            AnimUtil?.StartAnimation(new AnimationMetaData()
            {
                Animation = code,
                Code = code,
                AnimationSpeed = speed,
                EaseOutSpeed = 10f,
                EaseInSpeed = 10f
            });
        }

        private void StopClip(string code)
        {
            if (ClipActive(code))
                AnimUtil?.StopAnimation(code);
        }

        private void PlayPressSound()
        {
            if (Api?.Side != EnumAppSide.Client)
                return;

            var capi = Api as ICoreClientAPI;
            capi.World.PlaySoundAt(
                _soundPress,
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
                    _clientDialog = new GuiDialogExtruder(DialogTitle, Inventory, Pos, _capi);
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
            IsForging = tree.GetBool("isForging");

            if (Api != null)
                Inventory.AfterBlocksLoaded(Api.World);

            if (Api is ICoreClientAPI)
            {
                UpdateMeshes();
                EnsureAnimatorReady();
                if (IsForging)
                    StartAnimation();
                else
                    RequestStop();
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
            tree.SetBool("isForging", IsForging);
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

            return MachineAnimRunning();
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