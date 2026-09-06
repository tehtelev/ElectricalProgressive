using ElectricalProgressive.Content.Block;
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

namespace ElectricalProgressive.Content.Block.EMetalForming;

public class BlockEntityEMetalForming : BlockEntityGenericTypedContainer
{
    public const int PacketSelectRecipe = 1500;
    public const int PacketCancelRecipe = 1501;

    public const int VoxelsPerIngot = 42;
    public const int BitsPerIngot = 20;
    public const float CraftStartTemp = 900f;
    private const float HeatPerEnergy = 0.5f;
    private const float MaxTargetTemp = 1350f;

    internal InventoryMetalForming inventory;
    private GuiDialogMetalForming _clientDialog;

    private static MeshData? _mesh;
    private static Shape? _resultingShape;

    public override string InventoryClassName => "emetalforming";
    public override string DialogTitle => Lang.Get("emetalforming-title-gui");
    public override InventoryBase Inventory => inventory;

    public ItemSlot InputSlot => inventory[0];
    public ItemSlot OutputSlot => inventory[1];
    public ItemSlot BitsSlot => inventory[2];

    public int SelectedRecipeId = -1;
    public string SelectedProductKey = "";
    public SmithingRecipe? SelectedRecipe { get; private set; }
    public string CurrentRecipeName = string.Empty;
    public int NeededInputCount { get; private set; }
    public int RecipeVoxelCount { get; private set; }
    public int LeftoverBits { get; private set; }
    public int EnergyOperation { get; private set; }

    public float RecipeProgress;
    public int AccumulatedEnergy { get; set; }
    public bool IsForging { get; private set; }

    private readonly int _maxConsumption;
    private readonly int _energyPerIngot;
    private ICoreClientAPI? _capi;
    private bool _wasCraftingLastTick;
    private Facing _facing = Facing.None;
    private bool _animatorReadyForFormed;
    private GuiDialog? _recipeSelector;
    private MetalFormingCrateRenderer? _crateRenderer;

    private BlockEntityAnimationUtil? AnimUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;

    public AnimatorBase? GetAnimator() => AnimUtil?.animator;

    public bool IsWorkAnimating =>
        AnimUtil?.activeAnimationsByAnimCode != null &&
        AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on");

    /// <summary>Кадр work-on или -1, если анимация не идёт.</summary>
    public float GetWorkOnFrame()
    {
        if (!IsWorkAnimating || AnimUtil?.animator?.Animations == null || AnimUtil.animator.Animations.Length == 0)
            return -1f;
        return AnimUtil.animator.Animations[0].CurrentFrame;
    }

    public BEBehaviorElectricalProgressive? ElectricalProgressive =>
        GetBehavior<BEBehaviorElectricalProgressive>();

    public bool IsFormed => Block?.Variant?["state"] == "formed";

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

    public Facing Facing
    {
        get => _facing;
        set
        {
            if (value != _facing)
            {
                ElectricalProgressive!.Connection = FacingHelper.FullFace(_facing = value);
            }
        }
    }

    /// <summary>
    /// Во входе лежит материал выбранного рецепта — можно греть (даже если слитков пока мало).
    /// </summary>
    public bool CanHeatOrCraft =>
        StructureComplete &&
        !InputSlot.Empty &&
        SelectedRecipe != null &&
        RecipeMatchesInput();

    /// <summary>
    /// Хватает слитков, чтобы ковать выбранный рецепт.
    /// </summary>
    public bool CanProcess =>
        CanHeatOrCraft &&
        InputSlot.StackSize >= NeededInputCount &&
        NeededInputCount > 0;

