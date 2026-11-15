using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.ECloche;

public class BlockEntityECloche : BlockEntityOpenableContainer
{
    public const int OutputCount = 4;
    public const int TotalSlots = 6;
    public const float CompostNutrient = 65f;

    private const float ItemsPerLitre = 100f;
    private const float PlantSoilY = 7.05f / 16f;
    private const float PlantMaxHeight = 0.88f;
    private const float PlantMaxWidth = 0.50f;
    private const float LampWatts = 6f;
    private const float WattsPerDegree = 1.8f;
    private const float WaterWatts = 8f;
    private const float FertWatts = 10f;
    private const float WaterStart = 0.85f;
    private const float WaterStop = 0.95f;

    private static readonly object PlantLock = new();

    private static readonly Vec3f[] WaterTips =
    {
        new(8.0f, 26.35f, 2.675f),
        new(8.0f, 26.35f, 13.325f),
        new(2.675f, 26.35f, 8.0f),
        new(13.325f, 26.35f, 8.0f)
    };

    private static readonly SimpleParticleProperties WaterDrops = new()
    {
        MinVelocity = new Vec3f(-0.02f, -0.4f, -0.02f),
        AddVelocity = new Vec3f(0.04f, 0.05f, 0.04f),
        LifeLength = 1.5f,
        addLifeLength = 0.25f,
        MinQuantity = 1f,
        AddQuantity = 1.5f,
        GravityEffect = 0.55f,
        MinSize = 0.12f,
        MaxSize = 0.2f,
        ParticleModel = EnumParticleModel.Quad,
        DieOnRainHeightmap = false,
        ShouldDieInAir = false,
        WithTerrainCollision = false,
        WindAffectednes = 0f,
        SelfPropelled = true
    };
    private static readonly Dictionary<int, MeshData> PlantCache = new();

    private readonly InventoryECloche _inventory;
    private ItemStack[] _pending;
    private ItemStack _water;
    private bool _ready;
    private bool _suppress;
    private bool _plantChanged;
    private long _nextSync;
    private float _capacity = 10f;
    private int _maxConsumption = 40;
    private float _fertProgress;
    private bool _shownMoist;
    private bool _wantWater;
    private string _seedKey = "";
    private double _lastTotalHours;
    private static MeshData _farmDryMesh;
    private static MeshData _farmMoistMesh;

    public float Progress { get; private set; }
    public float Litres { get; private set; }
    public string PlantCode { get; private set; }
    public float NutrientN { get; private set; } = CompostNutrient;
    public float NutrientP { get; private set; } = CompostNutrient;
    public float NutrientK { get; private set; } = CompostNutrient;
    public float Moisture { get; private set; } = 0.35f;
    public float InsideTemp { get; private set; } = 15f;
    public float OutsideTemp { get; private set; } = 15f;
    public float OptimalTemp { get; private set; } = 18f;
    public bool Watering { get; private set; }
    public bool Fertilizing { get; private set; }
    public bool LampOn { get; set; }
    public float DesiredWatts { get; private set; }
    public float LastPowerRatio { get; private set; }

    public float Capacity => _capacity;
    public int MaxConsumption => _maxConsumption;
    public bool WaitingForOutput => _pending != null;
    public bool AutoReplant { get; private set; }
    public bool HasCrop => IsSeed(Api?.World, CropSeed);

    private ItemStack CropSeed => _planted;
    private ItemStack _planted;

    public override string InventoryClassName => "ecloche";
    public override InventoryBase Inventory => _inventory;
    public string GuiTitle => Lang.Get("ecloche-title-gui");

    public ItemSlot SeedSlot => _inventory[0];
    public ItemSlot FertilizerSlot => _inventory[1];

