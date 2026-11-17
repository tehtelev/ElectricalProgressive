using ElectricalProgressive.RecipeSystem;
using ElectricalProgressive.RecipeSystem.Recipe;
using ElectricalProgressive.Utils;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using MachineConstruct = global::ElectricalProgressive.Construction.BEBehaviorMachineConstruct;

namespace ElectricalProgressive.Content.Block.EDistiller;

public class DistillJob
{
    public ItemStack Output = null!;
    public float MashLitres;
    public float WaterLitres;
    public float ProductLitres;
    public int Energy;
}

public class BlockEntityEDistiller : BlockEntityGenericTypedContainer
{
    internal InventoryEDistiller _inventory;
    private GuiDialogEDistiller? _clientDialog;
    private static MeshData? _mesh;
    private static Shape? _resultingShape;
    private ICoreClientAPI? _capi;
    private ILoadedSound? _ambientSound;
    private AssetLocation? _workSound;
    private bool _wasWorking;
    private readonly int _maxConsumption;
    private Facing _facing = Facing.None;

    public DistillJob? CurrentJob;
    public float RecipeProgress;
    public int AccumulatedEnergy;
    public float MashTemperature;
    public float TargetTemperature = 80f;

    public override string InventoryClassName => "edistiller";
    public override string DialogTitle => Lang.Get("game:edistiller-title-gui");
    public override InventoryBase Inventory => _inventory;

    public ItemSlot MashSlot => _inventory.MashSlot;
    public ItemSlot WaterSlot => _inventory.WaterSlot;
    public ItemSlot ProductSlot => _inventory.ProductSlot;

    public float MashCapacity => Attr("mashCapacityLitres", 100f);
    public float WaterCapacity => Attr("waterCapacityLitres", 50f);
    public float ProductCapacity => Attr("productCapacityLitres", 50f);
    public float WaterPerMashLitre => Attr("waterPerMashLitre", 0.25f);
    public float EnergyPerMashLitre => Attr("energyPerMashLitre", 4000f);
    public float BoilTempMin => Attr("boilTempMin", 70f);
    public float BoilTempMax => Attr("boilTempMax", 100f);
    public int CurrentEnergyCost => CurrentJob?.Energy ?? 0;
    public int MaxConsumption => _maxConsumption;
    public bool IsHotEnough => MashTemperature + 0.5f >= TargetTemperature;

    public float MashLitres => LitresOf(MashSlot);
    public float WaterLitres => LitresOf(WaterSlot);
    public float ProductLitres => LitresOf(ProductSlot);

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

    private BlockEntityAnimationUtil? AnimUtil => GetBehavior<BEBehaviorAnimatable>()?.animUtil;
    public BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();

    public Facing Facing
    {
        get => _facing;
        set
        {
            if (value != _facing)
                ElectricalProgressive!.Connection = FacingHelper.FullFace(_facing = value);
        }
    }