    public BlockEntityEMetalForming()
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(Block, "maxConsumption", 200);
        _energyPerIngot = MyMiniLib.GetAttributeInt(Block, "energyPerIngot", 5000);
        inventory = new InventoryMetalForming(3, InventoryClassName, null!, null!, null!, this);
        inventory.SlotModified += OnSlotModified;
    }

    public static bool IsWorkableInput(ItemStack? stack)
    {
        if (stack?.Collectible == null)
            return false;
        if (stack.Collectible is ItemWorkItem)
            return false;
        return stack.Collectible.GetCollectibleInterface<IAnvilWorkable>() != null;
    }

    public static int CountRecipeVoxels(SmithingRecipe recipe)
    {
        if (recipe?.Voxels == null)
            return 0;
        var n = 0;
        foreach (var voxel in recipe.Voxels)
        {
            if (voxel)
                n++;
        }
        return n;
    }

    public void AddEnergy(int amount)
    {
        if (!StructureComplete)
            return;
        if (SelectedRecipe == null || InputSlot.Empty || amount <= 0)
            return;
        if (!CanProcess)
            return;

        var beh = GetBehavior<BEBehaviorEMetalForming>();
        if (beh == null)
            return;

        float currentPower = beh.PowerSetting;
        if (currentPower <= 0)
            return;

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
            MarkDirty(true);
            return;
        }

        SetInputTemperature(Math.Min(Math.Max(currentTemp, CraftStartTemp), MaxTargetTemp));
        SetForging(true);

        if (EnergyOperation <= 0)
            RecalcRecipeCosts();

        int maxNeeded = EnergyOperation - AccumulatedEnergy;
        if (maxNeeded <= 0)
        {
            ProcessCompletedCraft();
            return;
        }

        int energyToAdd = Math.Min(safeAmount, maxNeeded);
        AccumulatedEnergy += energyToAdd;

        if (EnergyOperation > 0)
        {
            RecipeProgress = AccumulatedEnergy / (float)EnergyOperation;
            UpdateState(RecipeProgress);
        }

        if (AccumulatedEnergy >= EnergyOperation)
            ProcessCompletedCraft();

        MarkDirty(true);
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
        inventory.LateInitialize(InventoryClassName + "-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);
        RegisterGameTickListener(Every1000Ms, 1000);
        ResolveSelectedRecipe();
        // Block в конструкторе ещё null — перечитываем атрибуты

        if (api.Side == EnumAppSide.Client)
        {
            _capi = (ICoreClientAPI)api;
            inventory.SlotModified += _ => _crateRenderer?.UpdateMesh();
            _crateRenderer = new MetalFormingCrateRenderer(_capi, this);
            _capi.Event.RegisterRenderer(_crateRenderer, EnumRenderStage.Opaque, "emetalforming-crate");
            EnsureAnimatorReady();
            _crateRenderer.UpdateMesh();
        }
    }

    public List<SmithingRecipe> GetRecipesForInput()
    {
        var stack = InputSlot?.Itemstack;
        if (!IsWorkableInput(stack))
            return [];

        var workable = stack!.Collectible.GetCollectibleInterface<IAnvilWorkable>();
        var list = workable?.GetMatchingRecipes(stack);
        if (list == null || list.Count == 0)
            return [];

        return list
            .OrderBy(r => r.Output?.ResolvedItemstack?.Collectible?.Code?.ToString() ?? "")
            .ToList();
    }

    public List<SmithingRecipe> GetAllSmithingRecipes()
    {
        var list = Api?.GetSmithingRecipes();
        if (list == null || list.Count == 0)
            return [];

        return list
            .Where(r => r?.Output?.ResolvedItemstack != null)
            .ToList();
    }

    public static string ProductKeyFromCode(AssetLocation? code)
    {
        if (code == null)
            return "";
        var path = code.Path;
        var dash = path.LastIndexOf('-');
        if (dash > 0)
            path = path[..dash];
        return code.Domain + ":" + path;
    }

    public static string ProductKey(SmithingRecipe recipe) =>
        ProductKeyFromCode(recipe.Output?.ResolvedItemstack?.Collectible?.Code);

    public static string ProductDisplayName(ItemStack? stack)
    {
        if (stack?.Collectible?.Code == null)
            return "";
        var key = ProductKeyFromCode(stack.Collectible.Code);
        var colon = key.IndexOf(':');
        var path = colon >= 0 ? key[(colon + 1)..] : key;
        var langKey = (stack.Class == EnumItemClass.Block ? "block-" : "item-") + path + "-*";
        var named = Lang.Get(langKey);
        if (!string.IsNullOrEmpty(named) && named != langKey)
            return named;
        return stack.GetName();
    }

    public void OpenRecipeSelector()
    {
        if (Api is not ICoreClientAPI capi)
            return;

        var groups = GetAllSmithingRecipes()
            .GroupBy(ProductKey)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .OrderBy(g => ProductDisplayName(g.First().Output.ResolvedItemstack))
            .ToList();

        if (groups.Count == 0)
        {
            capi.TriggerIngameError(this, "norecipe",
                Lang.Get("electricalprogressiveindustry:emetalforming-no-recipes"));
            return;
        }

        var outputs = groups.Select(g => g.First().Output.ResolvedItemstack).ToArray();
        _recipeSelector?.Dispose();
        _recipeSelector = new GuiDialogBlockEntityRecipeSelector(
            Lang.Get("electricalprogressiveindustry:emetalforming-select"),
            outputs,
            selectedIndex =>
            {
                var key = groups[selectedIndex].Key;
                SelectProduct(key);
                capi.Network.SendBlockEntityPacket(Pos, PacketSelectRecipe, SerializerUtil.Serialize(key));
            },
            () => { },
            Pos,
            capi);

        for (var i = 0; i < groups.Count; i++)
        {
            var ingred = GetRecipeIngredientPreview(groups[i].First());
            if (ingred != null)
                ((GuiDialogBlockEntityRecipeSelector)_recipeSelector).SetIngredientCounts(i, [ingred]);
        }

        capi.Gui.RegisterDialog(_recipeSelector);
        _recipeSelector.TryOpen();
    }

    private ItemStack? GetRecipeIngredientPreview(SmithingRecipe recipe)
    {
        var ingred = recipe.Ingredient?.ResolvedItemStack?.Clone();
        if (ingred?.Collectible == null)
            return null;

        var workable = ingred.Collectible.GetCollectibleInterface<IAnvilWorkable>();
        var voxelsPer = Math.Max(1, workable?.VoxelCountForHandbook(ingred) ?? VoxelsPerIngot);
        ingred.StackSize = Math.Max(1, (int)Math.Ceiling(CountRecipeVoxels(recipe) / (double)voxelsPer));
        return ingred;
    }

    public void SelectProduct(string? productKey)
    {
        SelectedProductKey = productKey ?? "";
        SelectedRecipeId = -1;
        AccumulatedEnergy = 0;
        RecipeProgress = 0;
        SetForging(false);
        ResolveSelectedRecipe();
        MarkDirty(true);
        if (Api is ICoreClientAPI)
            _clientDialog?.Update(RecipeProgress, CurrentRecipeName);
    }

    public void SelectRecipe(int recipeId)
    {
        if (recipeId < 0)
        {
            SelectProduct("");
            return;
        }

        var recipe = Api?.GetSmithingRecipes()?.FirstOrDefault(r => r.RecipeId == recipeId);
        SelectProduct(recipe != null ? ProductKey(recipe) : "");
    }

    private void ResolveSelectedRecipe()
    {
        SelectedRecipe = null;
        CurrentRecipeName = string.Empty;
        NeededInputCount = 0;
        RecipeVoxelCount = 0;
        LeftoverBits = 0;
        EnergyOperation = 0;

        if (Api == null)
            return;

        var all = GetAllSmithingRecipes();
        if (string.IsNullOrEmpty(SelectedProductKey) && SelectedRecipeId >= 0)
        {
            var byId = all.FirstOrDefault(r => r.RecipeId == SelectedRecipeId);
            if (byId != null)
                SelectedProductKey = ProductKey(byId);
        }

        if (string.IsNullOrEmpty(SelectedProductKey))
        {
            SelectedRecipeId = -1;
            return;
        }

        var group = all.Where(r => ProductKey(r) == SelectedProductKey).ToList();
        if (group.Count == 0)
        {
            SelectedProductKey = "";
            SelectedRecipeId = -1;
            return;
        }

        CurrentRecipeName = ProductDisplayName(group[0].Output.ResolvedItemstack);

        var matching = GetRecipesForInput();
        var hit = group.FirstOrDefault(g => matching.Any(m => m.RecipeId == g.RecipeId));
        if (hit != null)
        {
            SelectedRecipe = hit;
            SelectedRecipeId = hit.RecipeId;
        }
        else
        {
            SelectedRecipeId = -1;
        }

        RecalcRecipeCosts(SelectedRecipe ?? group[0]);
    }

    public bool RecipeMatchesInput() => StackMatchesSelectedRecipe(InputSlot?.Itemstack);

    public bool StackMatchesSelectedRecipe(ItemStack? stack)
    {
        if (!IsWorkableInput(stack))
            return false;
        if (string.IsNullOrEmpty(SelectedProductKey) && SelectedRecipe == null)
            return false;

        var workable = stack!.Collectible.GetCollectibleInterface<IAnvilWorkable>();
        var list = workable?.GetMatchingRecipes(stack);
        if (list == null || list.Count == 0)
            return false;

        if (!string.IsNullOrEmpty(SelectedProductKey))
            return list.Any(r => ProductKey(r) == SelectedProductKey);

        return list.Any(r => r.RecipeId == SelectedRecipe!.RecipeId);
    }

    private void RecalcRecipeCosts() => RecalcRecipeCosts(SelectedRecipe);

    private void RecalcRecipeCosts(SmithingRecipe? recipe)
    {
        if (recipe == null)
            return;

        var stack = InputSlot.Itemstack
                    ?? recipe.Ingredient?.ResolvedItemStack;
        if (stack?.Collectible == null)
            return;

        var workable = stack.Collectible.GetCollectibleInterface<IAnvilWorkable>();
        var voxelsPer = Math.Max(1, workable?.VoxelCountForHandbook(stack) ?? VoxelsPerIngot);
        RecipeVoxelCount = CountRecipeVoxels(recipe);
        NeededInputCount = Math.Max(1, (int)Math.Ceiling(RecipeVoxelCount / (double)voxelsPer));
        var leftoverVoxels = NeededInputCount * voxelsPer - RecipeVoxelCount;
        LeftoverBits = Math.Max(0, leftoverVoxels * BitsPerIngot / VoxelsPerIngot);
        EnergyOperation = NeededInputCount * Math.Max(1, _energyPerIngot);
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

        var shapePath = shapeBlock.Shape.Base.Clone()
            .WithPathPrefixOnce("shapes/")
            .WithPathAppendixOnce(".json");
        var shape = Shape.TryGet(api, shapePath);
        if (shape == null)
            return;

        shape = shape.Clone();
        HideCrateDummyGeometry(shape);

        var src = AnimUtil.CreateMesh(cacheDictKey + "-formed-hidecrate", shape, out _resultingShape, null);
        _mesh = src?.Clone();
    }

    private static void HideCrateDummyGeometry(Shape shape)
    {
        void Walk(ShapeElement[]? elems)
        {
            if (elems == null)
                return;
            foreach (var e in elems)
            {
                if (e.Name == "DynPlate" || (e.Name != null && e.Name.StartsWith("MaterialInputPlate")))
                {
                    e.Faces = null!;
                    e.FacesResolved = new ShapeElementFace[6];
                }
                Walk(e.Children);
            }
        }

        Walk(shape.Elements);
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
            _clientDialog?.Update(RecipeProgress, CurrentRecipeName);

        if (slotid != 0)
            return;

        var oldId = SelectedRecipeId;
        ResolveSelectedRecipe();
        if (SelectedRecipeId != oldId)
        {
            RecipeProgress = 0f;
            AccumulatedEnergy = 0;
            SetForging(false);
        }

        MarkDirty();

        if (_clientDialog != null && _clientDialog.IsOpened())
            _clientDialog.SingleComposer.ReCompose();
    }

    private void Every1000Ms(float dt)
    {
        var beh = GetBehavior<BEBehaviorEMetalForming>();
        if (beh == null || !StructureComplete)
        {
            StopAnimation();
            return;
        }

        if (InputSlot.Empty)
        {
            SetForging(false);
            StopAnimation();
            return;
        }

        if (Api.Side == EnumAppSide.Server)
            ResolveSelectedRecipe();

        var hasPower = beh.PowerSetting >= _maxConsumption * 0.1f;
        var isHotEnough = GetInputTemperature() >= CraftStartTemp;

        if (Api.Side == EnumAppSide.Server)
            SetForging(hasPower && CanProcess && isHotEnough);

        var isCraftingNow = CanProcess && IsForging;
        if (isCraftingNow)
        {
            if (!_wasCraftingLastTick)
                StartAnimation();
            if (EnergyOperation > 0)
            {
                RecipeProgress = AccumulatedEnergy / (float)EnergyOperation;
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
        if (SelectedRecipe == null || Api == null)
            return;

        try
        {
            float inputTemp = GetInputTemperature();
            RecalcRecipeCosts();
            if (InputSlot.StackSize < NeededInputCount)
                return;

            var output = SelectedRecipe.Output?.ResolvedItemstack?.Clone();
            if (output != null)
            {
                output.Collectible.SetTemperature(Api.World, output, inputTemp);
                TryMergeOrSpawn(output, OutputSlot);
            }

            if (LeftoverBits > 0)
            {
                var bits = MakeMetalBits(LeftoverBits);
                if (bits != null)
                {
                    bits.Collectible.SetTemperature(Api.World, bits, inputTemp);
                    TryMergeOrSpawn(bits, BitsSlot);
                }
            }

            InputSlot.TakeOut(NeededInputCount);
            if (!InputSlot.Empty)
                SetInputTemperature(inputTemp);
            InputSlot.MarkDirty();

            AccumulatedEnergy = 0;
            RecipeProgress = 0;
            ResolveSelectedRecipe();

            if (InputSlot.Empty || !RecipeMatchesInput() || InputSlot.StackSize < NeededInputCount)
            {
                SetForging(false);
                StopAnimation();
            }

            UpdateState(RecipeProgress);
            MarkDirty(true);
        }
        catch (Exception ex)
        {
            Api?.Logger.Error("Metal forming craft failed: {0}", ex);
        }
    }

    private ItemStack? MakeMetalBits(int count)
    {
        var stack = InputSlot.Itemstack;
        if (stack == null)
            return null;

        var workable = stack.Collectible.GetCollectibleInterface<IAnvilWorkable>();
        var baseMat = workable?.GetBaseMaterial(stack) ?? stack;
        var metal = baseMat.Collectible.LastCodePart();
        if (string.IsNullOrEmpty(metal))
            return null;

        var item = Api.World.GetItem(new AssetLocation("game:metalbit-" + metal));
        if (item == null)
            return null;

        return new ItemStack(item, count);
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

            var stackTemp = stack.Collectible.GetTemperature(Api.World, stack);
            var targetTemp = targetSlot.Itemstack.Collectible.GetTemperature(Api.World, targetSlot.Itemstack);
            var stackCapacity = stackTemp * toAdd;
            var targetCapacity = targetTemp * targetSlot.Itemstack.StackSize;

            targetSlot.Itemstack.StackSize += toAdd;
            targetSlot.Itemstack.Collectible.SetTemperature(Api.World, targetSlot.Itemstack,
                (stackCapacity + targetCapacity) / targetSlot.Itemstack.StackSize);
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

    private void StartAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || SelectedRecipe == null)
            return;

        EnsureAnimatorReady();
        if (AnimUtil == null)
            return;

        if (!AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on"))
        {
            AnimUtil.StartAnimation(new AnimationMetaData
            {
                Animation = "work-on",
                Code = "work-on",
                AnimationSpeed = 1.5f,
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
            AnimUtil.StopAnimation("work-on");
    }

    protected virtual void UpdateState(float recipeProgress)
    {
        if (Api != null && Api.Side == EnumAppSide.Client && _clientDialog != null && _clientDialog.IsOpened())
            _clientDialog.Update(recipeProgress, CurrentRecipeName);
        MarkDirty(true);
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (!StructureComplete)
            return true;

        if (Api.Side == EnumAppSide.Client)
        {
            toggleInventoryDialogClient(byPlayer, () =>
            {
                _clientDialog = new GuiDialogMetalForming(DialogTitle, Inventory, Pos, (ICoreClientAPI)Api);
                _clientDialog.Update(RecipeProgress, CurrentRecipeName);
                return _clientDialog;
            });
        }

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
                SelectRecipe(SerializerUtil.Deserialize<int>(data));
            }
            return;
        }

        if (packetid == PacketCancelRecipe)
        {
            SelectRecipe(-1);
        }
    }

    public override void OnReceivedServerPacket(int packetid, byte[] data)
    {
        base.OnReceivedServerPacket(packetid, data);
        ElectricalProgressive?.OnReceivedServerPacket(packetid, data);

        if (packetid != 1001)
            return;
        ((IClientWorldAccessor)Api.World).Player.InventoryManager.CloseInventory(Inventory);
        invDialog?.TryClose();
        invDialog?.Dispose();
        invDialog = null!;
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        var construct = GetBehavior<MachineConstruct>();
        if (construct is { IsRenderingBlueprint: true })
            return base.OnTesselation(mesher, tesselator);

        if (IsFormed)
            EnsureAnimatorReady();

        base.OnTesselation(mesher, tesselator);

        if (AnimUtil?.activeAnimationsByAnimCode == null ||
            !AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on"))
            return false;

        return true;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        Inventory.FromTreeAttributes(tree.GetTreeAttribute("_inventory"));
        RecipeProgress = tree.GetFloat("PowerCurrent");
        AccumulatedEnergy = tree.GetInt("accumulatedEnergy");
        IsForging = tree.GetBool("isForging");
        SelectedRecipeId = tree.GetInt("selectedRecipeId", -1);
        SelectedProductKey = tree.GetString("selectedProductKey") ?? "";

        if (Api != null)
        {
            Inventory.AfterBlocksLoaded(Api.World);
            ResolveSelectedRecipe();
        }

        if (Api is ICoreClientAPI)
        {
            EnsureAnimatorReady();
            _crateRenderer?.UpdateMesh();
            if (IsForging)
                StartAnimation();
            else
                StopAnimation();
            _clientDialog?.Update(RecipeProgress, CurrentRecipeName);
        }
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
        tree.SetInt("selectedRecipeId", SelectedRecipeId);
        tree.SetString("selectedProductKey", SelectedProductKey ?? "");
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
            ElectricalProgressive.Connection = Facing.None;

        _clientDialog?.TryClose();
        _clientDialog = null;
        _recipeSelector?.Dispose();
        _recipeSelector = null;
        DisposeCrateRenderer();
        StopAnimation();
        if (Api.Side == EnumAppSide.Client)
            AnimUtil?.Dispose();
        _mesh?.Dispose();
        _resultingShape = null;
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        _clientDialog?.TryClose();
        _recipeSelector?.Dispose();
        _recipeSelector = null;
        DisposeCrateRenderer();
        StopAnimation();
        if (Api?.Side == EnumAppSide.Client)
            AnimUtil?.Dispose();
        _mesh?.Dispose();
        _resultingShape = null;
        _capi = null;
    }

    private void DisposeCrateRenderer()
    {
        if (_capi != null && _crateRenderer != null)
            _capi.Event.UnregisterRenderer(_crateRenderer, EnumRenderStage.Opaque);
        _crateRenderer?.Dispose();
        _crateRenderer = null;
    }
}
