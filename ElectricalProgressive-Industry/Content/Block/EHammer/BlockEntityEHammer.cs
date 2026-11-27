using ElectricalProgressive.Content.Block;
using ElectricalProgressive.Content.Block.EMetalForming;
using ElectricalProgressive.RecipeSystem;
using ElectricalProgressive.RecipeSystem.Recipe;
using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using MachineConstruct = global::ElectricalProgressive.Construction.BEBehaviorMachineConstruct;


namespace ElectricalProgressive.Content.Block.EHammer;

public class BlockEntityEHammer : BlockEntityGenericTypedContainer, ITexPositionSource
{
    internal InventoryHammer inventory;
    private GuiDialogHammer _clientDialog;

    private static MeshData? _mesh;
    private static Shape? _resultingShape;
    public override string InventoryClassName => "ehammer";
    public HammerRecipe CurrentRecipe;
    private readonly int _maxConsumption;
    private ICoreClientAPI _capi;
    private bool _wasCraftingLastTick;
    public ItemSlot InputSlot => this.inventory[0];
    public ItemSlot OutputSlot => this.inventory[1];
    public ItemSlot SecondaryOutputSlot => this.inventory[2];

    public const int PacketSelectRecipe = 1500;
    public const int EnergyPerHit = 50;

    private const string AnimLower = "work-lower";
    private const string AnimHit = "work-hit";
    private const int HitFrames = 30;
    private const int HitImpactFrame = 7;
    private const float LowerSpeed = 2f;

    public string SelectedProductKey = "";
    public string CurrentRecipeName;
    public float RecipeProgress;
    private readonly HammerWorkpiece _work = new();
    private bool _internalSlotChange;
    private float _workTemperature;
    private GuiDialogMetalFormingRecipes? _recipeSelector;
    
    /// <summary>
    /// Накопленная энергия для текущего рецепта (целые единицы)
    /// </summary>
    public int AccumulatedEnergy { get; set; }

    /// <summary>
    /// Ковка идёт (нагрев до 900°C закончен). Синится на клиент для анимации.
    /// </summary>
    public bool IsForging { get; private set; }

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

    private static float _maxTargetTemp = 1350f;
    public const float CraftStartTemp = 900f;
    private const float HeatPerEnergy = 0.5f;

    public override string DialogTitle => Lang.Get("ehammer-title-gui");

    public override InventoryBase Inventory => (InventoryBase)this.inventory;

    private int _lastSoundFrame = -1;
    private long _lastAnimationCheckTime;
    private BlockEntityAnimationUtil? AnimUtil => this.GetBehavior<BEBehaviorAnimatable>()?.animUtil;
    private bool _animatorReadyForFormed;

    // Новые поля для системы мешей (как в холодильнике)
    private MeshData?[] _meshes;
    private Shape? _nowTesselatingShape;
    private CollectibleObject _nowTesselatingObj;
    private SmithingWorkItemRenderer? _workItemRenderer;

    //--------------------------------------------------------------------------------

    public BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();

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

    //--------------------------------------------------------------------------------

    private AssetLocation _soundHammer;
    private Facing _facing = Facing.None;

