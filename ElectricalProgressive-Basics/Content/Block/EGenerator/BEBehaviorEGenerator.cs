using ElectricalProgressive.Content.Block.EAccumulator;
using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Linq;
using System.Net;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ElectricalProgressive.Content.Block.EGenerator;

public class BEBehaviorEGenerator : BEBehaviorMPBase, IElectricProducer
{
    // --- Константы и Ключи ---

    private const string PowerOrderKey = "electricalprogressive:powerOrder";
    private const string PowerGiveKey = "electricalprogressive:powerGive";
    private const string AvgPowerOrderKey = "electricalprogressive:avgPowerOrder";

    /// <summary>Значения по умолчанию: I_max, speed_max, resistance_factor, resistance_load, base_resistance, kpd_max</summary>
    private static readonly float[] DefaultParams = [100.0F, 0.5F, 0.1F, 0.25F, 0.05F, 1F];

    // --- Параметры генератора (из ассетов) ---

    private float I_max;           // Максимальный ток
    private float speed_max;       // Максимальная скорость вращения
    private float resistance_factor;
    private float resistance_load;
    private float base_resistance;
    private float kpd_max;         // КПД

    // --- Состояние (State) ---

    /// <summary>Запрашиваемая мощность/ток от сети</summary>
    private float PowerOrder;

    /// <summary>Фактическая отдаваемая мощность/ток</summary>
    public float PowerGive;

    /// <summary>Фильтр для сглаживания нагрузки для расчета сопротивления (EMA)</summary>
    public ExponentialMovingAverage EmaFilterPowerOrder = new(0.05f);

    private float _avgPowerOrder; // Сглаженное значение

    /// <summary>Фильтр для сглаживания скорости(EMA)</summary>
    public ExponentialMovingAverage EmaFilterSpeed = new(0.05f);



    // --- Рендеринг и Визуал ---

    protected CompositeShape? CompositeShape;       // Высокодетализированная модель
    protected CompositeShape? CompositeShapeLOD2;   // Упрощенная модель (дальний план)


    /// <summary>Локальное состояние "сгорел" генератор</summary>
    private bool hasBurnout = false;

    /// <summary>Локальное состояние "готовится к сгоранию"</summary>
    private bool prepareBurnout = false;


    // --- Внутренние переменные и Служебное ---

    private ICoreClientAPI? capi;
    private bool playerSoFar; // Дальше ли игрок, чем LOD2 bias

    /// <summary>Кэш направления выхода для сети (вал)</summary>
    private BlockFacing? _outFacingForNetworkDiscovery;

    /// <summary>Кэш вектора оси вращения [x, y, z]</summary>
    private int[]? _axisSign;

    // Массив направлений для вычисления векторов осей. Индексы соответствуют BlockFacing.Index
    private static readonly int[][] AxisVectorsMap =
    [
        [+0, +0, -1], // 0: South (обычно) или Forward
        [-1, +0, +0], // 1: West? Зависит от реализации API
        [+0, +0, -1], // 2: North
        [-1, +0, +0], // 3: East
        [+0, -1, +0], // 4: Down
        [+0, +1, +0]  // 5: Up
    ];


    public BEBehaviorEGenerator(BlockEntity blockEntity) : base(blockEntity)
    {
        GetParams(); // Загружаем параметры при создании инстанса
    }

