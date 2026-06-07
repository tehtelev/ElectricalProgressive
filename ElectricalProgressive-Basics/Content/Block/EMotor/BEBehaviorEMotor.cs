using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ElectricalProgressive.Content.Block.EMotor;

public class BEBehaviorEMotor : BEBehaviorMPBase, IElectricConsumer
{
    // --- Константы и Ключи ---
    private const string PowerRequestKey = "electricalprogressive:powerRequest";
    private const string PowerReceiveKey = "electricalprogressive:powerReceive";

    // --- Параметры двигателя (из ассетов) ---
    private float I_min;
    private float I_max;
    private float torque_max;
    private float kpd_max;
    private float speed_max;
    private float resistanceFactor;   // Исправлено: не static, параметры индивидуальны для каждого блока
    private float base_resistance;

    /// <summary>Заглушка параметров: I_min, I_max, torque_max, kpd_max, speed_max, resistance_factor, base_resistance</summary>
    private static readonly float[] DefaultParams = [10.0F, 100.0F, 0.5F, 0.75F, 0.5F, 0.1F, 0.05F];

    // --- Состояние (State) ---
    private float powerRequest;
    public float powerReceive;
    private float torque;
    private float I_value;
    private bool hasBurnout = false;
    private bool prepareBurnout = false;

    /// <summary>Коэффициент текущего потребления</summary>
    public float AvgConsumeCoeff { get; set; }

    // --- Рендеринг и Визуал ---
    protected CompositeShape? CompositeShape;       // Высокодетализированная модель
    protected CompositeShape? CompositeShapeLOD2;   // Упрощённая модель (дальний план)
    private bool playerSoFar; // Дальше ли игрок, чем LOD2 bias

    // --- Внутренние переменные и Служебное ---
    private ICoreClientAPI? capi;
    private BlockFacing? _outFacingForNetworkDiscovery;
    private int[]? _axisSign;

    /// <summary>Массив направлений для вычисления векторов осей</summary>
    private static readonly int[][] AxisVectorsMap =
    [
        [+0, +0, -1], // 0: South/Forward
        [-1, +0, +0], // 1: West?
        [+0, +0, -1], // 2: North
        [-1, +0, +0], // 3: East
        [+0, -1, +0], // 4: Down
        [+0, +1, +0]  // 5: Up
    ];

    /// <summary>Индикатор "сгорел" двигатель</summary>
    private bool IsBurned => Block.Variant["type"] == "burned";


    public BEBehaviorEMotor(BlockEntity blockentity) : base(blockentity)
    {
        GetParams();
        powerReceive = 0;
        powerRequest = I_max; // Запрашиваем максимум при инициализации
    }