    public BlockEntityEHammer()
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
        this.inventory = new InventoryHammer(3, InventoryClassName, (string)null, (ICoreAPI)null, null, this);
        this.inventory.SlotModified += new Action<int>(this.OnSlotModifid);
    }

    /// <summary>
    /// Добавить энергию: сначала нагрев входа до 900°C, затем прогресс крафта.
    /// </summary>
    public void AddEnergy(int amount)
    {
        if (!StructureComplete)
            return;

        if (!string.IsNullOrEmpty(SelectedProductKey))
        {
            AddSmithingEnergy(amount);
            return;
        }

        if (CurrentRecipe == null || InputSlot.Empty)
            return;

        if (amount <= 0)
            return;

        var beh = GetBehavior<BEBehaviorEHammer>();
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
            ProcessCompletedCraft();
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
            ProcessCompletedCraft();
        }

        MarkDirty();
    }

    public float GetInputTemperature()
    {
        var stack = InputSlot?.Itemstack;
        if (stack?.Collectible == null || Api?.World == null)
            return 0f;
        return stack.Collectible.GetTemperature(Api.World, stack);
    }

    private void SetInputTemperature(float temperature)
    {
        var stack = InputSlot?.Itemstack;
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

            _workItemRenderer = new SmithingWorkItemRenderer(_capi, () => Pos, () => WorkTemperature)
            {
                VoxelWorkItem = true
            };
            _capi.Event.RegisterRenderer(_workItemRenderer, EnumRenderStage.Opaque, "ehammer-workitem");
            _capi.Event.RegisterRenderer(_workItemRenderer, EnumRenderStage.AfterFinalComposition, "ehammer-workitem");

            // Первоначальное создание мешей
            UpdateMeshes();

            _soundHammer = new AssetLocation("electricalprogressiveindustry:sounds/ehammer/hammer.ogg");

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

    // Сдвиг -1 по X снаружи сустава. В вершинах он крутит шестерни вокруг блока.
    private static float[] FormedPlacement(float rotationDegY)
    {
        float[] m = Mat4f.Create();
        Mat4f.Translate(m, m, 0.5f, 0f, 0.5f);
        Mat4f.RotateY(m, m, rotationDegY * GameMath.DEG2RAD);
        Mat4f.Translate(m, m, -0.5f, 0f, -0.5f);
        Mat4f.Translate(m, m, -1f, 0f, 0f);
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

        // CreateMesh кэширует меш. Клон не даёт чужому молоту перезаписать уже залитые вершины.
        var src = AnimUtil.CreateMesh(cacheDictKey + "-formed", shape, out _resultingShape, null);
        _mesh = src?.Clone();
    }

    public int GetRotation()
    {
        var side = Block.Variant["side"];
        var adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
        return adjustedIndex * 90;
    }

    private void CheckAnimationFrame(float dt)
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
            return;

        if (AnimUtil.animator == null)
            return;

        var lower = AnimUtil.animator.GetAnimationState(AnimLower);
        if (lower is { Active: true } && lower.CurrentFrame >= 99f &&
            !AnimUtil.activeAnimationsByAnimCode.ContainsKey(AnimHit))
        {
            StartHitAnimation();
        }

        var hit = AnimUtil.animator.GetAnimationState(AnimHit);
        if (hit is not { Active: true })
        {
            _lastSoundFrame = -1;
            return;
        }

        if (AnimUtil.activeAnimationsByAnimCode.TryGetValue(AnimHit, out var hitMeta))
            hitMeta.AnimationSpeed = HitAnimationSpeed();

        var currentFrame = hit.CurrentFrame;
        if (currentFrame >= HitImpactFrame && _lastSoundFrame != HitImpactFrame)
        {
            PlayHammerSound();
            _lastSoundFrame = HitImpactFrame;
        }
        else if (currentFrame < HitImpactFrame)
        {
            _lastSoundFrame = -1;
        }
    }

    private void PlayHammerSound()
    {
        if (Api?.Side != EnumAppSide.Client)
            return;

        var capi = Api as ICoreClientAPI;
        capi.World.PlaySoundAt(
            _soundHammer,
            Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5,
            null,
            false,
            32,
            1f
        );
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
        if (Api == null || Api.Side == EnumAppSide.Server || _capi == null)
            return;

        if (slotid >= inventory.Count)
            return;

        if (slotid != 0)
        {
            _meshes[slotid] = null;
            return;
        }

        if (_work.Active)
        {
            var workMesh = GenWorkpieceMesh();
            var outline = GenOutlineMesh();
            if (workMesh != null)
                PlaceOnSeat(workMesh, outline);
            _meshes[slotid] = workMesh;
            _workItemRenderer?.SetMesh(workMesh);
            _workItemRenderer?.SetOutline(outline);
            UpdateItemParticleOffset(workMesh);
            return;
        }

        if (inventory[slotid].Empty)
        {
            _meshes[slotid] = null;
            UpdateItemParticleOffset(null);
            _workItemRenderer?.SetMesh(null);
            _workItemRenderer?.SetOutline(null);
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

        _workItemRenderer?.SetMesh(_meshes[slotid]);
        _workItemRenderer?.SetOutline(null);
        UpdateItemParticleOffset(_meshes[slotid]);
    }

    public void TranslateMesh(MeshData? meshData, int slotId)
    {
        if (meshData == null || slotId != 0)
            return;

        var stack = this.inventory[slotId].Itemstack;
        if (stack == null)
            return;

        ApplySeat(meshData, stack);
    }

    private void ApplySeat(MeshData meshData, ItemStack stack)
    {
        var origin = new Vec3f(0.5f, 0, 0.5f);
        var orientationRotate = Block?.Shape?.rotateY ?? 0;

        if (stack.Class == EnumItemClass.Item)
        {
            var scaleX = MyMiniLib.GetAttributeFloat(stack.Item, "scaleX", 1.0F);
            var scaleY = MyMiniLib.GetAttributeFloat(stack.Item, "scaleY", 1.0F);
            var scaleZ = MyMiniLib.GetAttributeFloat(stack.Item, "scaleZ", 1.0F);
            var translateX = MyMiniLib.GetAttributeFloat(stack.Item, "translateX", 0.97F);
            var translateY = MyMiniLib.GetAttributeFloat(stack.Item, "translateY", 0.95F);
            var translateZ = MyMiniLib.GetAttributeFloat(stack.Item, "translateZ", -0.59F);
            var rotateX = MyMiniLib.GetAttributeFloat(stack.Item, "rotateX", 0F);
            var rotateY = MyMiniLib.GetAttributeFloat(stack.Item, "rotateY", 0F);
            var rotateZ = MyMiniLib.GetAttributeFloat(stack.Item, "rotateZ", 0F);

            meshData.Scale(origin, scaleX, scaleY, scaleZ);
            meshData.Translate(translateX, translateY, translateZ);
            meshData.Rotate(origin, rotateX * GameMath.DEG2RAD, rotateY * GameMath.DEG2RAD, rotateZ * GameMath.DEG2RAD);

            meshData.Translate(-1f, 0f, 0f);
            meshData.Rotate(origin, 0, orientationRotate * GameMath.DEG2RAD, 0);
        }
        else
        {
            meshData.Scale(origin, 0.8f, 0.8f, 0.8f);
            meshData.Translate(0.97f, 0.95f, -0.59f);

            meshData.Translate(-1f, 0f, 0f);
            meshData.Rotate(origin, 0, orientationRotate * GameMath.DEG2RAD, 0);
        }
    }

    /// <summary>
    /// Искры бьют в центр отрисованного меша входного предмета.
    /// </summary>
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
        for (var i = 0; i < this.inventory.Count; i++)
            UpdateMesh(i);

        if (InventoryVisual.Changed(ref _inventoryVisualKey, inventory))
            MarkDirty(true);
    }

    private void OnSlotModifid(int slotid)
    {
        if (this.Api is ICoreClientAPI && this._clientDialog != null)
            this._clientDialog.Update(RecipeProgress);

        if (slotid != 0 || _internalSlotChange)
            return;

        if (_work.Active && !InputIsWorkMetal() && !InputSlot.Empty)
        {
            if (Api?.Side == EnumAppSide.Server)
                RefundWorkpiece();
            else
                _work.Clear();
            RecipeProgress = 0f;
            AccumulatedEnergy = 0;
        }
        else if (!_work.Active && RecipeProgress < 1f)
        {
            RecipeProgress = 0f;
            AccumulatedEnergy = 0;
            UpdateState(RecipeProgress);
        }

        if (slotid == 0 && Api.Side == EnumAppSide.Client)
        {
            UpdateMesh(0);
        }

        // Убираем StopAnimation() и лишнюю логику — как в Extruder
        // Анимация будет управляться только из Every1000Ms

        this.MarkDirty();
        if (this._clientDialog == null || !this._clientDialog.IsOpened())
            return;

        this._clientDialog.SingleComposer.ReCompose();
        if (Api?.Side == EnumAppSide.Server)
        {
            FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory[0]);
            MarkDirty();
        }
    }

    public static bool FindMatchingRecipe(ref HammerRecipe currentRecipe, ref string currentRecipeName, ItemSlot inputSlot)
    {
        ItemSlot[] inputSlots = [inputSlot];
        currentRecipe = null;
        currentRecipeName = string.Empty;

        foreach (var recipe in ElectricalProgressiveRecipeManager.HammerRecipes)
        {
            var ingredientPath = recipe.Ingredients?[0]?.Code?.Path;
            if (!string.IsNullOrEmpty(ingredientPath) &&
                ingredientPath.StartsWith("ingot", StringComparison.OrdinalIgnoreCase))
                continue;

            if (recipe.Matches(inputSlots, out _))
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

    private void Every1000Ms(float dt)
    {
        var beh = GetBehavior<BEBehaviorEHammer>();
        if (beh == null || !StructureComplete)
        {
            StopAnimation();
            return;
        }

        if (!string.IsNullOrEmpty(SelectedProductKey))
        {
            var smithing = ResolveSmithingRecipe();
            var hasSmithPower = beh.PowerSetting >= _maxConsumption * 0.1F;
            if (Api.Side == EnumAppSide.Server)
            {
                var hot = WorkTemperature >= CraftStartTemp;
                SetForging(hasSmithPower && smithing != null && AcceptsSmithingPower() && hot);
            }

            var smithingNow = smithing != null && IsForging;
            if (smithingNow)
            {
                if (!_wasCraftingLastTick)
                    StartAnimation();
            }
            else if (_wasCraftingLastTick)
            {
                StopAnimation();
                MarkDirty(true);
            }

            _wasCraftingLastTick = smithingNow;
            return;
        }

        var stack = InputSlot?.Itemstack;
        if (stack is null ||
            stack.StackSize == 0 ||
            stack.Collectible == null)
        {
            SetForging(false);
            StopAnimation();
            return;
        }

        var hasPower = beh.PowerSetting >= _maxConsumption * 0.1F;
        var hasRecipe = !InputSlot.Empty && FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory[0]);

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
            StopAnimation();
            MarkDirty(true);
        }

        _wasCraftingLastTick = isCraftingNow;
    }

    private void ProcessCompletedCraft()
    {
        if (CurrentRecipe == null || Api == null || CurrentRecipe.Outputs == null || CurrentRecipe.Outputs.Length == 0)
        {
            return;
        }

        try
        {
            float inputTemp = GetInputTemperature();

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
                    TryMergeOrSpawn(outputItem, OutputSlot);
                }
                else if (i == 1)
                {
                    TryMergeOrSpawn(outputItem, SecondaryOutputSlot);
                }
                else
                {
                    Api.World.SpawnItemEntity(outputItem, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
            }

            InputSlot.TakeOut(CurrentRecipe.Ingredients[0].Quantity);
            if (!InputSlot.Empty)
                SetInputTemperature(inputTemp);
            InputSlot.MarkDirty();
            
            // Обнуляем накопленную энергию для следующего рецепта
            AccumulatedEnergy = 0;
            RecipeProgress = 0;
            
            // Проверяем, можно ли продолжить с тем же или новым рецептом
            if (!InputSlot.Empty)
            {
                FindMatchingRecipe(ref CurrentRecipe, ref CurrentRecipeName, inventory[0]);
                if (CurrentRecipe == null)
                {
                    AccumulatedEnergy = 0;
                    RecipeProgress = 0;
                }
            }
            else
            {
                CurrentRecipe = null;
                AccumulatedEnergy = 0;
                RecipeProgress = 0;
                SetForging(false);
                StopAnimation();
            }
            
            UpdateState(RecipeProgress);
            MarkDirty();
        }
        catch (Exception ex)
        {
            Api?.Logger.Error($"Ошибка в обработке крафта: {ex}");
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

            var stackTemp = stack.Collectible.GetTemperature(this.Api.World, stack);
            var targetstackTemp = targetSlot.Itemstack.Collectible.GetTemperature(this.Api.World, targetSlot.Itemstack);

            var stackCapacity = stackTemp * toAdd;
            var targetCapacity = targetstackTemp * targetSlot.Itemstack.StackSize;

            targetSlot.Itemstack.StackSize += toAdd;

            targetSlot.Itemstack.Collectible.SetTemperature(this.Api.World, targetSlot.Itemstack, (stackCapacity + targetCapacity) / targetSlot.Itemstack.StackSize);

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

    private void StartAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || (CurrentRecipe == null && string.IsNullOrEmpty(SelectedProductKey)))
            return;

        EnsureAnimatorReady();
        if (AnimUtil == null) return;

        var beh = GetBehavior<BEBehaviorEHammer>();
        if (beh == null) return;

        if (AnimUtil.activeAnimationsByAnimCode.ContainsKey(AnimLower) ||
            AnimUtil.activeAnimationsByAnimCode.ContainsKey(AnimHit))
            return;

        AnimUtil.StartAnimation(new AnimationMetaData()
        {
            Animation = AnimLower,
            Code = AnimLower,
            AnimationSpeed = LowerSpeed,
            EaseOutSpeed = 3f,
            EaseInSpeed = 4f
        });
    }

    private void StartHitAnimation()
    {
        if (AnimUtil == null || AnimUtil.activeAnimationsByAnimCode.ContainsKey(AnimHit))
            return;

        AnimUtil.StartAnimation(new AnimationMetaData()
        {
            Animation = AnimHit,
            Code = AnimHit,
            AnimationSpeed = HitAnimationSpeed(),
            EaseOutSpeed = 10f,
            EaseInSpeed = 10f
        });
    }

    /// <summary>
    /// Один цикл work-hit длится столько же, сколько один воксель.
    /// Энергия удара копится порциями Power/20 на тик сети.
    /// </summary>
    private float HitAnimationSpeed()
    {
        var power = Math.Max(1, GetBehavior<BEBehaviorEHammer>()?.PowerSetting ?? 1);
        var ticksPerSecond = Math.Max(1, global::ElectricalProgressive.ElectricalProgressive.speedOfElectricity);
        return HitFrames * power * ticksPerSecond / 30000f;
    }

    private void StopAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
            return;

        if (AnimUtil.activeAnimationsByAnimCode.ContainsKey(AnimHit))
            AnimUtil.StopAnimation(AnimHit);

        if (AnimUtil.activeAnimationsByAnimCode.ContainsKey(AnimLower))
            AnimUtil.StopAnimation(AnimLower);

        _lastSoundFrame = -1;
    }

    private bool MachineAnimRunning()
    {
        if (AnimUtil?.activeAnimationsByAnimCode != null &&
            (AnimUtil.activeAnimationsByAnimCode.ContainsKey(AnimLower) ||
             AnimUtil.activeAnimationsByAnimCode.ContainsKey(AnimHit)))
            return true;

        var animator = AnimUtil?.animator;
        if (animator == null)
            return false;

        var lower = animator.GetAnimationState(AnimLower);
        var hit = animator.GetAnimationState(AnimHit);
        return lower is { Running: true } || hit is { Running: true };
    }

    protected virtual void UpdateState(float recipeProgress)
    {
        if (Api != null && Api.Side == EnumAppSide.Client && _clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog.Update(recipeProgress);
        }
        MarkDirty();
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (!StructureComplete)
            return true;

        if (this.Api.Side == EnumAppSide.Client)
            this.toggleInventoryDialogClient(byPlayer, (CreateDialogDelegate)(() =>
            {
                this._clientDialog =
                  new GuiDialogHammer(this.DialogTitle, this.Inventory, this.Pos, this.Api as ICoreClientAPI);
                this._clientDialog.Update(RecipeProgress);
                return (GuiDialogBlockEntity)this._clientDialog;
            }));
        return true;
    }

    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(player, packetid, data);
        ElectricalProgressive?.OnReceivedClientPacket(player, packetid, data);

        if (packetid == PacketSelectRecipe && data != null)
        {
            try
            {
                SelectProduct(SerializerUtil.Deserialize<string>(data));
            }
            catch
            {
                // Пакет не строка ключа изделия.
            }
        }
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
        this.invDialog = (GuiDialogBlockEntity)null;
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        var construct = GetBehavior<MachineConstruct>();
        if (construct is { IsRenderingBlueprint: true })
            return base.OnTesselation(mesher, tesselator);

        if (IsFormed)
            EnsureAnimatorReady();

        base.OnTesselation(mesher, tesselator);

        return MachineAnimRunning();
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        // Раньше инвентаря: слот пустеет в том же пакете, и обработчик слота
        // не должен стереть заготовку, которую ещё не прочитали.
        SelectedProductKey = tree.GetString("selectedProductKey") ?? "";
        _workTemperature = tree.GetFloat("workTemperature");
        _work.Read(tree);
        this.Inventory.FromTreeAttributes(tree.GetTreeAttribute("_inventory"));
        this.RecipeProgress = tree.GetFloat("PowerCurrent");
        this.AccumulatedEnergy = tree.GetInt("accumulatedEnergy");
        this.IsForging = tree.GetBool("isForging");
        if (!string.IsNullOrEmpty(SelectedProductKey))
            CurrentRecipeName = ResolveSmithingRecipe()?.Output?.ResolvedItemstack?.GetName()
                                ?? CurrentRecipeName;

        if (this.Api != null)
            this.Inventory.AfterBlocksLoaded(this.Api.World);

        if (Api is ICoreClientAPI)
        {
            UpdateMeshes();
            EnsureAnimatorReady();
            if (IsForging)
                StartAnimation();
            else
                StopAnimation();
        }

        var api = this.Api;
        if ((api != null ? (api.Side == EnumAppSide.Client ? 1 : 0) : 0) == 0 || this._clientDialog == null)
            return;
        this._clientDialog.Update(RecipeProgress);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        var tree1 = (ITreeAttribute)new TreeAttribute();
        this.Inventory.ToTreeAttributes(tree1);
        tree["_inventory"] = (IAttribute)tree1;
        tree.SetFloat("PowerCurrent", this.RecipeProgress);
        tree.SetInt("accumulatedEnergy", this.AccumulatedEnergy);
        tree.SetBool("isForging", IsForging);
        tree.SetBool("structureComplete", StructureComplete);
        tree.SetString("selectedProductKey", SelectedProductKey ?? "");
        tree.SetFloat("workTemperature", _workTemperature);
        _work.Write(tree);
    }

    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);

        if (ElectricalProgressive == null || byItemStack == null)
            return;

        LoadEProperties.Load(this.Block, this);
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        if (ElectricalProgressive != null)
        {
            ElectricalProgressive.Connection = Facing.None;
        }

        if (this.Api is ICoreClientAPI && this._clientDialog != null)
        {
            this._clientDialog?.TryClose();
            this._clientDialog = null;
        }

        StopAnimation();

        if (this.Api.Side == EnumAppSide.Client && this.AnimUtil != null)
        {
            this.AnimUtil?.Dispose();
        }

        _workItemRenderer?.Dispose();
        _workItemRenderer = null;
        _mesh?.Dispose();
        _resultingShape = null;
        _meshes = null;
        _nowTesselatingShape = null;
        _nowTesselatingObj = null;
    }

    public bool WorkpieceActive => _work.Active;

    public float WorkTemperature => _work.Active ? _workTemperature : GetInputTemperature();

    public bool AcceptsSmithingPower()
    {
        if (string.IsNullOrEmpty(SelectedProductKey) || !StructureComplete)
            return false;

        var recipe = ResolveSmithingRecipe();
        if (recipe == null)
            return false;

        if (!_work.Active)
            return BlockEntityEMetalForming.IsWorkableInput(InputSlot?.Itemstack);

        if (_work.MetalCount >= BlockEntityEMetalForming.CountRecipeVoxels(recipe))
            return true;

        return InputIsWorkMetal();
    }

    public void OpenRecipeSelector()
    {
        if (Api is not ICoreClientAPI capi)
            return;

        if (!BlockEntityEMetalForming.IsWorkableInput(InputSlot?.Itemstack))
        {
            capi.TriggerIngameError(this, "noinput",
                Lang.Get("electricalprogressiveindustry:emetalforming-need-ingot"));
            return;
        }

        var groups = SmithingRecipesForInput()
            .GroupBy(BlockEntityEMetalForming.ProductKey)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .OrderBy(g => g.First().Output?.ResolvedItemstack?.GetName() ?? "")
            .ToList();

        if (groups.Count == 0)
        {
            capi.TriggerIngameError(this, "norecipe",
                Lang.Get("electricalprogressiveindustry:emetalforming-no-recipes"));
            return;
        }

        _recipeSelector?.Dispose();
        var selector = new GuiDialogMetalFormingRecipes(
            Lang.Get("electricalprogressiveindustry:emetalforming-select"),
            groups.Select(g => (IReadOnlyList<SmithingRecipe>)g.ToList()).ToList(),
            selectedIndex =>
            {
                var key = groups[selectedIndex].Key;
                SelectProduct(key);
                capi.Network.SendBlockEntityPacket(Pos, PacketSelectRecipe, SerializerUtil.Serialize(key));
            },
            Pos,
            capi);
        _recipeSelector = selector;
        capi.Gui.RegisterDialog(_recipeSelector);
        _recipeSelector.TryOpen();
    }

    public void SelectProduct(string? productKey)
    {
        var key = productKey ?? "";
        if (!string.Equals(SelectedProductKey, key, StringComparison.Ordinal))
        {
            if (Api?.Side == EnumAppSide.Server)
                RefundWorkpiece();
            else
                _work.Clear();

            AccumulatedEnergy = 0;
            RecipeProgress = 0f;
            SetForging(false);
        }

        SelectedProductKey = key;
        CurrentRecipe = null;
        var recipe = ResolveSmithingRecipe();
        CurrentRecipeName = recipe?.Output?.ResolvedItemstack?.GetName() ?? "";
        MarkDirty(true);
        if (Api is ICoreClientAPI)
            _clientDialog?.Update(RecipeProgress, CurrentRecipeName);
    }

    public SmithingRecipe? ResolveSmithingRecipe()
    {
        if (string.IsNullOrEmpty(SelectedProductKey) || Api == null)
            return null;

        var all = Api.GetSmithingRecipes();
        if (all == null)
            return null;

        if (_work.Active && _work.RecipeId >= 0)
        {
            var byId = all.FirstOrDefault(r => r.RecipeId == _work.RecipeId);
            if (byId != null && BlockEntityEMetalForming.ProductKey(byId) == SelectedProductKey)
                return byId;
        }

        var stack = InputSlot?.Itemstack;
        if (!BlockEntityEMetalForming.IsWorkableInput(stack))
            return null;

        var workable = stack!.Collectible.GetCollectibleInterface<IAnvilWorkable>();
        return workable?.GetMatchingRecipes(stack)?
            .FirstOrDefault(r => BlockEntityEMetalForming.IsIngotRecipe(r) &&
                                 BlockEntityEMetalForming.ProductKey(r) == SelectedProductKey);
    }

    private void AddSmithingEnergy(int amount)
    {
        if (Api?.Side != EnumAppSide.Server || !StructureComplete || amount <= 0)
            return;

        var recipe = ResolveSmithingRecipe();
        if (recipe == null)
            return;

        var beh = GetBehavior<BEBehaviorEHammer>();
        if (beh == null || beh.PowerSetting <= 0)
            return;

        var safeAmount = Math.Min(amount, Math.Max(1, (int)(beh.PowerSetting / 20f)));
        var temp = WorkTemperature;
        if (temp < CraftStartTemp)
        {
            SetWorkTemperature(Math.Min(temp + safeAmount * HeatPerEnergy, CraftStartTemp));
            RecipeProgress = _work.Active ? _work.MatchRatio(recipe) : 0f;
            SetForging(false);
            UpdateState(RecipeProgress);
            MarkDirty();
            return;
        }

        SetWorkTemperature(Math.Min(Math.Max(temp, CraftStartTemp), _maxTargetTemp));
        SetForging(true);
        AccumulatedEnergy += safeAmount;
        if (AccumulatedEnergy < EnergyPerHit)
        {
            MarkDirty();
            return;
        }

        AccumulatedEnergy -= EnergyPerHit;
        if (!StepWorkpiece(recipe))
            SetForging(false);

        MarkDirty();
    }

    private bool StepWorkpiece(SmithingRecipe recipe)
    {
        if (!_work.Active)
        {
            if (!BlockEntityEMetalForming.IsWorkableInput(InputSlot?.Itemstack))
                return false;

            var metal = InputSlot.Itemstack.Collectible.LastCodePart();
            _workTemperature = GetInputTemperature();
            ConsumeOneIngot();
            _work.Begin(Api, metal, recipe);
        }

        if (_work.Matches(recipe))
        {
            FinishWorkpiece(recipe);
            return true;
        }

        // Один удар — один воксель. Следующий слиток берём только когда
        // двигать уже нечего, а рецепту ещё не хватает металла.
        if (!_work.Hit(recipe))
        {
            var needsMetal = _work.MetalCount < BlockEntityEMetalForming.CountRecipeVoxels(recipe);
            if (!needsMetal || !InputIsWorkMetal() || _work.TryAddIngot() <= 0)
                return false;

            _workTemperature = Math.Max(_workTemperature, GetInputTemperature());
            ConsumeOneIngot();
        }

        if (_work.Matches(recipe))
        {
            FinishWorkpiece(recipe);
            return true;
        }

        RecipeProgress = _work.MatchRatio(recipe);
        UpdateState(RecipeProgress);
        return true;
    }

    private void FinishWorkpiece(SmithingRecipe recipe)
    {
        var output = recipe.Output?.ResolvedItemstack?.Clone();
        if (output != null)
        {
            output.Collectible.SetTemperature(Api.World, output, _workTemperature);
            TryMergeOrSpawn(output, OutputSlot);
        }

        var bits = _work.LeftoverMetal(recipe) * BlockEntityEMetalForming.BitsPerIngot / BlockEntityEMetalForming.VoxelsPerIngot;
        var bitStack = MakeMetalBits(_work.Metal, bits);
        if (bitStack != null)
        {
            bitStack.Collectible.SetTemperature(Api.World, bitStack, _workTemperature);
            TryMergeOrSpawn(bitStack, SecondaryOutputSlot);
        }

        _work.Clear();
        AccumulatedEnergy = 0;
        RecipeProgress = 0f;
        if (InputSlot.Empty || ResolveSmithingRecipe() == null)
            SetForging(false);
        UpdateState(RecipeProgress);
    }

    private void RefundWorkpiece()
    {
        if (!_work.Active)
            return;

        var bits = _work.MetalCount * BlockEntityEMetalForming.BitsPerIngot / BlockEntityEMetalForming.VoxelsPerIngot;
        var bitStack = MakeMetalBits(_work.Metal, bits);
        if (bitStack != null)
            Api.World.SpawnItemEntity(bitStack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));

        _work.Clear();
        _workTemperature = 0f;
    }

    private ItemStack? MakeMetalBits(string metal, int count)
    {
        if (count <= 0 || string.IsNullOrEmpty(metal) || Api?.World == null)
            return null;

        var item = Api.World.GetItem(new AssetLocation("game:metalbit-" + metal));
        return item == null ? null : new ItemStack(item, count);
    }

    private void ConsumeOneIngot()
    {
        _internalSlotChange = true;
        var temp = GetInputTemperature();
        InputSlot.TakeOut(1);
        if (!InputSlot.Empty)
            SetInputTemperature(temp);
        InputSlot.MarkDirty();
        _internalSlotChange = false;
    }

    private void SetWorkTemperature(float temperature)
    {
        _workTemperature = temperature;
        if (!InputSlot.Empty)
            SetInputTemperature(temperature);
    }

    private bool InputIsWorkMetal()
    {
        var stack = InputSlot?.Itemstack;
        return BlockEntityEMetalForming.IsIngot(stack) &&
               string.Equals(stack!.Collectible.LastCodePart(), _work.Metal, StringComparison.OrdinalIgnoreCase);
    }

    private List<SmithingRecipe> SmithingRecipesForInput()
    {
        var stack = InputSlot?.Itemstack;
        if (!BlockEntityEMetalForming.IsWorkableInput(stack))
            return [];

        var workable = stack!.Collectible.GetCollectibleInterface<IAnvilWorkable>();
        var list = workable?.GetMatchingRecipes(stack);
        if (list == null || list.Count == 0)
            return [];

        return list.Where(BlockEntityEMetalForming.IsIngotRecipe).ToList();
    }

    private MeshData? GenWorkpieceMesh()
    {
        if (_capi == null || string.IsNullOrEmpty(_work.Metal))
            return null;

        var item = Api.World.GetItem(new AssetLocation("game:workitem-" + _work.Metal));
        if (item == null)
            return null;

        MeshData? mesh;
        try
        {
            mesh = ItemWorkItem.GenMesh(_capi, new ItemStack(item), _work.Voxels);
        }
        catch (Exception e)
        {
            Api.World.Logger.Error("Не удалось собрать меш заготовки {0}: {1}", _work.Metal, e.Message);
            return null;
        }

        if (mesh == null || mesh.VerticesCount == 0)
            return null;

        return mesh;
    }

    private string? _seatKey;
    private Vec3d? _seatCenter;

    /// <summary>
    /// Центр слитка на столе. Заготовка наковальни уже стоит на высоте наковальни,
    /// поэтому её центр переносим сюда, а не прибавляем сдвиг слитка ещё раз.
    /// </summary>
    private Vec3d? IngotSeatCenter()
    {
        if (_capi == null || string.IsNullOrEmpty(_work.Metal))
            return null;

        var key = _work.Metal + ":" + (Block?.Shape?.rotateY ?? 0);
        if (_seatCenter != null && _seatKey == key)
            return _seatCenter;

        var item = Api.World.GetItem(new AssetLocation("game:ingot-" + _work.Metal));
        if (item == null)
            return null;

        var mesh = GenMesh(new DummySlot(new ItemStack(item)));
        if (mesh == null || mesh.VerticesCount == 0)
            return null;

        ApplySeat(mesh, new ItemStack(item));
        _seatKey = key;
        _seatCenter = GetMeshCenter(mesh);
        return _seatCenter;
    }

    private void PlaceOnSeat(MeshData solid, MeshData? outline)
    {
        var origin = new Vec3f(0.5f, 0f, 0.5f);
        var yaw = (Block?.Shape?.rotateY ?? 0) * GameMath.DEG2RAD;
        solid.Rotate(origin, 0f, yaw, 0f);
        outline?.Rotate(origin, 0f, yaw, 0f);

        var seat = IngotSeatCenter();
        var here = GetMeshCenter(solid);
        if (seat == null || here == null)
            return;

        var dx = (float)(seat.X - here.X);
        var dy = (float)(seat.Y - here.Y);
        var dz = (float)(seat.Z - here.Z);
        solid.Translate(dx, dy, dz);
        outline?.Translate(dx, dy, dz);
    }

    /// <summary>
    /// Зелёный — клетка рецепта пустая, оранжевый — в клетке лишний металл.
    /// Контур обновляется вместе с вокселями на каждый удар.
    /// </summary>
    private MeshData? GenOutlineMesh()
    {
        if (_capi == null)
            return null;

        var recipe = ResolveSmithingRecipe();
        var voxels = recipe?.Voxels;
        if (voxels == null)
            return null;

        var outline = new MeshData(24, 36, false, false, true, false);
        outline.SetMode(EnumDrawMode.Lines);

        var green = _capi.ColorPreset.GetColor("anvilColorGreen");
        var orange = _capi.ColorPreset.GetColor("anvilColorRed");
        var greenCube = LineMeshUtil.GetCube(green);
        var orangeCube = LineMeshUtil.GetCube(orange);
        for (var i = 0; i < greenCube.xyz.Length; i++)
        {
            greenCube.xyz[i] = greenCube.xyz[i] / 32f + 1f / 32f;
            orangeCube.xyz[i] = orangeCube.xyz[i] / 32f + 1f / 32f;
        }

        var offset = greenCube.Clone();
        var yMax = voxels.GetLength(1);

        for (var x = 0; x < 16; x++)
        {
            for (var y = 0; y < 6; y++)
            {
                for (var z = 0; z < 16; z++)
                {
                    var require = y < yMax && voxels[x, y, z];
                    var mat = (EnumVoxelMaterial)_work.Voxels[x, y, z];
                    if (require && mat == EnumVoxelMaterial.Metal)
                        continue;
                    if (!require && mat == EnumVoxelMaterial.Empty)
                        continue;

                    var px = x / 16f;
                    var py = 10f / 16f + y / 16f;
                    var pz = z / 16f;
                    for (var i = 0; i < greenCube.xyz.Length; i += 3)
                    {
                        offset.xyz[i] = px + greenCube.xyz[i];
                        offset.xyz[i + 1] = py + greenCube.xyz[i + 1];
                        offset.xyz[i + 2] = pz + greenCube.xyz[i + 2];
                    }

                    offset.Rgba = require && mat == EnumVoxelMaterial.Empty ? greenCube.Rgba : orangeCube.Rgba;
                    outline.AddMeshData(offset);
                }
            }
        }

        if (outline.VerticesCount == 0)
            return null;

        return outline;
    }

    public ItemStack InputStack
    {
        get => this.inventory[0].Itemstack;
        set
        {
            this.inventory[0].Itemstack = value;
            this.inventory[0].MarkDirty();
        }
    }

    public ItemStack OutputStack
    {
        get => this.inventory[1].Itemstack;
        set
        {
            this.inventory[1].Itemstack = value;
            this.inventory[1].MarkDirty();
        }
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        this._clientDialog?.TryClose();

        StopAnimation();

        if (this.Api.Side == EnumAppSide.Client && this.AnimUtil != null)
        {
            this.AnimUtil?.Dispose();
        }

        _workItemRenderer?.Dispose();
        _workItemRenderer = null;
        _mesh?.Dispose();
        _resultingShape = null;
        _meshes = null;
        _nowTesselatingShape = null;
        _nowTesselatingObj = null;
        _capi = null;
    }
}