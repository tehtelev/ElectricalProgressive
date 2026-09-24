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
        private float _powerDisplay;
        private float _leverDisplay;

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
        private const float HEATING_POWER = 100f; // °C/с при полной мощности
        private long _lastHeatMs;
        
        // Слоты (2 входа, 2 выхода)
        public ItemSlot InputSlot1 => inventory[0];
        public ItemSlot InputSlot2 => inventory[1];
        public ItemSlot InputSlot3 => inventory[4];
        public ItemSlot MoldSlot => inventory[5];
        public ItemSlot OutputSlot1 => inventory[2];
        public ItemSlot OutputSlot2 => inventory[3];
        
        public override string DialogTitle => Lang.Get("eblastfurnace-title-gui");
        public override InventoryBase Inventory => inventory;

        private static MeshData? _mesh;
        private static Shape? _resultingShape;
        
        private BlockEntityAnimationUtil? AnimUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;
        
        private MeshData?[] _meshes;
        private Shape? _nowTesselatingShape;
        private CollectibleObject _nowTesselatingObj;
        private SmithingWorkItemRenderer? _resourceRenderer;
        private BlastFurnaceCoilRenderer? _coilRenderer;
        private MeshData? _coilGlowMesh;
        private bool _coilMeshReady;

        private static readonly Vec3f[] NuggetPileOffsets =
        [
            new(0f, 0f, 0f),
            new(0.05f, 0.015f, 0.08f),
            new(-0.05f, 0.015f, -0.07f),
            new(0.06f, 0.03f, -0.05f),
            new(-0.04f, 0.03f, 0.05f),
            new(0.01f, 0.05f, 0.02f),
            new(-0.02f, 0.045f, -0.09f),
            new(0.04f, 0.06f, 0.09f)
        ];

        private Facing _facing = Facing.None;
        public BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();
        public BEBehaviorEPImmersive? EPImmersive => GetBehavior<BEBehaviorEPImmersive>();

        public Facing Facing
        {
            get => this._facing;
            set
            {
                if (value != this._facing)
                    this._facing = value;
            }
        }

        private AssetLocation _soundDoorOpen;
        private AssetLocation _soundDoorClose;
        private bool _isDoorOpen = false;
        /// <summary>Аниматор привязан к полной formed-модели (после сборки).</summary>
        private bool _animatorReadyForFormed;

        public BlockEntityEBlastFurnace()
        {
            _maxConsumption = MyMiniLib.GetAttributeInt(Block, "maxConsumption", 800);
            this.inventory = new InventoryEBlastFurnace(6, InventoryClassName, null, null, CreateSlot, this);
            inventory.SlotModified += OnSlotModified;
        }

        private static ItemSlot CreateSlot(int slotId, InventoryGeneric inv) => slotId switch
        {
            5 => new ItemSlotBlastFurnaceMold(inv),
            0 or 1 or 4 => new ItemSlotBlastFurnaceCharge(inv),
            3 => new ItemSlotBlastFurnaceChance(inv),
            _ => new ItemSlot(inv)
        };

        #region Анимации

        /// <summary>
        /// Incomplete-модели без anim open/work-on. Всегда грузим formed-shape и
        /// переинициализируем аниматор после ExchangeBlock → formed.
        /// </summary>
        private void EnsureAnimatorReady()
        {
            if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
                return;

            // Пока идёт сборка — анимации печи не нужны
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
            StartHeldAnim("power");
            StartHeldAnim("lever");
        }

        public bool IsConsumingEnergy()
        {
            var beh = GetBehavior<BEBehaviorEBlastFurnace>();
            return beh != null && beh.PowerSetting > 0;
        }

        private static void DisableFaces(ShapeElement elem)
        {
            if (elem.FacesResolved == null)
                return;
            foreach (var face in elem.FacesResolved)
            {
                if (face != null)
                    face.Enabled = false;
            }
        }

        private static void KeepOnlyCoils(ShapeElement elem, bool keepGeometry)
        {
            if (!keepGeometry)
                DisableFaces(elem);
            if (elem.Children == null)
                return;
            var parentIs52 = string.Equals(elem.Name, "Cube52", StringComparison.OrdinalIgnoreCase);
            foreach (var child in elem.Children)
            {
                var childKeep = keepGeometry
                    || (parentIs52 && child.Name is "Cube3" or "Cube153" or "Cube165");
                KeepOnlyCoils(child, childKeep);
            }
        }

        private void EnsureCoilGlowMesh()
        {
            if (_coilMeshReady || _capi == null || !IsFormed)
                return;

            var shapeBlock = GetFormedBlockForAnim(Api) ?? Block;
            if (shapeBlock?.Shape?.Base == null)
                return;

            var shapePath = shapeBlock.Shape.Base.Clone()
                .WithPathPrefixOnce("shapes/")
                .WithPathAppendixOnce(".json");
            var asset = _capi.Assets.TryGet(shapePath);
            var coilShape = asset?.ToObject<Shape>();
            if (coilShape?.Elements == null)
                return;

            foreach (var root in coilShape.Elements)
            {
                root.ResolveReferences();
                KeepOnlyCoils(root, false);
            }

            var texSrc = new ShapeTextureSource(_capi, coilShape, "eblastfurnace-coil");
            _capi.Tesselator.TesselateShape(
                "eblastfurnace-coil",
                coilShape,
                out var coilMesh,
                texSrc,
                new Vec3f(0, 0, 0),
                255, 0, 0,
                null,
                null);

            if (coilMesh == null || coilMesh.VerticesCount <= 0)
                return;

            coilMesh = coilMesh.Clone();
            coilMesh.SetVertexFlags(VertexFlags.GlowLevelBitMask);
            coilMesh.Translate(-1f, 0f, 0f);
            var rot = GetRotation();
            if (rot != 0)
                coilMesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0, rot * GameMath.DEG2RAD, 0);

            _coilGlowMesh = coilMesh;
            _coilRenderer?.SetMesh(coilMesh);
            _coilMeshReady = true;
        }

        private Vintagestory.API.Common.Block? GetFormedBlockForAnim(ICoreAPI api)
        {
            if (Block?.Variant == null || !Block.Variant.ContainsKey("state"))
                return Block;

            return api.World.GetBlock(Block.CodeWithVariant("state", "formed")) ?? Block;
        }

        public void OpenLid()
        {
            if (_isDoorOpen) return;
            EnsureAnimatorReady();
            if (AnimUtil == null) return;
            
            if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("close"))
            {
                AnimUtil.StopAnimation("close");
            }

            if (!AnimUtil.activeAnimationsByAnimCode.ContainsKey("open"))
            {
                AnimUtil.StartAnimation(new AnimationMetaData()
                {
                    Animation = "open",
                    Code = "open",
                    AnimationSpeed = 1.4f,
                    EaseInSpeed = 10,
                    EaseOutSpeed = 10,
                    Weight = 1,
                    BlendMode = EnumAnimationBlendMode.Add
                });

                _capi?.World.PlaySoundAt(_soundDoorOpen, Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F);
                _isDoorOpen = true;
                MarkDirty(true);
            }
        }

        public void CloseLid()
        {
            if (!_isDoorOpen) return;
            EnsureAnimatorReady();
            if (AnimUtil == null) return;
            
            if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("open"))
            {
                AnimUtil.StopAnimation("open");
            }

            if (!AnimUtil.activeAnimationsByAnimCode.ContainsKey("close"))
            {
                AnimUtil.StartAnimation(new AnimationMetaData()
                {
                    Animation = "close",
                    Code = "close",
                    AnimationSpeed = 1.4f,
                    EaseInSpeed = 10,
                    EaseOutSpeed = 10,
                    Weight = 1,
                    BlendMode = EnumAnimationBlendMode.Add
                });
            }

            _capi?.World.PlaySoundAt(_soundDoorClose, Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F);
            _isDoorOpen = false;
            MarkDirty(true);
        }

        public void StartWorkingAnim()
        {
        }

        public void StopWorkingAnim()
        {
            if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey("work-on") == true)
                AnimUtil.StopAnimation("work-on");
        }

        private void UpdatePowerIndicator(float dt)
        {
            if (Api?.Side != EnumAppSide.Client || !IsFormed)
                return;

            EnsureAnimatorReady();
            if (AnimUtil?.animator == null)
                return;

            if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on"))
                AnimUtil.StopAnimation("work-on");

            StartHeldAnim("power");
            StartHeldAnim("lever");

            var beh = GetBehavior<BEBehaviorEBlastFurnace>();
            var watts = beh?.PowerSetting ?? 0;
            var ratio = _maxConsumption > 0
                ? GameMath.Clamp(watts / (float)_maxConsumption, 0f, 1f)
                : 0f;
            if (ratio > 0.9f)
                ratio = 1f;

            var k = GameMath.Clamp(dt * 10f, 0.08f, 1f);
            _powerDisplay += (ratio - _powerDisplay) * k;
            _leverDisplay = watts > 0 ? 1f : 0f;

            SetAnimFrame("power", _powerDisplay);
            SetAnimFrame("lever", _leverDisplay);
        }

        private void StartHeldAnim(string code)
        {
            if (AnimUtil?.activeAnimationsByAnimCode.ContainsKey(code) == true)
                return;
            AnimUtil?.StartAnimation(new AnimationMetaData
            {
                Animation = code,
                Code = code,
                AnimationSpeed = 0.0001f,
                EaseInSpeed = 1000,
                EaseOutSpeed = 1000,
                Weight = 1,
                BlendMode = EnumAnimationBlendMode.Add
            });
        }

        private void SetAnimFrame(string code, float t)
        {
            var anims = AnimUtil?.animator?.Animations;
            if (anims == null)
                return;
            foreach (var anim in anims)
            {
                var animCode = anim?.Animation?.Code ?? anim?.Animation?.Name;
                if (!string.Equals(animCode, code, StringComparison.OrdinalIgnoreCase))
                    continue;
                var frames = anim.Animation.QuantityFrames;
                anim.CurrentFrame = t >= 0.999f
                    ? Math.Max(frames - 1, 1)
                    : GameMath.Clamp(t, 0f, 1f) * Math.Max(frames - 1, 1);
                break;
            }
        }

        #endregion

        #region Температура и нагрев

        /// <summary>
        /// Получение температуры плавления из предмета
        /// </summary>
        public float GetMeltingPointFromStack(ItemStack stack)
        {
            if (stack?.Collectible == null) return 1200f;

            var meltingPoint = stack.Collectible.CombustibleProps?.MeltingPoint ?? 0f;
            if (meltingPoint <= 0)
                meltingPoint = 1200f;

            return meltingPoint;
        }
        
        /// <summary>
        /// Получение текущей температуры переплавляемого ресурса
        /// </summary>
        public float GetCurrentTemperature()
        {
            var temp = 20f;
            foreach (var slot in ChargeSlots())
            {
                if (slot.Itemstack?.Collectible == null)
                    continue;
                temp = Math.Max(temp, slot.Itemstack.Collectible.GetTemperature(Api.World, slot.Itemstack));
            }
            return temp;
        }

        /// <summary>
        /// Установка температуры переплавляемого ресурса
        /// </summary>
        public void SetTemperature(float temperature)
        {
            foreach (var slot in ChargeSlots())
            {
                if (slot.Itemstack?.Collectible == null)
                    continue;
                slot.Itemstack.Collectible.SetTemperature(Api.World, slot.Itemstack, temperature);
            }
        }

        #endregion

        #region Обработка энергии

        public void AddEnergy(int amount)
        {
            if (!StructureComplete)
                return;

            if (CurrentRecipe == null || !HasRequiredItems())
                return;

            var resourceStack = FindResourceSlot()?.Itemstack;
            if (resourceStack == null) return;
            
            if (amount <= 0) return;

            var meltingPoint = GetMeltingPointFromStack(resourceStack);
            var currentTemp = GetCurrentTemperature();

            if (currentTemp < meltingPoint)
                ProcessHeating(currentTemp, meltingPoint);
            else
                ProcessSmelting(amount);
            
            MarkDirty();
        }

        private void ProcessHeating(float currentTemp, float meltingPoint)
        {
            var beh = GetBehavior<BEBehaviorEBlastFurnace>();
            if (beh != null)
                beh.CurrentState = BEBehaviorEBlastFurnace.FurnaceState.Heating;

            var now = Api.World.ElapsedMilliseconds;
            float dt;
            if (_lastHeatMs <= 0)
                dt = 0.05f;
            else
                dt = (now - _lastHeatMs) / 1000f;
            _lastHeatMs = now;
            if (dt > 1f) dt = 1f;
            if (dt < 0f) dt = 0f;

            var powerPercent = 1f;
            if (beh != null && _maxConsumption > 0)
                powerPercent = GameMath.Clamp(beh.PowerSetting / (float)_maxConsumption, 0.05f, 1f);

            var newTemp = Math.Min(currentTemp + HEATING_POWER * powerPercent * dt, meltingPoint);
            SetTemperature(newTemp);
            UpdateState(newTemp / meltingPoint);
        }

        private void ProcessSmelting(int amount)
        {
            var beh = GetBehavior<BEBehaviorEBlastFurnace>();
            if (beh != null)
                beh.CurrentState = BEBehaviorEBlastFurnace.FurnaceState.Smelting;
            
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
            if (IsFormed)
                LoadImmersiveEProperties.Load(Block, this);

            if (api.Side == EnumAppSide.Client)
            {
                _capi = api as ICoreClientAPI;
                _meshes = new MeshData[this.inventory.Count];

                this.inventory.SlotModified += slotId => UpdateMeshes();
                _resourceRenderer = new SmithingWorkItemRenderer(_capi, () => Pos, () => FindResourceSlot()?.Itemstack);
                _capi.Event.RegisterRenderer(_resourceRenderer, EnumRenderStage.Opaque, "eblastfurnace-resource");
                UpdateMeshes();

                _soundDoorOpen = new AssetLocation("game:sounds/block/cokeovendoor-open");
                _soundDoorClose = new AssetLocation("game:sounds/block/cokeovendoor-close");

                // Если уже formed (загрузка мира / creative) — сразу готовим аниматор
                EnsureAnimatorReady();
                RegisterGameTickListener(UpdatePowerIndicator, 1);
            }
        }

        private void PrepareAnimUtil(ICoreAPI api, string cacheDictKey)
        {
            if (AnimUtil == null)
                return;

            // Всегда полная formed-модель: у eblastfurnace_base нет open/work-on
            var shapeBlock = GetFormedBlockForAnim(api) ?? Block;
            if (shapeBlock?.Shape?.Base == null)
                return;

            AssetLocation shapePath = shapeBlock.Shape.Base.Clone()
                .WithPathPrefixOnce("shapes/")
                .WithPathAppendixOnce(".json");
            Shape shape = Shape.TryGet(api, shapePath);
            if (shape == null)
                return;

            // shapebytype offsetX -1: сдвиг корней на -16 vx, чтобы шарниры дверцы
            // совпали с вершинами. Translate меша ломает origin анимации — дверца
            // уходит внутрь и не с той петли (у термогенератора offset нет).
            var animShape = shape.Clone();
            ShiftRootElementsX(animShape, -16);

            var src = AnimUtil.CreateMesh(cacheDictKey + "-formed-offx", animShape, out _resultingShape, null);
            _mesh = src?.Clone();
        }

        private static void ShiftRootElementsX(Shape shape, double voxels)
        {
            if (shape?.Elements == null)
                return;
            foreach (var el in shape.Elements)
            {
                if (el.From != null) el.From[0] += voxels;
                if (el.To != null) el.To[0] += voxels;
                if (el.RotationOrigin != null) el.RotationOrigin[0] += voxels;
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

            if (slotid is 0 or 1 or 4 or 5)
            {
                RecipeProgress = 0f;
                AccumulatedEnergy = 0;
                _lastHeatMs = 0;
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

        private static bool IsIngotMold(ItemStack? stack)
        {
            return stack?.Collectible?.Code?.Path?.Contains("ingotmold") == true;
        }

        private ItemSlot? FindMoldSlot()
        {
            if (!inventory[5].Empty && IsIngotMold(inventory[5].Itemstack))
                return inventory[5];
            foreach (var i in new[] { 0, 1, 4 })
            {
                if (!inventory[i].Empty && IsIngotMold(inventory[i].Itemstack))
                    return inventory[i];
            }

            return null;
        }

        public ItemSlot? FindResourceSlot()
        {
            foreach (var slot in ChargeSlots())
                return slot;
            return null;
        }

        private IEnumerable<ItemSlot> ChargeSlots()
        {
            foreach (var i in new[] { 0, 1, 4 })
            {
                if (!inventory[i].Empty && !IsIngotMold(inventory[i].Itemstack))
                    yield return inventory[i];
            }
        }

        public void UpdateMesh(int slotid)
        {
            if (slotid is 0 or 1 or 4 or 5)
                UpdateMeshes();
        }

        private string? _inventoryVisualKey;

        public void UpdateMeshes()
        {
            if (Api == null || Api.Side == EnumAppSide.Server || _capi == null || _meshes == null)
                return;

            for (var i = 0; i < _meshes.Length; i++)
                _meshes[i] = null;

            if (!IsFormed)
            {
                MarkDirty(true);
                return;
            }

            MeshData? moldMesh = null;
            var moldSlot = FindMoldSlot();
            if (moldSlot != null)
            {
                moldMesh = GenMesh(moldSlot);
                if (moldMesh != null)
                {
                    TranslateIntoHearth(moldMesh);
                    _meshes[moldSlot == InputSlot1 ? 0 : 1] = moldMesh;
                }
            }

            MeshData? resourceMesh = null;
            var pileIndex = 0;
            foreach (var resourceSlot in ChargeSlots())
            {
                var pile = BuildResourcePile(resourceSlot, moldMesh);
                if (pile == null)
                    continue;
                pile.Translate(pileIndex * 0.07f, pileIndex * 0.02f, pileIndex * 0.05f);
                if (resourceMesh == null)
                    resourceMesh = pile;
                else
                    resourceMesh.AddMeshData(pile);
                pileIndex++;
            }

            _resourceRenderer?.SetMesh(resourceMesh);

            if (InventoryVisual.Changed(ref _inventoryVisualKey, inventory))
                MarkDirty(true);
        }

        /// <summary>
        /// Полость между половинами корпуса, пол на y=2 vx.
        /// shapebytype offsetX -1 уже в рендере блока; меш слота в локали контроллера.
        /// </summary>
        private void TranslateIntoHearth(MeshData mesh)
        {
            var origin = new Vec3f(0.5f, 0f, 0.5f);
            mesh.Translate(0f, 0.25f, -0.5f);
            mesh.Rotate(origin, 0, Block.Shape.rotateY * GameMath.DEG2RAD, 0);
        }

        private MeshData? BuildResourcePile(ItemSlot resourceSlot, MeshData? moldMesh)
        {
            var unit = GenMesh(resourceSlot);
            if (unit == null)
                return null;

            var shown = GameMath.Clamp(resourceSlot.StackSize, 1, 8);
            var scale = moldMesh != null ? 0.5f : 0.45f;
            var origin = new Vec3f(0.5f, 0f, 0.5f);
            MeshData? pile = null;

            for (var i = 0; i < shown; i++)
            {
                var piece = unit.Clone();
                piece.Scale(origin, scale, scale, scale);
                var off = NuggetPileOffsets[i];
                piece.Translate(off.X, off.Y, off.Z);
                if (i > 0)
                    piece.Rotate(origin, 0, i * 37f * GameMath.DEG2RAD, 0);

                if (pile == null)
                    pile = piece;
                else
                    pile.AddMeshData(piece);
            }

            pile.Rotate(origin, 0, Block.Shape.rotateY * GameMath.DEG2RAD, 0);

            if (moldMesh != null)
            {
                var moldCenter = GetMeshCenter(moldMesh);
                var pileCenter = GetMeshCenter(pile);
                if (moldCenter != null && pileCenter != null)
                {
                    pile.Translate(
                        (float)(moldCenter.X - pileCenter.X),
                        (float)(moldCenter.Y - pileCenter.Y) + 0.03f,
                        (float)(moldCenter.Z - pileCenter.Z));
                }
            }
            else
                TranslateIntoHearth(pile);

            return pile;
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
            catch (Exception e)
            {
                Api.World.Logger.Error("Не удалось выполнить тесселяцию предмета {0}: {1}", stack.Collectible?.Code, e.Message);
                meshData = null;
            }

            return meshData;
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
            return recipe.Matches([inventory[0], inventory[1], inventory[4], inventory[5]], out _);
        }

        private bool HasRequiredItems()
        {
            if (CurrentRecipe == null) return false;
            return MatchesRecipe(CurrentRecipe, inventory);
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
                    for (var slotIndex = 0; slotIndex < 4; slotIndex++)
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
            2 => InputSlot3,
            3 => MoldSlot,
            _ => throw new ArgumentOutOfRangeException()
        };

        #endregion

        #region Основной цикл работы

        private void Every1000Ms(float dt)
        {
        }

        protected virtual void UpdateState(float progress)
        {
            if (Api?.Side == EnumAppSide.Client && _clientDialog?.IsOpened() == true)
                _clientDialog.Update(progress);
            MarkDirty();
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
            if (Api is ICoreClientAPI)
            {
                UpdateMeshes();
                // После ExchangeBlock → formed клиент получает новый Block через sync
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
            LoadImmersiveEProperties.Load(this.Block, this);
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
        {
            // Incomplete: только blueprint — иначе return false дорисует обычный incomplete shape
            var construct = GetBehavior<MachineConstruct>();
            if (construct is { IsRenderingBlueprint: true })
                return base.OnTesselation(mesher, tesselator);

            // После сборки подхватить аниматор, если ещё не готов
            if (IsFormed)
                EnsureAnimatorReady();

            // MachineConstruct (Core) рисует stage-mesh сам через BEBehavior.OnTesselation
            base.OnTesselation(mesher, tesselator);

            if (_meshes != null)
            {
                for (var i = 0; i < _meshes.Length; i++)
                    if (_meshes[i] != null) mesher.AddMeshData(_meshes[i]);
            }

            // Formed: корпус всегда рисует аниматор (power/open/close). Не переключать
            // _drawBaseMesh каждый тик — из-за этого корпус мигал.
            if (Block is ImmersiveWireBlock wireBlock)
                wireBlock._drawBaseMesh = !(IsFormed && _animatorReadyForFormed);

            return false;
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            if (this.Api is ICoreClientAPI && this._clientDialog != null)
            {
                this._clientDialog?.TryClose();
                this._clientDialog = null;
            }
            StopWorkingAnim();
            if (Block is ImmersiveWireBlock wb)
                wb._drawBaseMesh = true;
            if (this.Api.Side == EnumAppSide.Client && this.AnimUtil != null) this.AnimUtil?.Dispose();
            _resourceRenderer?.Dispose();
            _resourceRenderer = null;
            _coilRenderer?.Dispose();
            _coilRenderer = null;
            _coilMeshReady = false;
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
            _resourceRenderer?.Dispose();
            _resourceRenderer = null;
            _coilRenderer?.Dispose();
            _coilRenderer = null;
            _coilMeshReady = false;
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