    /// <summary>Получает ссылку на сущность генератора, чтобы не приводить тип каждый раз</summary>
    private BlockEntityEGenerator? Generator => Blockentity as BlockEntityEGenerator;


    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);

        if (api.Side == EnumAppSide.Client)
        {
            capi = api as ICoreClientAPI;
            // Слушаем тик реже для оптимизации рендера
            api.Event.RegisterGameTickListener(OnTick, 2000);
        }
    }

    /// <summary>Отслеживает расстояние до игрока и переключает детализацию модели</summary>
    private void OnTick(float dt)
    {
        // Если генератор выгружен или не загружена система ElectricalProgressive - выходим
        if (Generator?.ElectricalProgressive?.IsLoaded != true)
            return;

        var lod2 = MyMiniLib.CheckLOD2Distance(capi, Pos);

        if (lod2 != playerSoFar)
        {
            playerSoFar = lod2;
            updateShape(capi.World); // Обновляем модель при смене уровня детализации

        }
    }




    /// <summary>Вызывается при выгрузке блока из мира. Очищаем кэш шейпов.</summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        CompositeShape = null;
        CompositeShapeLOD2 = null;
    }


    /// <summary>Направление выхода для обнаружения сети (вал)</summary>
    public override BlockFacing? OutFacingForNetworkDiscovery
    {
        get
        {
            if (_outFacingForNetworkDiscovery == null && Generator?.Facing != Facing.None)
            {
                // Берем первое направление из массива направлений вращения
                _outFacingForNetworkDiscovery = FacingHelper.Directions(Generator.Facing).FirstOrDefault();
            }
            return _outFacingForNetworkDiscovery;
        }
    }

    /// <summary>Вектор оси генератора</summary>
    public override int[] AxisSign
    {
        get
        {
            if (_axisSign == null && OutFacingForNetworkDiscovery != null)
            {
                var index = OutFacingForNetworkDiscovery.Index;
                // Возвращаем вектор из мапы или дефолтный [0,0,-1] если индекс некорректен
                _axisSign = (index >= 0 && index < AxisVectorsMap.Length)
                    ? AxisVectorsMap[index]
                    : new int[] { 0, 0, -1 };
            }
            return _axisSign ?? new int[] { 0, 0, -1 };
        }
    }

    /// <summary>Извлекаем параметры из ассетов блока</summary>  
    private void GetParams()
    {
        // Получаем массив параметров или используем дефолтный
        var paramsArray = MyMiniLib.GetAttributeArrayFloat(Block, "params", DefaultParams);

        I_max = paramsArray[0];
        speed_max = paramsArray[1];
        resistance_factor = paramsArray[2];
        resistance_load = paramsArray[3];
        base_resistance = paramsArray[4];
        kpd_max = paramsArray[5];

        _avgPowerOrder = 0;
    }


    /// <summary>Рассчитывает отдаваемую мощность в зависимости от скорости вращения</summary>
    public float Produce_give()
    {
        // Скорость с учетом передаточного отношения
        var speed = (float)EmaFilterSpeed.Update(GetSpeed());

        // Формула: Линейный рост до max speed, затем горизонтальная линия
        var power = (Math.Abs(speed) <= speed_max)
            ? Math.Abs(speed) / speed_max * I_max
            : I_max;

        PowerGive = power;
        return power;
    }


    /// <summary>Устанавливает запрашиваемую мощность и обновляет сглаженный фильтр</summary>
    public void Produce_order(float amount)
    {
        PowerOrder = amount;

        // Сглаживаем фактическую передачу (минимум из того, что просят и того что есть)
        _avgPowerOrder = (float)EmaFilterPowerOrder.Update(Math.Min(PowerGive, PowerOrder));
    }


    public float getPowerGive() => PowerGive;
    public float getPowerOrder() => PowerOrder;

    private float GetSpeed()
    {
        return Math.Abs(network?.Speed * GearedRatio ?? 0.0F);
    }


    /// <summary>Механическая сеть берет отсюда сопротивление этого генератора</summary>
    public override float GetResistance()
    {
        // Проверяем, сгорел ли блок (через вариант)
        bool isBurnedBlock = Block.Variant["type"] == "burned";

        if (isBurnedBlock) return 9999.0F;

        float spd;

        if (Api.Side == EnumAppSide.Server)
            spd = (float)EmaFilterSpeed.Update(GetSpeed());
        else
        {
            spd = GetSpeed();
        }
            

        // Расчет сопротивления нагрузки
        float loadRes = resistance_load * (Math.Min(_avgPowerOrder, I_max) / I_max);

        // Нелинейный фактор сопротивления при высоких скоростях
        float speedFactor;
        if (spd > speed_max)
        {
            // При превышении скорости сопротивление растет по квадрату
            speedFactor = resistance_factor * (float)Math.Pow((spd / speed_max), 2f);
        }
        else
        {
            // Линейный рост в нормальном режиме
            speedFactor = resistance_factor * spd / speed_max;
        }

        float res = base_resistance + loadRes + speedFactor;

        return res / kpd_max; // Делим на КПД (учитываем потери)
    }


    /// <summary>Основной цикл обновления логики</summary>
    public void Update()
    {
        if (Generator == null || Generator.ElectricalProgressive?.AllEparams is null)
            return;

        // Если нет сети, пытаемся подключиться к соседям через вал
        if (network == null && OutFacingForNetworkDiscovery != null)
        {
            CreateJoinAndDiscoverNetwork(OutFacingForNetworkDiscovery);
        }

        bool anyBurnout = false;
        bool anyPrepareBurnout = false;

        // Проходим по параметрам всех связанных элементов для проверки пробоя
        foreach (var eParam in Generator.ElectricalProgressive.AllEparams)
        {
            if (!hasBurnout && eParam.burnout) hasBurnout = true;
            if (!prepareBurnout && eParam.ticksBeforeBurnout > 0) prepareBurnout = true;

            if (eParam.burnout) anyBurnout = true;
            if (eParam.ticksBeforeBurnout > 0) anyPrepareBurnout = true;
        }

        // Если состояние глобальной переменной отличается от того, что мы насчитали - обновляем
        bool stateChanged = false;

        if (!anyBurnout && hasBurnout)
        {
            hasBurnout = false;
            stateChanged = true;
        }
        else if (anyBurnout && !hasBurnout)
        {
            hasBurnout = true;
            stateChanged = true;
        }

        if (!anyPrepareBurnout && prepareBurnout)
        {
            prepareBurnout = false;
            stateChanged = true;
        }
        else if (anyPrepareBurnout && !prepareBurnout)
        {
            prepareBurnout = true;
            stateChanged = true;
        }

        // Если состояние изменилось, помечаем блок как грязный для сохранения
        if (stateChanged)
        {
            Generator.MarkDirty(true);
        }

        // Обработка сгорания блока (визуальное изменение физического блока)
        if (hasBurnout && Block.Variant["type"] != "burned")
        {
            var burnedBlock = Api.World.GetBlock(Block.CodeWithVariant("type", "burned"));
            if (burnedBlock != null)
            {
                Api.World.BlockAccessor.ExchangeBlock(burnedBlock.BlockId, Pos);
                // Блок физически изменился, нужно сохранить состояние
                Generator.MarkDirty(true);
            }
        }
    }


    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat(PowerOrderKey, PowerOrder);
        tree.SetFloat(PowerGiveKey, PowerGive);
        tree.SetFloat(AvgPowerOrderKey, _avgPowerOrder);

        if (_outFacingForNetworkDiscovery != null)
        {
            tree.SetInt("savedOutFacing", _outFacingForNetworkDiscovery.Index);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        PowerOrder = tree.GetFloat(PowerOrderKey);
        PowerGive = tree.GetFloat(PowerGiveKey);
        _avgPowerOrder= tree.GetFloat(AvgPowerOrderKey);

        var savedIndex = tree.GetInt("savedOutFacing", -1);
        if (savedIndex >= 0 && savedIndex < BlockFacing.ALLFACES.Length)
        {
            _outFacingForNetworkDiscovery = BlockFacing.ALLFACES[savedIndex];
        }
    }


    /// <summary>Информация о блоке в подсказке</summary>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        if (Generator == null || Block.Variant["type"] == "burned") return;

        var speed = GetSpeed();

        // Прогрессбар заполнения
        var percent = _avgPowerOrder / I_max * 100;
        stringBuilder.AppendLine(StringHelper.Progressbar(percent));

        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Production") + ": " +
            ((int)_avgPowerOrder).ToString() + "/" + (int)I_max + " " + Lang.Get("electricalprogressivebasics:W"));

        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Prod_potential") + ": " +
            ((int)PowerGive).ToString() + " " + Lang.Get("electricalprogressivebasics:W"));

        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Speed") + ": " +
            speed.ToString("F3") + " " + Lang.Get("electricalprogressivebasics:rps"));

        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:resistance_moment", (int)(GetResistance() * 100)));
    }


    /// <summary>Подготовка модели ротора для отрисовки</summary>
    protected override CompositeShape? GetShape()
    {
        if (capi == null || Generator == null || Generator.Facing == Facing.None || Block.Variant["type"] == "burned")
            return null;

        // Инициализируем шейпы один раз
        if (CompositeShape == null)
        {
            var tier = Generator.Block.Variant["tier"];

            CompositeShape = Block.Shape.Clone();
            CompositeShapeLOD2 = Block.Shape.Clone();

            CompositeShape.Base = new AssetLocation($"electricalprogressivebasics:shapes/block/egenerator/egenerator-{tier}-rotor.json");
            CompositeShapeLOD2.Base = new AssetLocation($"electricalprogressivebasics:shapes/block/egenerator/egenerator-{tier}-rotor-lod2.json");
        }

        // Выбираем нужную модель (High/Low Poly)
        var shape = playerSoFar ? CompositeShapeLOD2.Clone() : CompositeShape.Clone();


        // Вращение модели в зависимости от направления выхода
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

    protected override void updateShape(IWorldAccessor worldForResolve)
    {
        Shape = GetShape();
    }


    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        // Кэшируем освещение для оптимизации рендера
        this.lightRbs = this.Api.World.BlockAccessor.GetLightRGBs(this.Blockentity.Pos);
        return false;
    }

    public override void WasPlaced(BlockFacing connectedOnFacing)
    {
        // Логика при установке блока (если нужна)
    }
}