    public BlockEntityECloche()
    {
        _inventory = new InventoryECloche(this);
        _inventory.SlotModified += OnSlotModified;
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        _inventory.Pos = Pos;
        _inventory.LateInitialize(InventoryClassName + "-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);
        _inventory.AfterBlocksLoaded(api.World);
        if (Block?.Attributes?["transitionSpeedMul"].Exists == true)
            _inventory.TransitionableSpeedMulByType = Block.Attributes["transitionSpeedMul"].AsObject<Dictionary<EnumTransitionType, float>>();
        if (Block?.Attributes?["spoilSpeedMulByFoodCat"].Exists == true)
            _inventory.PerishableFactorByFoodCategory = Block.Attributes["spoilSpeedMulByFoodCat"].AsObject<Dictionary<EnumFoodCategory, float>>();
        _capacity = MyMiniLib.GetAttributeFloat(Block, "capacityLitres", 10f);
        _maxConsumption = MyMiniLib.GetAttributeInt(Block, "maxConsumption", 40);
        if (Litres > _capacity)
            Litres = _capacity;
        SyncWaterStack();
        RefreshClimate();
        _shownMoist = Moisture >= 0.55f;

        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(ServerTick, 500);
        else
            RegisterGameTickListener(ClientTick, 50);

        _seedKey = SeedKey();
        if (_lastTotalHours <= 0 && api.World?.Calendar != null)
            _lastTotalHours = api.World.Calendar.TotalHours;
        _ready = true;
    }

    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);
        NutrientN = NutrientP = NutrientK = CompostNutrient;
        Moisture = 0.35f;
        Progress = 0f;
        if (ElectricalProgressive != null)
            LoadEProperties.Load(Block, this);
    }

    public BEBehaviorElectricalProgressive ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            toggleInventoryDialogClient(byPlayer, () =>
            {
                invDialog = new GuiDialogECloche(GuiTitle, _inventory, Pos, Api as ICoreClientAPI);
                return invDialog;
            });
        }

        return true;
    }

    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
    {
        if (packetid == 1001)
        {
            AutoReplant = !AutoReplant;
            MarkDirty(true);
            return;
        }

        base.OnReceivedClientPacket(fromPlayer, packetid, data);
    }

    public override void OnBlockRemoved()
    {
        SpillPending();
        if (Api?.Side == EnumAppSide.Server && _planted != null)
            Api.World.SpawnItemEntity(_planted, Pos.ToVec3d().Add(0.5, 0.5, 0.5));

        if (invDialog != null)
        {
            invDialog.TryClose();
            invDialog.Dispose();
            invDialog = null;
        }

        if (ElectricalProgressive != null)
            ElectricalProgressive.Connection = Facing.None;

        base.OnBlockRemoved();
    }

    private void UpdateWantWater()
    {
        if (!HasCrop || Litres <= 0.05f)
        {
            _wantWater = false;
            return;
        }

        if (Moisture <= WaterStart)
            _wantWater = true;
        else if (Moisture >= WaterStop)
            _wantWater = false;
    }

    public bool NeedsGrowLamp()
    {
        if (Api?.World == null)
            return false;

        var at = Pos.UpCopy();
        int sun = Api.World.BlockAccessor.GetLightLevel(at, EnumLightLevelType.TimeOfDaySunLight);
        return sun < 10;
    }

    private void ClientTick(float dt)
    {
        if (!Watering || Api?.World == null)
            return;

        WaterDrops.Color = ColorUtil.ToRgba(160, 45, 110, 190);
        WaterDrops.AddPos = new Vec3d(0.03, 0.02, 0.03);
        foreach (var tip in WaterTips)
        {
            WaterDrops.MinPos = TipWorld(tip);
            Api.World.SpawnParticles(WaterDrops);
        }
    }

    private Vec3d TipWorld(Vec3f voxel)
    {
        float lx = voxel.X / 16f - 0.5f;
        float ly = voxel.Y / 16f;
        float lz = voxel.Z / 16f - 0.5f;
        float deg = Block.Variant["side"] switch
        {
            "east" => 270f,
            "south" => 180f,
            "west" => 90f,
            _ => 0f
        };
        float rad = deg * GameMath.DEG2RAD;
        float c = GameMath.Cos(rad);
        float s = GameMath.Sin(rad);
        float nx = lx * c + lz * s;
        float nz = -lx * s + lz * c;
        return Pos.ToVec3d().Add(nx + 0.5f, ly, nz + 0.5f);
    }

    public float ComputeDesiredWatts()
    {
        RefreshClimate();
        float watts = 0f;
        if (HasCrop && NeedsGrowLamp())
            watts += LampWatts;

        float dT = Math.Abs(OutsideTemp - OptimalTemp);
        watts += Math.Min(28f, dT * WattsPerDegree);

        UpdateWantWater();
        if (_wantWater)
            watts += WaterWatts;

        Fertilizing = CanApplyFertilizer();
        if (Fertilizing)
            watts += FertWatts;

        DesiredWatts = Math.Min(_maxConsumption, watts);
        return DesiredWatts;
    }

    public void SetReceivedPower(float watts)
    {
        float r = DesiredWatts > 0.01f ? GameMath.Clamp(watts / DesiredWatts, 0f, 1.5f) : 0f;
        if (r >= 0.08f)
            LastPowerRatio = r;
        else
            LastPowerRatio = Math.Max(0f, LastPowerRatio - 0.08f);
    }

    private void Simulate(float hours, float powerRatio)
    {
        RefreshClimate();
        bool skip = hours >= 0.35f;
        bool powered = powerRatio > 0.2f;

        if (skip && powered)
            InsideTemp += (OptimalTemp - InsideTemp) * GameMath.Clamp(hours * 0.5f, 0f, 1f);
        else if (skip)
            InsideTemp += (OutsideTemp - InsideTemp) * 0.2f;
        else
        {
            float leak = GameMath.Clamp(0.22f * hours, 0f, 1f);
            InsideTemp += (OutsideTemp - InsideTemp) * leak;
            float dT = OptimalTemp - InsideTemp;
            if (Math.Abs(dT) > 0.4f && powered)
                InsideTemp += dT * GameMath.Clamp(hours * 0.7f * powerRatio, 0f, 1f);
        }

        float evaporate = hours * (0.012f + Math.Max(0f, InsideTemp) * 0.0003f);
        Moisture = GameMath.Clamp(Moisture - evaporate, 0f, 1f);

        UpdateWantWater();
        Watering = _wantWater && powerRatio > 0.2f && Litres > 0.01f;
        if (Watering)
        {
            float give = hours * 0.07f * powerRatio;
            float litres = give * 4f;
            if (Litres < litres)
            {
                give *= Litres / litres;
                litres = Litres;
            }

            Litres -= litres;
            Moisture = GameMath.Clamp(Moisture + give, 0f, 1f);
            SyncWaterStack();
        }

        bool moist = Moisture >= 0.55f;
        if (moist != _shownMoist)
        {
            _shownMoist = moist;
            _plantChanged = true;
        }

        if (Fertilizing && powerRatio > 0.25f)
        {
            _fertProgress += hours * 0.45f * powerRatio;
            if (_fertProgress >= 1f)
            {
                _fertProgress = 0f;
                ApplyFertilizerItem();
            }
        }
        else
            _fertProgress = Math.Max(0f, _fertProgress - hours);

        bool lamp = HasCrop && powered && (skip || NeedsGrowLamp());
        if (lamp != LampOn)
            _plantChanged = true;
        LampOn = lamp;
        Grow(hours, powerRatio);
        UpdatePlantCode();
    }

    private void Grow(float hours, float powerRatio)
    {
        if (!TryResolve(Api.World, CropSeed, out var stage, out _) || _pending != null || Progress >= 1f)
            return;

        var props = stage.CropProps;
        float need = GrowthNeedHours(props);
        float mul = GrowthMul(props, powerRatio);
        if (mul <= 0.001f)
            return;

        float gain = hours * mul / need;
        float consumed = props.NutrientConsumption * gain;
        SpendNutrient(props.RequiredNutrient, consumed);
        Progress = Math.Min(1f, Progress + gain);
    }

    private float TempMul(float temp, BlockCropProperties props)
    {
        float cold = props.ColdDamageBelow;
        float heat = props.HeatDamageAbove > 0.01f ? props.HeatDamageAbove : 32f;
        if (temp < cold)
            return 0.02f;
        if (temp > heat)
            return 0.05f;

        float dist = Math.Abs(temp - OptimalTemp);
        float span = Math.Max(6f, (heat - cold) * 0.35f);
        return GameMath.Clamp(1f - dist / span, 0.15f, 1f);
    }

    private float ConditionMul(BlockCropProperties props)
    {
        float moistFactor = GameMath.Clamp((Moisture - 0.3f) / 0.55f, 0f, 1f);
        float req = NutrientOf(props.RequiredNutrient) / 100f;
        float avg = (NutrientN + NutrientP + NutrientK) / 300f;
        float nScore = 0.65f * req + 0.35f * avg;
        float nFactor = GameMath.Clamp(nScore / 0.65f, 0f, 1.2f);
        float mix = GameMath.Clamp(0.5f * moistFactor + 0.5f * Math.Min(nFactor, 1f), 0f, 1f);
        float mul = 1f + 2f * mix;
        if (nFactor > 1f)
            mul += 0.25f * (nFactor - 1f);
        if (Moisture < 0.25f)
            mul *= 0.15f;
        return GameMath.Clamp(mul, 0.08f, 3.2f);
    }

    private float GrowthMul(BlockCropProperties props, float powerRatio)
    {
        float cond = ConditionMul(props);
        float tMul = TempMul(InsideTemp, props);
        float lightMul = LampOn || !NeedsGrowLamp() ? 1f : 0.08f;
        return cond * tMul * lightMul * GameMath.Clamp(powerRatio, 0f, 1f);
    }

    public float EstimateRemainingHours()
    {
        if (!TryResolve(Api?.World, CropSeed, out var stage, out _) || Progress >= 1f)
            return -1f;

        var props = stage.CropProps;
        float power = LastPowerRatio >= 0.12f ? 1f : LastPowerRatio;
        if (power <= 0.01f)
            return -1f;
        float mul = GrowthMul(props, power);
        if (mul <= 0.001f)
            return -1f;

        float need = GrowthNeedHours(props);
        return (1f - Progress) * need / mul;
    }

    public string RemainingLabel()
    {
        float hours = EstimateRemainingHours();
        if (hours < 0f)
            return Lang.Get("ecloche-eta-stall");
        if (hours >= 24f)
            return Lang.Get("ecloche-eta-d", (hours / 24f).ToString("0.0"));
        if (hours >= 1.5f)
            return Lang.Get("ecloche-eta-h", hours.ToString("0.0"));
        return Lang.Get("ecloche-eta-m", Math.Max(1, Math.Ceiling(hours * 60f)).ToString("0"));
    }

    private float NutrientOf(EnumSoilNutrient kind)
    {
        return kind switch
        {
            EnumSoilNutrient.N => NutrientN,
            EnumSoilNutrient.P => NutrientP,
            EnumSoilNutrient.K => NutrientK,
            _ => NutrientN
        };
    }

    private void SpendNutrient(EnumSoilNutrient kind, float amount)
    {
        switch (kind)
        {
            case EnumSoilNutrient.N:
                NutrientN = Math.Max(0f, NutrientN - amount);
                break;
            case EnumSoilNutrient.P:
                NutrientP = Math.Max(0f, NutrientP - amount);
                break;
            default:
                NutrientK = Math.Max(0f, NutrientK - amount);
                break;
        }
    }

    private bool CanApplyFertilizer()
    {
        if (FertilizerSlot.Empty || !IsFertilizer(FertilizerSlot.Itemstack))
            return false;

        return NutrientN < 95f || NutrientP < 95f || NutrientK < 95f;
    }

    private void ApplyFertilizerItem()
    {
        var stack = FertilizerSlot.Itemstack;
        if (stack?.Collectible?.Attributes == null)
            return;

        var fp = stack.Collectible.Attributes["fertilizerProps"];
        if (!fp.Exists)
            return;

        NutrientN = Math.Min(100f, NutrientN + fp["n"].AsFloat());
        NutrientP = Math.Min(100f, NutrientP + fp["p"].AsFloat());
        NutrientK = Math.Min(100f, NutrientK + fp["k"].AsFloat());
        FertilizerSlot.TakeOut(1);
        FertilizerSlot.MarkDirty();
    }

    public void RefreshClimate()
    {
        var climate = Api?.World?.BlockAccessor.GetClimateAt(Pos, EnumGetClimateMode.NowValues);
        OutsideTemp = climate?.Temperature ?? 15f;
        OptimalTemp = 18f;
        if (!TryResolve(Api?.World, CropSeed, out var stage, out _))
            return;

        var props = stage.CropProps;
        float cold = props.ColdDamageBelow;
        float heat = props.HeatDamageAbove > 0.01f ? props.HeatDamageAbove : 32f;
        float lo = cold + 8f;
        float hi = heat - 4f;
        if (hi <= lo)
            hi = lo + 10f;
        OptimalTemp = GameMath.Clamp((lo + hi) * 0.5f, 8f, 28f);
    }

    public string CropTitle()
    {
        if (CropSeed == null)
            return Lang.Get("ecloche-idle");

        return CropSeed.GetName();
    }

    public int GrowthStage()
    {
        if (!TryResolve(Api?.World, CropSeed, out var stage, out _))
            return 0;

        int stages = stage.CropProps.GrowthStages;
        if (Progress <= 0f)
            return 1;
        return (int)Math.Clamp(Math.Ceiling(Progress * stages), 1, stages);
    }

    public int GrowthStages()
    {
        if (!TryResolve(Api?.World, CropSeed, out var stage, out _))
            return 0;

        return stage.CropProps.GrowthStages;
    }

    public bool OutputCanTakeHarvest()
    {
        if (!TryResolve(Api.World, CropSeed, out _, out var ripe))
            return false;

        var sample = SampleDrops(ripe);
        return sample.Count > 0 && CanFit(sample);
    }

    public int TryPutWater(ItemStack liquid, float desiredLitres)
    {
        if (liquid?.Collectible == null || desiredLitres <= 0f)
            return 0;

        if (liquid.Collectible.Code.Path != "waterportion")
            return 0;

        var props = BlockLiquidContainerBase.GetContainableProps(liquid);
        if (props == null || !props.Containable || props.ItemsPerLitre <= 0f)
            return 0;

        float room = _capacity - Litres;
        float available = liquid.StackSize / props.ItemsPerLitre;
        float movedLitres = Math.Min(desiredLitres, Math.Min(room, available));
        int movedItems = (int)(movedLitres * props.ItemsPerLitre);
        if (movedItems <= 0)
            return 0;

        Litres += movedItems / props.ItemsPerLitre;
        if (_water == null || !_water.Equals(Api.World, liquid, GlobalConstants.IgnoredStackAttributes))
            _water = liquid.Clone();

        SyncWaterStack();
        MarkDirty(true);
        return movedItems;
    }

    public void SetWater(ItemStack content)
    {
        if (content == null)
        {
            Litres = 0f;
            _water = null;
            MarkDirty();
            return;
        }

        var props = BlockLiquidContainerBase.GetContainableProps(content);
        if (props == null || content.Collectible?.Code?.Path != "waterportion")
            return;

        _water = content.Clone();
        Litres = Math.Min(_capacity, content.StackSize / props.ItemsPerLitre);
        SyncWaterStack();
        MarkDirty();
    }

    public ItemStack TryTakeWater(int quantityItems)
    {
        if (_water == null || quantityItems <= 0)
            return null;

        var props = BlockLiquidContainerBase.GetContainableProps(_water);
        float perLitre = props?.ItemsPerLitre > 0f ? props.ItemsPerLitre : ItemsPerLitre;
        int have = (int)Math.Floor(Litres * perLitre);
        int take = Math.Min(quantityItems, have);
        if (take <= 0)
            return null;

        Litres -= take / perLitre;
        var taken = _water.Clone();
        taken.StackSize = take;
        SyncWaterStack();
        MarkDirty();
        return taken;
    }

    public ItemStack WaterContent()
    {
        if (_water == null || Litres <= 0.001f)
            return null;

        var stack = _water.Clone();
        stack.StackSize = Math.Max(1, (int)Math.Floor(Litres * ItemsPerLitre));
        return stack;
    }

    public WaterTightContainableProps WaterProps()
    {
        return _water == null ? null : BlockLiquidContainerBase.GetContainableProps(_water);
    }

    public bool WaterFull => Litres >= _capacity - 0.001f;

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        var inventoryTree = new TreeAttribute();
        _inventory.ToTreeAttributes(inventoryTree);
        tree["_inventory"] = inventoryTree;
        tree.SetFloat("growth", Progress);
        tree.SetFloat("litres", Litres);
        tree.SetString("plant", PlantCode ?? "");
        tree.SetItemstack("water", _water);
        tree.SetFloat("n", NutrientN);
        tree.SetFloat("p", NutrientP);
        tree.SetFloat("k", NutrientK);
        tree.SetFloat("moist", Moisture);
        tree.SetFloat("inside", InsideTemp);
        tree.SetBool("lamp", LampOn);
        tree.SetBool("watering", Watering);
        tree.SetBool("wantWater", _wantWater);
        tree.SetFloat("fertProgress", _fertProgress);
        tree.SetFloat("powerRatio", LastPowerRatio);
        tree.SetDouble("lastHours", _lastTotalHours);
        tree.SetBool("auto", AutoReplant);
        tree.SetItemstack("planted", _planted);
        WritePending(tree);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        _suppress = true;
        base.FromTreeAttributes(tree, worldForResolving);
        var invTree = tree.GetTreeAttribute("_inventory") ?? tree.GetTreeAttribute("inventory");
        if (invTree != null)
            _inventory.FromTreeAttributes(invTree);
        if (Api != null)
            _inventory.AfterBlocksLoaded(worldForResolving);

        Progress = tree.GetFloat("growth");
        Litres = tree.GetFloat("litres");
        PlantCode = tree.GetString("plant");
        if (string.IsNullOrEmpty(PlantCode))
            PlantCode = null;

        _water = tree.GetItemstack("water");
        _water?.ResolveBlockOrItem(worldForResolving);
        if (Litres <= 0f && _water != null)
            Litres = _water.StackSize / ItemsPerLitre;

        NutrientN = tree.GetFloat("n", CompostNutrient);
        NutrientP = tree.GetFloat("p", CompostNutrient);
        NutrientK = tree.GetFloat("k", CompostNutrient);
        Moisture = tree.GetFloat("moist", 0.35f);
        InsideTemp = tree.GetFloat("inside", 15f);
        LampOn = tree.GetBool("lamp");
        Watering = tree.GetBool("watering");
        _wantWater = tree.GetBool("wantWater");
        _fertProgress = tree.GetFloat("fertProgress");
        LastPowerRatio = tree.GetFloat("powerRatio");
        _lastTotalHours = tree.GetDouble("lastHours");
        AutoReplant = tree.GetBool("auto");
        _planted = tree.GetItemstack("planted");
        _planted?.ResolveBlockOrItem(worldForResolving);
        ReadPending(tree, worldForResolving);
        _seedKey = SeedKey();
        _suppress = false;

        if (invDialog is GuiDialogECloche dialog)
            dialog.Refresh();
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        var farm = FarmlandMesh(tessThreadTesselator);
        if (farm != null)
            mesher.AddMeshData(farm);

        var mesh = ScaledPlant(tessThreadTesselator);
        if (mesh != null)
            mesher.AddMeshData(mesh);

        return false;
    }

    private MeshData FarmlandMesh(ITesselatorAPI tess)
    {
        bool moist = Moisture >= 0.55f;
        if (!moist)
            return null;

        if (_farmMoistMesh != null)
            return _farmMoistMesh.Clone();

        if (Api is not ICoreClientAPI capi)
            return null;

        var loc = new AssetLocation("electricalprogressiveindustry", "block/ecloche/soilbed-moist");
        var shape = capi.TesselatorManager.GetCachedShape(loc) ?? Shape.TryGet(capi, loc);
        if (shape == null)
            return null;

        tess.TesselateShape(Block, shape, out var mesh);
        if (mesh == null || mesh.VerticesCount <= 0)
            return null;

        SetPlantPass(mesh);
        _farmMoistMesh = mesh;
        return mesh.Clone();
    }

    public static bool IsFertilizer(ItemStack stack)
    {
        var attributes = stack?.Collectible?.Attributes;
        return attributes != null && attributes["fertilizerProps"].Exists;
    }

    public static bool IsSeed(IWorldAccessor world, ItemStack stack)
    {
        return TryResolve(world, stack, out _, out _);
    }

    public static bool TryResolve(IWorldAccessor world, ItemStack seed, out Vintagestory.API.Common.Block stage, out Vintagestory.API.Common.Block ripe)
    {
        stage = null;
        ripe = null;
        if (world == null || seed?.Collectible is not ItemPlantableSeed)
            return false;

        var type = seed.Collectible.Variant["type"];
        if (string.IsNullOrEmpty(type))
            return false;

        var domain = seed.Collectible.Code.Domain;
        stage = world.GetBlock(new AssetLocation(domain, "crop-" + type + "-1"));
        if (stage?.CropProps == null || stage.CropProps.GrowthStages < 1 || stage.Id == 0)
            return false;

        ripe = world.GetBlock(new AssetLocation(domain, "crop-" + type + "-" + stage.CropProps.GrowthStages));
        return ripe != null && ripe.Id != 0 && ripe.Drops != null && ripe.Drops.Length > 0;
    }

    private void ServerTick(float dt)
    {
        RefreshClimate();
        double now = Api.World.Calendar.TotalHours;
        if (_lastTotalHours <= 0)
            _lastTotalHours = now;

        float hours = (float)(now - _lastTotalHours);
        _lastTotalHours = now;
        if (hours < 0f)
            hours = 0f;
        if (hours > 72f)
            hours = 72f;

        TryPlant();
        if (hours > 0.0002f)
        {
            Simulate(hours, LastPowerRatio);
            if (Progress >= 1f)
                TryFinishHarvest();
            TouchSync();
        }
        else if (_pending != null || Progress >= 1f)
            TryFinishHarvest();
    }

    private void OnSlotModified(int slotId)
    {
        if (_suppress)
            return;

        MarkDirty();
        if (!_ready || Api?.Side != EnumAppSide.Server)
            return;

        if (slotId == 0)
            TryPlant();
    }

    private void TryPlant()
    {
        if (_planted != null || Api?.Side != EnumAppSide.Server)
            return;
        if (!IsSeed(Api.World, SeedSlot.Itemstack))
            return;

        _planted = SeedSlot.TakeOut(1);
        SeedSlot.MarkDirty();
        Progress = 0f;
        _pending = null;
        UpdatePlantCode();
        MarkDirty(true);
    }

    private string SeedKey()
    {
        if (SeedSlot?.Empty != false || SeedSlot.Itemstack?.Collectible?.Code == null)
            return "";

        return SeedSlot.Itemstack.Collectible.Code.ToString();
    }

    private void TryFinishHarvest()
    {
        if (!TryResolve(Api.World, CropSeed, out _, out var ripe))
        {
            _pending = null;
            Progress = 0f;
            UpdatePlantCode();
            TouchSync();
            return;
        }

        if (_pending == null)
            _pending = BuildHarvest(ripe);

        if (_pending.Length > 0 && !TryGive(_pending))
            return;

        _pending = null;
        Progress = 0f;
        if (AutoReplant && _planted != null)
        {
            UpdatePlantCode();
            TouchSync();
            return;
        }

        _planted = null;
        TryPlant();
        UpdatePlantCode();
        TouchSync();
    }

    private float YieldMul(Vintagestory.API.Common.Block ripe)
    {
        var props = ripe.CropProps;
        float n = props != null ? NutrientOf(props.RequiredNutrient) : NutrientN;
        float mul = 0.4f + n / 100f * 0.85f;
        if (Moisture < 0.4f)
            mul *= 0.7f;
        else if (Moisture >= 0.75f)
            mul *= 1.05f;
        return GameMath.Clamp(mul, 0.35f, 1.35f);
    }

    private ItemStack[] Roll(Vintagestory.API.Common.Block ripe)
    {
        var list = new List<ItemStack>();
        float yieldMul = YieldMul(ripe);
        var rand = Api.World.Rand;

        foreach (var drop in ripe.Drops)
        {
            if (drop?.ResolvedItemstack == null)
                continue;

            float qty;
            if (drop.Quantity != null)
                qty = drop.Quantity.nextFloat(yieldMul, rand);
            else
                qty = drop.ResolvedItemstack.StackSize * yieldMul;

            int n = GameMath.RoundRandom(rand, qty);
            if (n <= 0)
                continue;

            var stack = drop.ResolvedItemstack.Clone();
            stack.StackSize = n;
            list.Add(stack);
        }

        return list.ToArray();
    }

    private ItemStack[] BuildHarvest(Vintagestory.API.Common.Block ripe)
    {
        var list = new List<ItemStack>(Roll(ripe));
        if (AutoReplant)
            TakeOneSeedForReplant(list);

        return list.ToArray();
    }

    private void TakeOneSeedForReplant(List<ItemStack> drops)
    {
        if (_planted == null)
            return;

        for (int i = 0; i < drops.Count; i++)
        {
            var drop = drops[i];
            if (drop?.Collectible is not ItemPlantableSeed)
                continue;
            if (!drop.Equals(Api.World, _planted, GlobalConstants.IgnoredStackAttributes))
                continue;

            drop.StackSize--;
            if (drop.StackSize <= 0)
                drops.RemoveAt(i);
            return;
        }
    }

    private List<ItemStack> SampleDrops(Vintagestory.API.Common.Block ripe)
    {
        var list = new List<ItemStack>();
        foreach (var drop in ripe.Drops)
        {
            if (drop?.ResolvedItemstack == null)
                continue;

            var stack = drop.ResolvedItemstack.Clone();
            float avg = drop.Quantity?.avg ?? stack.StackSize;
            stack.StackSize = Math.Max(1, (int)Math.Ceiling(avg));
            list.Add(stack);
        }

        return list;
    }

    private bool CanFit(List<ItemStack> give)
    {
        var stacks = new ItemStack[OutputCount];
        var sizes = new int[OutputCount];
        for (int i = 0; i < OutputCount; i++)
        {
            var slot = _inventory[2 + i];
            if (slot.Empty)
                continue;

            stacks[i] = slot.Itemstack;
            sizes[i] = slot.Itemstack.StackSize;
        }

        foreach (var drop in give)
        {
            int left = drop.StackSize;
            for (int i = 0; i < OutputCount && left > 0; i++)
            {
                if (stacks[i] == null)
                {
                    int put = Math.Min(left, drop.Collectible.MaxStackSize);
                    stacks[i] = drop;
                    sizes[i] = put;
                    left -= put;
                }
                else if (ItemSlotClocheOutput.SameIgnoreSpoil(Api.World, stacks[i], drop))
                {
                    int room = stacks[i].Collectible.MaxStackSize - sizes[i];
                    int put = Math.Min(left, Math.Max(0, room));
                    sizes[i] += put;
                    left -= put;
                }
            }

            if (left > 0)
                return false;
        }

        return true;
    }

    private bool TryGive(ItemStack[] give)
    {
        if (!CanFit(new List<ItemStack>(give)))
            return false;

        foreach (var drop in give)
        {
            for (int i = 2; i <= 5 && drop.StackSize > 0; i++)
                drop.StackSize -= PutIgnoreSpoil(_inventory[i], drop, drop.StackSize);
        }

        return true;
    }

    private static int PutIgnoreSpoil(ItemSlot slot, ItemStack drop, int quantity)
    {
        if (quantity <= 0 || drop?.Collectible == null)
            return 0;

        if (slot.Empty)
        {
            int put = Math.Min(quantity, drop.Collectible.MaxStackSize);
            slot.Itemstack = drop.Clone();
            slot.Itemstack.StackSize = put;
            slot.MarkDirty();
            return put;
        }

        if (!ItemSlotClocheOutput.SameIgnoreSpoil(slot.Inventory?.Api?.World, slot.Itemstack, drop))
            return 0;

        int room = slot.Itemstack.Collectible.MaxStackSize - slot.StackSize;
        int take = Math.Min(room, quantity);
        if (take <= 0)
            return 0;

        ItemSlotClocheOutput.MergePerish(slot.Itemstack, drop, take);
        slot.Itemstack.StackSize += take;
        slot.MarkDirty();
        return take;
    }

    private void UpdatePlantCode()
    {
        string next = null;
        if (TryResolve(Api.World, CropSeed, out var stage, out _))
        {
            int stages = stage.CropProps.GrowthStages;
            int shown = Progress <= 0f ? 1 : (int)Math.Clamp(Math.Ceiling(Progress * stages), 1, stages);
            var type = CropSeed.Collectible.Variant["type"];
            next = CropSeed.Collectible.Code.Domain + ":crop-" + type + "-" + shown;
        }

        if (next == PlantCode)
            return;

        PlantCode = next;
        _plantChanged = true;
    }

    private void TouchSync()
    {
        long now = Api.World.ElapsedMilliseconds;
        bool mesh = _plantChanged;
        _plantChanged = false;
        if (!mesh && now < _nextSync)
            return;

        MarkDirty(mesh);
        _nextSync = now + 250;
    }

    private void SyncWaterStack()
    {
        int items = (int)Math.Floor(Litres * ItemsPerLitre);
        if (items <= 0)
        {
            _water = null;
            if (Litres < 0f)
                Litres = 0f;
            return;
        }

        if (_water == null)
        {
            var item = Api.World.GetItem(new AssetLocation("game", "waterportion"));
            if (item == null)
                return;

            _water = new ItemStack(item, items);
            return;
        }

        _water.StackSize = items;
    }

    private void SpillPending()
    {
        if (Api?.Side != EnumAppSide.Server || _pending == null)
            return;

        foreach (var stack in _pending)
        {
            if (stack != null && stack.StackSize > 0)
                Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }

        _pending = null;
    }

    private void WritePending(ITreeAttribute tree)
    {
        if (_pending == null)
        {
            tree.RemoveAttribute("pending");
            return;
        }

        var sub = new TreeAttribute();
        sub.SetInt("count", _pending.Length);
        for (int i = 0; i < _pending.Length; i++)
            sub.SetItemstack("s" + i, _pending[i]);

        tree["pending"] = sub;
    }

    private void ReadPending(ITreeAttribute tree, IWorldAccessor world)
    {
        var sub = tree.GetTreeAttribute("pending");
        if (sub == null)
        {
            _pending = null;
            return;
        }

        int count = sub.GetInt("count");
        _pending = new ItemStack[count];
        for (int i = 0; i < count; i++)
        {
            var stack = sub.GetItemstack("s" + i);
            stack?.ResolveBlockOrItem(world);
            _pending[i] = stack;
        }
    }

    private MeshData ScaledPlant(ITesselatorAPI tess)
    {
        if (string.IsNullOrEmpty(PlantCode) || Api is not ICoreClientAPI capi)
            return null;

        var crop = Api.World.GetBlock(new AssetLocation(PlantCode));
        if (crop == null || crop.Id == 0)
            return null;

        lock (PlantLock)
        {
            if (PlantCache.TryGetValue(crop.Id, out var cached))
                return cached.Clone();

            var mesh = TesselateCrop(capi, tess, crop);
            if (mesh == null || mesh.VerticesCount <= 0)
                return null;

            StripWind(mesh);
            SetPlantPass(mesh);
            FitPlant(mesh);
            PlantCache[crop.Id] = mesh;
            return mesh.Clone();
        }
    }

    private static MeshData TesselateCrop(ICoreClientAPI capi, ITesselatorAPI tess, Vintagestory.API.Common.Block crop)
    {
        tess.TesselateBlock(crop, out var blockMesh);
        if (blockMesh != null && blockMesh.VerticesCount > 0)
            return blockMesh;

        var loc = crop.Shape?.Base;
        if (loc == null)
            return blockMesh;

        var resolved = loc.Clone();
        if (resolved.Path.Contains('*') && crop.Variant.TryGetValue("stage", out var stage))
            resolved.Path = resolved.Path.Replace("*", stage);

        var shape = capi.TesselatorManager.GetCachedShape(resolved);
        if (shape == null)
            return blockMesh;

        tess.TesselateShape(crop, shape, out var shaped);
        return shaped ?? blockMesh;
    }

    private static void StripWind(MeshData mesh)
    {
        if (mesh.Flags == null)
            return;

        int mask = VertexFlags.WindModeBitsMask | VertexFlags.WindDataBitsMask;
        for (int i = 0; i < mesh.Flags.Length; i++)
            mesh.Flags[i] &= ~mask;
    }

    private static void SetPlantPass(MeshData mesh)
    {
        var ipf = mesh.IndicesPerFace > 0 ? mesh.IndicesPerFace : 3;
        var faces = Math.Max(1, mesh.IndicesCount / ipf);
        if (mesh.RenderPassesAndExtraBits == null || mesh.RenderPassesAndExtraBits.Length != faces)
            mesh.RenderPassesAndExtraBits = new short[faces];
        for (int i = 0; i < mesh.RenderPassesAndExtraBits.Length; i++)
            mesh.RenderPassesAndExtraBits[i] = (short)EnumChunkRenderPass.OpaqueNoCull;
    }

    private static void FitPlant(MeshData mesh)
    {
        var xyz = mesh.xyz;
        if (xyz == null || xyz.Length < 3)
            return;

        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i + 2 < xyz.Length; i += 3)
        {
            minX = Math.Min(minX, xyz[i]);
            maxX = Math.Max(maxX, xyz[i]);
            minY = Math.Min(minY, xyz[i + 1]);
            maxY = Math.Max(maxY, xyz[i + 1]);
            minZ = Math.Min(minZ, xyz[i + 2]);
            maxZ = Math.Max(maxZ, xyz[i + 2]);
        }

        if (maxY > 4f || maxX > 4f || maxZ > 4f)
        {
            mesh.Scale(new Vec3f(0f, 0f, 0f), 1f / 16f, 1f / 16f, 1f / 16f);
            minX /= 16f; maxX /= 16f;
            minY /= 16f; maxY /= 16f;
            minZ /= 16f; maxZ /= 16f;
        }

        float sx = 0f, sz = 0f;
        int n = 0;
        for (int i = 0; i + 2 < xyz.Length; i += 3)
        {
            float x = xyz[i], y = xyz[i + 1], z = xyz[i + 2];
            if (x * x + y * y + z * z < 1e-8f)
                continue;
            sx += x;
            sz += z;
            n++;
        }

        float cx = n > 0 ? sx / n : (minX + maxX) * 0.5f;
        float cz = n > 0 ? sz / n : (minZ + maxZ) * 0.5f;
        float height = Math.Max(0.05f, maxY - minY);
        float width = Math.Max(Math.Max(maxX - minX, maxZ - minZ), 0.05f);
        float scale = Math.Min(PlantMaxWidth / width, PlantMaxHeight / height);
        scale = Math.Min(scale, 1f);

        mesh.Translate(0.5f - cx, -minY, 0.5f - cz);
        mesh.Scale(new Vec3f(0.5f, 0f, 0.5f), scale, scale, scale);
        mesh.Translate(0f, PlantSoilY, 0f);
    }

    private float GrowthNeedHours(BlockCropProperties props)
    {
        var cal = Api.World.Calendar;
        float daysPerMonth = cal != null && cal.DaysPerMonth > 0 ? cal.DaysPerMonth : 9f;
        float days;
        if (props.TotalGrowthDays > 0f)
            days = props.TotalGrowthDays;
        else if (props.TotalGrowthMonths > 0f)
            days = props.TotalGrowthMonths * daysPerMonth;
        else
            days = daysPerMonth;

        return Math.Max(4f, days * 24f);
    }
}
