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
    private bool _loadPlaying;
    private bool _seenLoadStart;
    private bool _needRestart;
    private int _restartWaits;
    private int _craftSerial;
    private int _queuedSerial = -1;
    private Facing _facing = Facing.None;
    private bool _animatorReadyForFormed;
    private GuiDialog? _recipeSelector;
    private MetalFormingCrateRenderer? _crateRenderer;

    private BlockEntityAnimationUtil? AnimUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;

    public AnimatorBase? GetAnimator() => AnimUtil?.animator;

    public bool IsWorkAnimating => IsAnimPlaying("load") || IsAnimPlaying("forge");

    /// <summary>
    /// Кадр загрузки для предмета в руке. Во время отбивки деталь уже на ленте.
    /// </summary>
    public float GetWorkOnFrame()
    {
        if (IsAnimPlaying("forge"))
            return 400f;

        var frame = AnimFrame("load");
        return frame ?? -1f;
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

    public static bool IsIngot(ItemStack? stack)
    {
        var path = stack?.Collectible?.Code?.Path;
        return path != null && path.StartsWith("ingot-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsIngotRecipe(SmithingRecipe? recipe)
    {
        if (recipe == null)
            return false;

        var path = recipe.Ingredient?.Code?.Path;
        if (!string.IsNullOrEmpty(path) &&
            path.StartsWith("ingot", StringComparison.OrdinalIgnoreCase))
            return true;

        return IsIngot(recipe.Ingredient?.ResolvedItemStack);
    }

    public static bool IsWorkableInput(ItemStack? stack)
    {
        if (!IsIngot(stack) || stack!.Collectible is ItemWorkItem)
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
            MarkDirty();
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
        if (forging)
            _craftSerial++;
        MarkDirty(true);
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        inventory.LateInitialize(InventoryClassName + "-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);
        RegisterGameTickListener(Every1000Ms, 1000);
        if (api.Side == EnumAppSide.Client)
            RegisterGameTickListener(OnAnimTick, 50);
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
            .Where(IsIngotRecipe)
            .OrderBy(r => r.Output?.ResolvedItemstack?.Collectible?.Code?.ToString() ?? "")
            .ToList();
    }

    public List<SmithingRecipe> GetAllSmithingRecipes()
    {
        var list = Api?.GetSmithingRecipes();
        if (list == null || list.Count == 0)
            return [];

        return list
            .Where(r => r?.Output?.ResolvedItemstack != null && IsIngotRecipe(r))
            .ToList();
    }

    public static string ProductKey(SmithingRecipe? recipe)
    {
        var output = recipe?.Output?.ResolvedItemstack?.Collectible?.Code;
        if (output == null)
            return "";

        var metal = recipe!.Ingredient?.ResolvedItemStack?.Collectible?.LastCodePart();
        var path = output.Path;
        if (!string.IsNullOrEmpty(metal) &&
            path.EndsWith("-" + metal, StringComparison.OrdinalIgnoreCase))
            path = path[..^(metal.Length + 1)];

        return output.Domain + ":" + path;
    }

    public static string ProductDisplayName(ItemStack? stack) =>
        GenericCollectibleName(stack == null ? [] : [stack]);

    /// <summary>
    /// Общее имя без металла: «пластина», а не «висмутовая пластина».
    /// Берётся из слов, которые есть во всех вариантах.
    /// </summary>
    public static string GenericCollectibleName(IEnumerable<ItemStack?> stacks)
    {
        var names = new List<string>();
        foreach (var stack in stacks)
        {
            if (stack?.Collectible == null)
                continue;
            var name = stack.GetName();
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (names.Exists(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                continue;
            names.Add(name);
        }

        if (names.Count == 0)
            return "";
        if (names.Count == 1)
            return StripMaterialQualifier(names[0]);

        var lists = names.ConvertAll(SplitWords);
        var common = new HashSet<string>(lists[0].ConvertAll(w => w.ToLowerInvariant()));
        for (var i = 1; i < lists.Count; i++)
        {
            var set = new HashSet<string>(lists[i].ConvertAll(w => w.ToLowerInvariant()));
            common.IntersectWith(set);
        }

        foreach (var stop in new[] { "из", "of", "from", "the", "a", "an" })
            common.Remove(stop);

        var source = lists.OrderBy(l => l.Count).First();
        var words = new List<string>();
        foreach (var word in source)
        {
            if (!common.Contains(word.ToLowerInvariant()))
                continue;
            if (words.Exists(w => string.Equals(w, word, StringComparison.OrdinalIgnoreCase)))
                continue;
            words.Add(word);
        }

        if (words.Count == 0)
            return StripMaterialQualifier(names.OrderBy(n => n.Length).First());

        var text = string.Join(" ", words);
        return char.ToUpper(text[0]) + text[1..];
    }

    private static List<string> SplitWords(string name) =>
        name.Split([' ', '-', '—'], StringSplitOptions.RemoveEmptyEntries).ToList();

    private static string StripMaterialQualifier(string name)
    {
        var ofRu = name.IndexOf(" из ", StringComparison.OrdinalIgnoreCase);
        if (ofRu > 0)
            return name[..ofRu];
        var ofEn = name.IndexOf(" of ", StringComparison.OrdinalIgnoreCase);
        if (ofEn > 0)
            return name[..ofEn];

        var words = SplitWords(name);
        if (words.Count >= 2)
        {
            var last = words[^1];
            return char.ToUpper(last[0]) + last[1..];
        }

        return name;
    }

    public void OpenRecipeSelector()
    {
        if (Api is not ICoreClientAPI capi)
            return;

        var groups = GetAllSmithingRecipes()
            .GroupBy(ProductKey)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .OrderBy(g => GenericCollectibleName(g.Select(r => r.Output?.ResolvedItemstack)))
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
        for (var i = 0; i < groups.Count; i++)
        {
            var ingred = GetRecipeIngredientPreview(groups[i].First());
            if (ingred == null)
                continue;
            var material = GenericCollectibleName(groups[i].Select(r => r.Ingredient?.ResolvedItemStack));
            selector.SetIngredientCounts(i, ingred.StackSize, material);
        }
        _recipeSelector = selector;

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

        CurrentRecipeName = GenericCollectibleName(group.Select(g => g.Output?.ResolvedItemstack));

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
            return list.Any(r => IsIngotRecipe(r) && ProductKey(r) == SelectedProductKey);

        return list.Any(r => IsIngotRecipe(r) && r.RecipeId == SelectedRecipe!.RecipeId);
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
            if (EnergyOperation > 0)
            {
                RecipeProgress = AccumulatedEnergy / (float)EnergyOperation;
                UpdateState(RecipeProgress);
            }
        }
        else if (_wasCraftingLastTick && Api.Side == EnumAppSide.Server)
        {
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
            else
            {
                _craftSerial++;
            }

            UpdateState(RecipeProgress);
            MarkDirty();
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

    private void OnAnimTick(float dt)
    {
        if (Api?.Side != EnumAppSide.Client)
            return;

        if (!IsForging)
        {
            if (_loadPlaying || IsAnimPlaying("load") || IsAnimPlaying("forge"))
                StopAnimation();
            _needRestart = false;
            return;
        }

        if (_queuedSerial != _craftSerial)
        {
            _queuedSerial = _craftSerial;
            _needRestart = true;
            _restartWaits = 0;
            _seenLoadStart = false;
            _loadPlaying = false;
            StopOne("forge");
            StopOne("load");
            return;
        }

        if (_needRestart)
        {
            if (SelectedRecipe == null)
                return;

            // Пока прошлый клип ещё в аниматоре, новый старт игнорируется.
            if ((AnimFrame("load") != null || AnimFrame("forge") != null ||
                 IsAnimPlaying("load") || IsAnimPlaying("forge")) && _restartWaits < 4)
            {
                _restartWaits++;
                StopOne("forge");
                StopOne("load");
                return;
            }
            _restartWaits = 0;

            EnsureAnimatorReady();
            Play("load");
            if (!IsAnimPlaying("load"))
                return;

            _needRestart = false;
            _loadPlaying = true;
            return;
        }

        TryStartForge();
    }

    private void TryStartForge()
    {
        if (!_loadPlaying || IsAnimPlaying("forge"))
            return;

        var frame = AnimFrame("load") ?? -1f;
        if (!_seenLoadStart)
        {
            if (frame >= 0f && frame < 30f)
                _seenLoadStart = true;
            return;
        }

        if (frame < 414f)
            return;

        Play("forge");
    }

    private void Play(string code)
    {
        if (AnimUtil == null || IsAnimPlaying(code))
            return;

        AnimUtil.StartAnimation(new AnimationMetaData
        {
            Animation = code,
            Code = code,
            AnimationSpeed = 1.5f,
            EaseOutSpeed = 2f,
            EaseInSpeed = 1f
        });
    }

    private void StopAnimation()
    {
        _loadPlaying = false;
        StopOne("load");
        StopOne("forge");
    }

    private void StopOne(string code)
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
            return;
        if (IsAnimPlaying(code))
            AnimUtil.StopAnimation(code);
    }

    private bool IsAnimPlaying(string code) =>
        AnimUtil?.activeAnimationsByAnimCode != null &&
        AnimUtil.activeAnimationsByAnimCode.ContainsKey(code);

    private float? AnimFrame(string code)
    {
        var anims = AnimUtil?.animator?.Animations;
        if (anims == null)
            return null;

        foreach (var anim in anims)
        {
            if (anim?.Animation == null || !anim.Active)
                continue;
            if (anim.Animation.Code == code || anim.Animation.Name == code)
                return anim.CurrentFrame;
        }

        return null;
    }

    protected virtual void UpdateState(float recipeProgress)
    {
        if (Api != null && Api.Side == EnumAppSide.Client && _clientDialog != null && _clientDialog.IsOpened())
            _clientDialog.Update(recipeProgress, CurrentRecipeName);
        MarkDirty();
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

        if (!IsWorkAnimating)
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
        _craftSerial = tree.GetInt("craftSerial");
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
            if (!IsForging)
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
        tree.SetInt("craftSerial", _craftSerial);
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