    public BlockEntityEDistiller()
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(Block, "maxConsumption", 100);
        _inventory = new InventoryEDistiller();
        _inventory.SlotModified += OnSlotModified;
    }

    private float Attr(string key, float fallback)
        => Block?.Attributes?[key].Exists == true ? Block.Attributes[key].AsFloat(fallback) : fallback;

    public static float LitresOf(ItemSlot slot)
    {
        if (slot == null || slot.Empty)
            return 0f;
        var props = BlockLiquidContainerBase.GetContainableProps(slot.Itemstack);
        if (props == null || props.ItemsPerLitre <= 0)
            return 0f;
        return slot.Itemstack.StackSize / props.ItemsPerLitre;
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        _inventory.LateInitialize(InventoryClassName + "-" + Pos, api);
        _inventory.SetBlockPos(Pos);
        if (MashTemperature <= 0.01f)
            MashTemperature = GetAmbientTemperature();
        TargetTemperature = 80f;
        RegisterGameTickListener(Every1000Ms, 1000);

        if (api.Side == EnumAppSide.Client)
        {
            _capi = api as ICoreClientAPI;
            if (AnimUtil != null)
            {
                PrepareAnimUtil(api, InventoryClassName);
                AnimUtil.InitializeAnimator(InventoryClassName, _mesh, _resultingShape, new Vec3f(0, GetRotation(), 0f));
            }

            _workSound = new AssetLocation("electricalprogressiveindustry:sounds/erecycler/erecycler.ogg");
        }
    }

    private void PrepareAnimUtil(ICoreAPI api, string cacheDictKey)
    {
        if (_mesh != null && _resultingShape != null)
            return;
        if (Block?.Shape?.Base == null || AnimUtil == null)
            return;

        var shapePath = Block.Shape.Base.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
        var shape = Shape.TryGet(api, shapePath);
        if (shape == null)
            return;
        _mesh = AnimUtil.CreateMesh(cacheDictKey, shape, out _resultingShape, null);
    }

    public int GetRotation()
    {
        var side = Block.Variant["side"];
        var adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
        return adjustedIndex * 90;
    }

    private void OnSlotModified(int slotid)
    {
        if (slotid == InventoryEDistiller.MashSlotId || slotid == InventoryEDistiller.WaterSlotId)
        {
            RecipeProgress = 0;
            AccumulatedEnergy = 0;
        }

        RefreshJob();
        MarkDirty();
        if (Api?.Side == EnumAppSide.Client)
            _clientDialog?.Update();
    }

    public float GetAmbientTemperature()
    {
        var climate = Api?.World.BlockAccessor.GetClimateAt(Pos, EnumGetClimateMode.NowValues);
        return climate?.Temperature ?? 20f;
    }

    public bool CanHeat() => StructureComplete && MashLitres >= 0.1f;

    public bool CanDistill()
    {
        if (!CanHeat() || !IsHotEnough)
            return false;
        RefreshJob();
        return CurrentJob != null;
    }

    public bool CanWork()
    {
        if (!CanHeat())
            return false;
        if (!IsHotEnough)
            return true;
        RefreshJob();
        return CurrentJob != null;
    }

    public void RefreshJob()
    {
        CurrentJob = FindJob();
    }

    public DistillJob? FindJob()
    {
        if (MashSlot.Empty)
            return null;

        var mashLitresAvail = MashLitres;
        if (mashLitresAvail < 0.1f)
            return null;

        var waterAvail = WaterLitres;
        var props = BlockLiquidContainerBase.GetContainableProps(MashSlot.Itemstack);
        if (props == null)
            return null;

        if (TryVanillaDistillation(out var output, out var ratio) && output != null)
        {
            var mash = Math.Min(1f, mashLitresAvail);
            var waterNeed = mash * WaterPerMashLitre;
            if (waterAvail < waterNeed)
                return null;
            var productNeed = mash * ratio;
            if (!CanAcceptProduct(output, productNeed))
                return null;
            return new DistillJob
            {
                Output = output,
                MashLitres = mash,
                WaterLitres = waterNeed,
                ProductLitres = productNeed,
                Energy = Math.Max(1, (int)(mash * EnergyPerMashLitre))
            };
        }

        if (ElectricalProgressiveRecipeManager.DistillerRecipes == null)
            return null;

        foreach (var recipe in ElectricalProgressiveRecipeManager.DistillerRecipes)
        {
            if (recipe.Ingredients == null || recipe.Ingredients.Length == 0)
                continue;
            if (!recipe.Ingredients[0].SatisfiesAsIngredient(MashSlot.Itemstack))
                continue;
            if (recipe.Outputs == null || recipe.Outputs.Length == 0 || recipe.Outputs[0].ResolvedItemstack == null)
                continue;

            var mash = Math.Min(recipe.MashLitres <= 0 ? 1f : recipe.MashLitres, mashLitresAvail);
            if (mash < 0.1f)
                continue;
            var scale = mash / (recipe.MashLitres <= 0 ? 1f : recipe.MashLitres);
            var waterNeed = recipe.WaterLitres * scale;
            if (waterAvail < waterNeed)
                continue;
            var productNeed = recipe.ProductLitres * scale;
            var outStack = recipe.Outputs[0].ResolvedItemstack.Clone();
            if (!CanAcceptProduct(outStack, productNeed))
                continue;

            return new DistillJob
            {
                Output = outStack,
                MashLitres = mash,
                WaterLitres = waterNeed,
                ProductLitres = productNeed,
                Energy = Math.Max(1, (int)(recipe.EnergyOperation * scale))
            };
        }

        return null;
    }

    private bool CanAcceptProduct(ItemStack output, float litres)
    {
        var outProps = BlockLiquidContainerBase.GetContainableProps(output);
        if (outProps == null)
            return false;
        if (ProductSlot.Empty)
            return litres <= ProductCapacity;
        if (!ProductSlot.Itemstack.Equals(Api.World, output, GlobalConstants.IgnoredStackAttributes))
            return false;
        return ProductLitres + litres <= ProductCapacity + 0.001f;
    }

    private bool TryVanillaDistillation(out ItemStack? distilled, out float ratio)
    {
        distilled = null;
        ratio = 0f;
        var stack = MashSlot.Itemstack;
        var attrs = stack?.Collectible?.Attributes;
        if (attrs == null)
            return false;

        JsonObject? props = null;
        if (attrs.KeyExists("distillationPropsbytype"))
        {
            var byType = attrs["distillationPropsbytype"];
            var path = stack!.Collectible.Code.Path;
            if (byType.KeyExists(path))
                props = byType[path];
            else if (byType.KeyExists("*"))
                props = byType["*"];
        }

        if (props == null && attrs.KeyExists("distillationProps"))
            props = attrs["distillationProps"];
        if (props == null || !props.KeyExists("distilledStack"))
            return false;

        ratio = props["ratio"].AsFloat(0.1f);
        var jsonStack = props["distilledStack"].AsObject<JsonItemStack>(null!, stack!.Collectible.Code.Domain);
        if (jsonStack?.Code == null)
            return false;

        if (stack.Collectible.Variant != null && stack.Collectible.Variant.TryGetValue("fruit", out var fruit))
            jsonStack.Code = new AssetLocation(jsonStack.Code.Domain, jsonStack.Code.Path.Replace("{fruit}", fruit));

        if (!jsonStack.Resolve(Api.World, "edistiller"))
            return false;

        distilled = jsonStack.ResolvedItemstack?.Clone();
        return distilled != null;
    }

    public void AddEnergy(int amount)
    {
        if (!StructureComplete || amount <= 0 || !IsHotEnough)
            return;

        RefreshJob();
        if (CurrentJob == null)
            return;

        var needed = CurrentJob.Energy - AccumulatedEnergy;
        if (needed <= 0)
        {
            FinishBatch();
            return;
        }

        AccumulatedEnergy += Math.Min(amount, needed);
        RecipeProgress = CurrentJob.Energy <= 0 ? 1f : AccumulatedEnergy / (float)CurrentJob.Energy;
        if (AccumulatedEnergy >= CurrentJob.Energy)
            FinishBatch();

        MarkDirty(true);
        if (Api?.Side == EnumAppSide.Client)
            _clientDialog?.Update();
    }

    private void FinishBatch()
    {
        RefreshJob();
        if (CurrentJob == null)
        {
            AccumulatedEnergy = 0;
            RecipeProgress = 0;
            return;
        }

        TakeLitres(MashSlot, CurrentJob.MashLitres);
        TakeLitres(WaterSlot, CurrentJob.WaterLitres);
        PutLitres(ProductSlot, CurrentJob.Output, CurrentJob.ProductLitres);

        AccumulatedEnergy = 0;
        RecipeProgress = 0;
        RefreshJob();
        MarkDirty(true);
    }

    public int PutLitres(ItemSlot slot, ItemStack liquid, float litres)
    {
        if (liquid == null || litres <= 0)
            return 0;
        var props = BlockLiquidContainerBase.GetContainableProps(liquid);
        if (props == null)
            return 0;
        var items = Math.Max(1, (int)Math.Round(litres * props.ItemsPerLitre, MidpointRounding.AwayFromZero));
        if (slot.Empty)
        {
            var placed = liquid.Clone();
            placed.StackSize = items;
            slot.Itemstack = placed;
            slot.MarkDirty();
            return items;
        }

        if (!slot.Itemstack.Equals(Api.World, liquid, GlobalConstants.IgnoredStackAttributes))
            return 0;
        slot.Itemstack.StackSize += items;
        slot.MarkDirty();
        return items;
    }

    public int TakeLitres(ItemSlot slot, float litres)
    {
        if (slot.Empty || litres <= 0)
            return 0;
        var props = BlockLiquidContainerBase.GetContainableProps(slot.Itemstack);
        if (props == null)
            return 0;
        var items = Math.Max(1, (int)Math.Round(litres * props.ItemsPerLitre, MidpointRounding.AwayFromZero));
        items = Math.Min(items, slot.StackSize);
        slot.TakeOut(items);
        slot.MarkDirty();
        return items;
    }

    public int TryPutIntoTank(ItemStack liquidStack, float desiredLitres, bool waterPreferred)
    {
        if (liquidStack == null)
            return 0;
        var path = liquidStack.Collectible.Code.Path;
        ItemSlot slot;
        float cap;
        if (path.Contains("water") || waterPreferred && path.Contains("water"))
        {
            slot = WaterSlot;
            cap = WaterCapacity;
        }
        else
        {
            slot = MashSlot;
            cap = MashCapacity;
        }

        var props = BlockLiquidContainerBase.GetContainableProps(liquidStack);
        if (props == null || !props.Containable)
            return 0;

        var desiredItems = (int)(props.ItemsPerLitre * desiredLitres);
        var maxItems = cap * props.ItemsPerLitre;
        if (slot.Empty)
        {
            var moved = (int)GameMath.Min(desiredItems, maxItems, liquidStack.StackSize);
            if (moved <= 0)
                return 0;
            var placed = liquidStack.Clone();
            placed.StackSize = moved;
            slot.Itemstack = placed;
            slot.MarkDirty();
            MarkDirty();
            return moved;
        }

        if (!slot.Itemstack.Equals(Api.World, liquidStack, GlobalConstants.IgnoredStackAttributes))
            return 0;
        var placeable = (int)Math.Min(liquidStack.StackSize, maxItems - slot.StackSize);
        var take = Math.Min(placeable, desiredItems);
        if (take <= 0)
            return 0;
        slot.Itemstack.StackSize += take;
        slot.MarkDirty();
        MarkDirty();
        return take;
    }

    public ItemStack? TryTakeFromProduct(int quantityItems)
    {
        if (ProductSlot.Empty)
            return null;
        var take = Math.Min(quantityItems, ProductSlot.StackSize);
        var taken = ProductSlot.Itemstack.Clone();
        taken.StackSize = take;
        ProductSlot.TakeOut(take);
        ProductSlot.MarkDirty();
        MarkDirty();
        return taken;
    }

    private static string T(string key) => Lang.Get("electricalprogressiveindustry:" + key);
    private static string T(string key, params object[] args) => Lang.Get("electricalprogressiveindustry:" + key, args);

    public string LiquidName(ItemSlot slot)
        => slot == null || slot.Empty ? T("edistiller-empty") : slot.Itemstack.GetName();

    public string GetStatusKey()
    {
        if (!StructureComplete)
            return "electricalprogressivebasics:structure-incomplete";
        var beh = GetBehavior<BEBehaviorEDistiller>();
        var powered = beh != null && beh.PowerSetting >= _maxConsumption * 0.1f;
        if (MashSlot.Empty)
            return "electricalprogressiveindustry:edistiller-need-mash";
        if (!powered && CanWork())
            return "electricalprogressiveindustry:edistiller-nopower";
        if (!IsHotEnough)
            return "electricalprogressiveindustry:edistiller-heating";
        if (WaterSlot.Empty)
            return "electricalprogressiveindustry:edistiller-need-water";
        RefreshJob();
        if (CurrentJob == null)
            return "electricalprogressiveindustry:edistiller-no-recipe";
        if (!CanAcceptProduct(CurrentJob.Output, CurrentJob.ProductLitres))
            return "electricalprogressiveindustry:edistiller-product-full";
        if (powered)
            return "electricalprogressiveindustry:edistiller-working";
        return "electricalprogressiveindustry:edistiller-idle";
    }

    public string GetGuiText()
    {
        var beh = GetBehavior<BEBehaviorEDistiller>();
        var power = beh?.PowerSetting ?? 0;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Lang.Get(GetStatusKey()));
        sb.AppendLine(T("edistiller-temp-value", MashTemperature.ToString("0"), TargetTemperature.ToString("0")));
        sb.AppendLine(T("edistiller-input") + ": " + LiquidName(MashSlot));
        sb.AppendLine("  " + T("edistiller-litres", MashLitres.ToString("0.#"), MashCapacity.ToString("0")));
        sb.AppendLine(T("edistiller-output") + ": " + (CurrentJob != null ? CurrentJob.Output.GetName() : LiquidName(ProductSlot)));
        sb.AppendLine("  " + T("edistiller-litres", ProductLitres.ToString("0.#"), ProductCapacity.ToString("0")));
        sb.AppendLine(T("edistiller-power-value", power, _maxConsumption));
        sb.AppendLine(T("edistiller-use-water", WaterPerMashLitre.ToString("0.##")));
        sb.Append(T("edistiller-use-energy", EnergyPerMashLitre.ToString("0")));
        if (CanDistill() && power > 0 && CurrentJob is { Energy: > 0 })
        {
            var batchesPerHour = power * 3600f / CurrentJob.Energy;
            sb.AppendLine();
            sb.AppendLine(T("edistiller-rate-mash", (batchesPerHour * CurrentJob.MashLitres).ToString("0.##")));
            sb.AppendLine(T("edistiller-rate-water", (batchesPerHour * CurrentJob.WaterLitres).ToString("0.##")));
            sb.Append(T("edistiller-rate-product", (batchesPerHour * CurrentJob.ProductLitres).ToString("0.##")));
        }

        return sb.ToString();
    }

    private void TickTemperature(float dt)
    {
        if (Api?.Side != EnumAppSide.Server)
            return;

        var ambient = GetAmbientTemperature();
        if (MashLitres < 0.05f)
        {
            MashTemperature = ambient;
            return;
        }

        var beh = GetBehavior<BEBehaviorEDistiller>();
        var frac = beh == null || _maxConsumption <= 0 ? 0f : beh.PowerSetting / (float)_maxConsumption;
        if (frac > 0.05f)
        {
            var rate = frac * 48f / Math.Max(10f, MashLitres);
            MashTemperature = Math.Min(TargetTemperature + 1.5f, MashTemperature + rate * dt);
        }
        else
        {
            var cool = 1f - MathF.Exp(-0.08f * dt);
            MashTemperature += (ambient - MashTemperature) * cool;
        }
    }

    private void Every1000Ms(float dt)
    {
        TickTemperature(dt);
        var beh = GetBehavior<BEBehaviorEDistiller>();
        var working = beh != null && CanWork() && beh.PowerSetting >= _maxConsumption * 0.1f;
        var distilling = working && CanDistill();
        if (distilling)
        {
            if (!_wasWorking)
                StartSound();
            StartAnimation();
            if (CurrentJob != null && CurrentJob.Energy > 0)
                RecipeProgress = AccumulatedEnergy / (float)CurrentJob.Energy;
        }
        else if (_wasWorking)
        {
            StopAnimation();
            StopSound();
        }

        _wasWorking = distilling;
        MarkDirty(true);
        if (Api?.Side == EnumAppSide.Client)
            _clientDialog?.Update();
    }

    private void StartAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
            return;
        if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on"))
            return;
        AnimUtil.StartAnimation(new AnimationMetaData
        {
            Animation = "work-on",
            Code = "work-on",
            AnimationSpeed = 1f,
            EaseOutSpeed = 4f,
            EaseInSpeed = 1f
        });
    }

    private void StopAnimation()
    {
        if (Api?.Side != EnumAppSide.Client || AnimUtil == null)
            return;
        if (AnimUtil.activeAnimationsByAnimCode.ContainsKey("work-on"))
            AnimUtil.StopAnimation("work-on");
    }

    private void StartSound()
    {
        if (_ambientSound != null || Api?.Side != EnumAppSide.Client || _workSound == null)
            return;
        _ambientSound = _capi!.World.LoadSound(new SoundParams
        {
            Location = _workSound,
            ShouldLoop = true,
            Position = Pos.ToVec3f().Add(0.5f, 0.25f, 0.5f),
            DisposeOnFinish = false,
            Volume = 0.8f
        });
        _ambientSound.Start();
    }

    private void StopSound()
    {
        _ambientSound?.Stop();
        _ambientSound?.Dispose();
        _ambientSound = null;
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (!StructureComplete)
            return true;
        if (Api.Side == EnumAppSide.Client)
        {
            toggleInventoryDialogClient(byPlayer, () =>
            {
                _clientDialog = new GuiDialogEDistiller(DialogTitle, Inventory, Pos, Api as ICoreClientAPI, this);
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
        (Api.World as IClientWorldAccessor)?.Player.InventoryManager.CloseInventory(Inventory);
        invDialog?.TryClose();
        invDialog?.Dispose();
        invDialog = null;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        Inventory.FromTreeAttributes(tree.GetTreeAttribute("_inventory"));
        RecipeProgress = tree.GetFloat("PowerCurrent");
        AccumulatedEnergy = tree.GetInt("accumulatedEnergy");
        MashTemperature = tree.GetFloat("mashTemp", MashTemperature);
        TargetTemperature = 80f;
        if (Api != null)
        {
            Inventory.AfterBlocksLoaded(Api.World);
            RefreshJob();
        }
        if (Api?.Side == EnumAppSide.Client)
            _clientDialog?.Update();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        var inv = new TreeAttribute();
        Inventory.ToTreeAttributes(inv);
        tree["_inventory"] = inv;
        tree.SetFloat("PowerCurrent", RecipeProgress);
        tree.SetInt("accumulatedEnergy", AccumulatedEnergy);
        tree.SetFloat("mashTemp", MashTemperature);
        tree.SetFloat("targetTemp", TargetTemperature);
    }

    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);
        if (ElectricalProgressive != null && byItemStack != null)
            LoadEProperties.Load(Block, this);
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        if (ElectricalProgressive != null)
            ElectricalProgressive.Connection = Facing.None;
        _clientDialog?.TryClose();
        _clientDialog = null;
        StopAnimation();
        StopSound();
        if (Api.Side == EnumAppSide.Client)
            AnimUtil?.Dispose();
        _mesh?.Dispose();
        _resultingShape = null;
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        _clientDialog?.TryClose();
        StopSound();
        _mesh?.Dispose();
        _resultingShape = null;
    }
}