    /// <summary>Получает ссылку на сущность двигателя, чтобы избежать многократного приведения типов</summary>
    private BlockEntityEMotor? Motor => Blockentity as BlockEntityEMotor;


    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);

        if (api.Side == EnumAppSide.Client)
        {
            capi = api as ICoreClientAPI;
            // Слушаем тик реже для оптимизации проверки дистанции и рендера
            api.Event.RegisterGameTickListener(OnTick, 2000);
        }
    }

    /// <summary>Отслеживает расстояние до игрока и переключает детализацию модели</summary>
    private void OnTick(float dt)
    {
        // Если генератор выгружен или не загружена система ElectricalProgressive - выходим
        if (Motor?.ElectricalProgressive?.IsLoaded != true)
            return;

        var lod2 = MyMiniLib.CheckLOD2Distance(capi, Pos);

        if (lod2 != playerSoFar)
        {
            playerSoFar = lod2;
            updateShape(capi.World); // Обновляем модель при смене уровня детализации

        }
    }


    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        CompositeShape = null;
        CompositeShapeLOD2 = null;
    }


    public override BlockFacing? OutFacingForNetworkDiscovery
    {
        get
        {
            if (_outFacingForNetworkDiscovery == null && Motor?.Facing != Facing.None)
                _outFacingForNetworkDiscovery = FacingHelper.Directions(Motor.Facing).FirstOrDefault();
            return _outFacingForNetworkDiscovery;
        }
    }

    public override int[] AxisSign
    {
        get
        {
            if (_axisSign == null && OutFacingForNetworkDiscovery != null)
            {
                var index = OutFacingForNetworkDiscovery.Index;
                _axisSign = (index >= 0 && index < AxisVectorsMap.Length) ? AxisVectorsMap[index] : new int[] { 0, 0, -1 };
            }
            return _axisSign ?? new int[] { 0, 0, -1 };
        }
    }

    public new BlockPos Pos => Position;


    private void GetParams()
    {
        var paramsArray = MyMiniLib.GetAttributeArrayFloat(Block, "params", DefaultParams);
        I_min = paramsArray[0];
        I_max = paramsArray[1];
        torque_max = paramsArray[2];
        kpd_max = paramsArray[3];
        speed_max = paramsArray[4];
        resistanceFactor = paramsArray[5];
        base_resistance = paramsArray[6];
    }


    public float Consume_request() => powerRequest;

    public void Consume_receive(float amount) => powerReceive = amount;


    /// <summary>Основной цикл обновления логики</summary>
    public void Update()
    {
        if (Motor == null || Motor.ElectricalProgressive?.AllEparams is null) return;

        // Инициализация сети валом, если её нет
        if (network == null && OutFacingForNetworkDiscovery != null)
            CreateJoinAndDiscoverNetwork(OutFacingForNetworkDiscovery);

        bool anyBurnout = false;
        bool anyPrepareBurnout = false;

        foreach (var eParam in Motor.ElectricalProgressive.AllEparams)
        {
            if (!hasBurnout && eParam.burnout) hasBurnout = true;
            if (!prepareBurnout && eParam.ticksBeforeBurnout > 0) prepareBurnout = true;

            if (eParam.burnout) anyBurnout = true;
            if (eParam.ticksBeforeBurnout > 0) anyPrepareBurnout = true;
        }

        // Вычисляем, изменилось ли состояние, чтобы вызвать MarkDirty ровно один раз
        bool stateChanged = false;
        if (hasBurnout != anyBurnout) { hasBurnout = anyBurnout; stateChanged = true; }
        if (prepareBurnout != anyPrepareBurnout) { prepareBurnout = anyPrepareBurnout; stateChanged = true; }

        if (stateChanged) Motor.MarkDirty(true);

        // Физическое изменение блока при сгорании
        if (hasBurnout && Block.Variant["type"] != "burned")
        {
            var burnedBlock = Api.World.GetBlock(Block.CodeWithVariant("type", "burned"));
            if (burnedBlock != null)
            {
                Api.World.BlockAccessor.ExchangeBlock(burnedBlock.BlockId, Pos);
                Motor.MarkDirty(true); // Блок физически изменился, сохраняем состояние
            }
        }
    }


    public float getPowerReceive() => powerReceive;
    public float getPowerRequest() => powerRequest;

    public override float GetResistance()
    {
        if (IsBurned)
            return 9999.0F;

        var spd = Math.Abs(Network?.Speed * GearedRatio ?? 0.0f);
        var ratio = spd / speed_max;

        // Нелинейный рост сопротивления при высоких скоростях (квадратичная зависимость)
        float resistanceFactorMultiplier = (spd > speed_max) ? (float)Math.Pow(ratio, 2f) : ratio;

        return base_resistance + (resistanceFactor * resistanceFactorMultiplier);
    }


    public override float GetTorque(long tick, float speed, out float resistance)
    {
        torque = 0f;
        resistance = GetResistance();
        I_value = 0f;

        var availablePower = powerReceive; // Доступно тока/энергии

        if (availablePower <= I_min)
            return torque;

        I_value = Math.Min(availablePower, I_max); // Ограничиваем максимальным током

        if (I_value < I_min)
            torque = 0.0F;
        else
            torque = (I_value / I_max) * torque_max * kpd_max;

        powerRequest = I_max; // Запрашиваем максимум для стабильной работы двигателя

        // Учитываем направление передачи крутящего момента относительно оси сети
        float directionMultiplier = propagationDir == OutFacingForNetworkDiscovery ? 1f : -1f;

        return directionMultiplier * torque;
    }


    protected override CompositeShape? GetShape()
    {
        if (capi == null || Motor == null || Motor.Facing == Facing.None || IsBurned)
            return null;

        // Инициализация моделей один раз при первом запросе
        if (CompositeShape == null)
        {
            var tier = Motor.Block.Variant["tier"];

            CompositeShape = Block.Shape.Clone();
            CompositeShapeLOD2 = Block.Shape.Clone();

            CompositeShape.Base = new AssetLocation($"electricalprogressivebasics:shapes/block/emotor/emotor-{tier}-rotor.json");
            CompositeShapeLOD2.Base = new AssetLocation($"electricalprogressivebasics:shapes/block/emotor/emotor-{tier}-rotor-lod2.json");
        }

        // Выбор модели в зависимости от дистанции до игрока
        var shape = playerSoFar ? CompositeShapeLOD2.Clone() : CompositeShape.Clone();

        // Вращение модели по оси выхода сети (используем .Index для совместимости с switch)
        switch (OutFacingForNetworkDiscovery.Index)
        {
            case BlockFacing.indexNORTH: shape.rotateY = 0; break;
            case BlockFacing.indexEAST: shape.rotateY = 270; break;
            case BlockFacing.indexSOUTH: shape.rotateY = 180; break;
            case BlockFacing.indexWEST: shape.rotateY = 90; break;
            case BlockFacing.indexUP: shape.rotateX = 90; break;
            case BlockFacing.indexDOWN: shape.rotateX = 270; break;
        }

        return shape;
    }

    protected override void updateShape(IWorldAccessor worldForResolve) => Shape = GetShape();


    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        this.lightRbs = this.Api.World.BlockAccessor.GetLightRGBs(this.Blockentity.Pos);
        return false;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat(PowerRequestKey, powerRequest);
        tree.SetFloat(PowerReceiveKey, powerReceive);

        if (_outFacingForNetworkDiscovery != null)
            tree.SetInt("savedOutFacing", _outFacingForNetworkDiscovery.Index);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        powerRequest = tree.GetFloat(PowerRequestKey);
        powerReceive = tree.GetFloat(PowerReceiveKey);

        var savedIndex = tree.GetInt("savedOutFacing", -1);
        if (savedIndex >= 0 && savedIndex < BlockFacing.ALLFACES.Length)
            _outFacingForNetworkDiscovery = BlockFacing.ALLFACES[savedIndex];
    }


    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        if (Motor == null || IsBurned) return;

        stringBuilder.AppendLine(StringHelper.Progressbar(powerReceive / I_max * 100));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Consumption") + ": " + ((int)powerReceive).ToString() + "/" + (int)I_max + " " + Lang.Get("electricalprogressivebasics:W"));

        var speed = network?.Speed * GearedRatio ?? 0.0F;
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Speed") + ": " + speed.ToString("F3") + " " + Lang.Get("electricalprogressivebasics:rps"));

        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:resistance_moment", (int)(GetResistance() * 100)));

        // Вычисляем момент для отображения в подсказке. 
        // Вызов GetTorque здесь безопасен, так как метод вызывается редко (при наведении мыши)
        float dummyRes = 0;
        var currentTorque = GetTorque(0, speed, out dummyRes);
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:torque_moment", (int)(currentTorque * 100)));
    }

    public override void WasPlaced(BlockFacing connectedOnFacing) { /* Логика при установке блока */ }
}
