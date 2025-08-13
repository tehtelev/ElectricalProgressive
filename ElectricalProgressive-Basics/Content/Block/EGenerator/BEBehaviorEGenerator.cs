using ElectricalProgressive.Content.Block.EMotor;
using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Linq;
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
    private float PowerOrder;           // Просят столько энергии (сохраняется)
    public const string PowerOrderKey = "electricalprogressive:powerOrder";

    private float PowerGive;           // Отдаем столько энергии (сохраняется)
    public const string PowerGiveKey = "electricalprogressive:powerGive";



    /// <summary>
    /// Максимальный ток
    /// </summary>
    private float I_max;
    /// <summary>
    /// Максимальная скорость вращения
    /// </summary>
    private float speed_max;
    /// <summary>
    /// Множитель сопротивления
    /// </summary>
    private float resistance_factor;
    /// <summary>
    /// Сопротивление нагрузки генератора
    /// </summary>
    private float resistance_load;
    /// <summary>
    /// Базовое сопротивление
    /// </summary>
    private float base_resistance;
    /// <summary>
    /// КПД
    /// </summary>
    private float kpd_max;

    /// <summary>
    /// Заглушка. I_max, speed_max , resistance_factor, resistance_load, base_resistance, kpd_max
    /// </summary>
    private float[] def_Params => new[] { 100.0F, 0.5F, 0.1F, 0.25F, 0.05F, 1F };
    /// <summary>
    /// Сюда берем параметры из ассетов
    /// </summary>
    private float[] Params = { 0, 0, 0, 0, 0, 0 };

    // задает коэффициент сглаживания фильтра
    public ExponentialMovingAverage EmaFilter;

    private float AvgPowerOrder;

    private bool IsBurned => Block.Variant["type"] == "burned";


    protected CompositeShape? CompositeShape;  //не трогать уровни доступа

    private static readonly int[][] _axisSigns =
    {
        new[] { +0, +0, -1 }, // index 0
        new[] { -1, +0, +0 }, // index 1
        new[] { +0, +0, -1 }, // index 2
        new[] { -1, +0, +0 }, // index 3
        new[] { +0, -1, +0 }, // index 4
        new[] { +0, +1, +0 }  // index 5
    };



    /// <summary>
    /// Вызывается при выгрузке блока из мира
    /// </summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        CompositeShape = null;
    }



    public new BlockPos Pos => Position;

    private BlockFacing _outFacingForNetworkDiscovery = null!;
    private int[] _axisSign=null!;


    /// <summary>
    /// Возвращает направление выхода для обнаружения сети валом
    /// </summary>
    public override BlockFacing OutFacingForNetworkDiscovery
    {
        get
        {
            if (_outFacingForNetworkDiscovery == null)
            {
                if (Blockentity is BlockEntityEGenerator entity && entity.Facing != Facing.None)
                    _outFacingForNetworkDiscovery = FacingHelper.Directions(entity.Facing).First();
                else
                    return null!; // fallback to default direction if not set
            }

            return _outFacingForNetworkDiscovery;
        }
    }



    /// <summary>
    /// Возвращает направление оси, в которой находится генератор
    /// </summary>
    public override int[] AxisSign
    {
        get
        {
            if (_axisSign == null && OutFacingForNetworkDiscovery != null)
            {
                int index = OutFacingForNetworkDiscovery.Index;
                _axisSign = (index >= 0 && index < _axisSigns.Length)
                    ? _axisSigns[index]
                    : _axisSigns[0]; // fallback to default
            }

            return _axisSign;
        }
    }



    /// <inheritdoc />
    public BEBehaviorEGenerator(BlockEntity blockEntity) : base(blockEntity)
    {
        EmaFilter = new(0.05f);
        this.GetParams();
    }

    /// <summary>
    /// Извлекаем параметры из ассетов
    /// </summary>  
    private void GetParams()
    {
        Params = MyMiniLib.GetAttributeArrayFloat(Block, "params", def_Params);

        I_max = Params[0];
        speed_max = Params[1];
        resistance_factor = Params[2];
        resistance_load = Params[3];
        base_resistance = Params[4];
        kpd_max = Params[5];

        AvgPowerOrder = 0;
    }

    /// <inheritdoc />
    public float Produce_give()
    {
        float speed = network?.Speed * GearedRatio ?? 0.0F;

        float power = (Math.Abs(speed) <= speed_max) // Задаем форму кривых тока(мощности)
            ? Math.Abs(speed) / speed_max * I_max
            : I_max; // Линейная горизонтальная

        PowerGive = power;
        return power;
    }

    /// <inheritdoc />
    public void Produce_order(float amount)
    {
        PowerOrder = amount;
        AvgPowerOrder = (float)EmaFilter.Update(Math.Min(PowerGive, PowerOrder));
    }

    /// <inheritdoc />
    public float getPowerGive() => PowerGive;

    /// <inheritdoc />
    public float getPowerOrder() => PowerOrder;

    /// <summary>
    /// Механическая сеть берет отсюда сопротивление этого генератора
    /// </summary>
    /// <returns></returns>
    public override float GetResistance()
    {
        if (IsBurned)
            return 9999.0F;

        var spd = Math.Abs(network?.Speed * GearedRatio ?? 0.0F);

        var res = base_resistance +
                  ((spd > speed_max)
                      ? resistance_load * (Math.Min(AvgPowerOrder, I_max) / I_max) + (resistance_factor * (float)Math.Pow((spd / speed_max), 2f))
                      : resistance_load * (Math.Min(AvgPowerOrder, I_max) / I_max) + (resistance_factor * spd / speed_max));

        res /= kpd_max; // Учитываем КПД

        return res;
    }

    
    /// <inheritdoc />
    public void Update()
    {
        //смотрим надо ли обновить модельку когда сгорает прибор
        if (Api.World.BlockAccessor.GetBlockEntity(Blockentity.Pos) is BlockEntityEGenerator
            {
                AllEparams: not null
            } entity)
        {
            var hasBurnout = entity.AllEparams.Any(e => e.burnout);
            if (hasBurnout)
                ParticleManager.SpawnBlackSmoke(Api.World, Pos.ToVec3d().Add(0.1, 0, 0.1));

            if (hasBurnout && entity.Block.Variant["type"] != "burned")
            {
                var type = "type";
                var variant = "burned";

                Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant(type, variant)).BlockId, Pos);
            }

            bool prepareBurnout = entity.AllEparams.Any(e => e.ticksBeforeBurnout > 0);
            if (prepareBurnout)
            {
                ParticleManager.SpawnWhiteSlowSmoke(this.Api.World, Pos.ToVec3d().Add(0.1, 0, 0.1));
            }
        }

        
    }



    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat(PowerOrderKey, PowerOrder);
        tree.SetFloat(PowerGiveKey, PowerGive);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        PowerOrder = tree.GetFloat(PowerOrderKey);
        PowerGive = tree.GetFloat(PowerGiveKey);
    }

    /// <summary>
    /// Подсказка при наведении на блок
    /// </summary>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        if (Api.World.BlockAccessor.GetBlockEntity(Blockentity.Pos) is not BlockEntityEGenerator)
            return;

        

        if (IsBurned)
            return;

        stringBuilder.AppendLine(StringHelper.Progressbar(Math.Min(PowerGive, PowerOrder) / I_max * 100));
        stringBuilder.AppendLine("└ " + Lang.Get("Production") + ": " + ((int)Math.Min(PowerGive, PowerOrder)).ToString() + "/" + I_max + " " + Lang.Get("W"));
        float speed = network?.Speed * GearedRatio ?? 0.0F;
        stringBuilder.AppendLine("└ " + Lang.Get("Speed") + ": " + speed.ToString("F3") + " " + Lang.Get("rps"));
    }



    /// <summary>
    /// Выдается игре шейп для отрисовки ротора
    /// </summary>
    /// <returns></returns>
    protected override CompositeShape? GetShape()
    {
        if (Api is not { } api || Blockentity is not BlockEntityEGenerator entity || entity.Facing == Facing.None ||
            entity.Block.Variant["type"] == "burned")
            return null;

        var direction = OutFacingForNetworkDiscovery;
        if (CompositeShape == null)
        {
            string tier = entity.Block.Variant["tier"];             // какой тир
            string type = "rotor";
            string[] types = new string[2] { "tier", "type" };// типы генератора
            string[] variants = new string[2] { tier, type };// нужные вариант генератора

            var location = Block.CodeWithVariants(types, variants);

            CompositeShape = api.World.BlockAccessor.GetBlock(location).Shape.Clone();
        }

        var shape = CompositeShape.Clone();

        if (direction == BlockFacing.NORTH)
            shape.rotateY = 0;

        if (direction == BlockFacing.EAST)
            shape.rotateY = 270;

        if (direction == BlockFacing.SOUTH)
            shape.rotateY = 180;

        if (direction == BlockFacing.WEST)
            shape.rotateY = 90;

        if (direction == BlockFacing.UP)
            shape.rotateX = 90;

        if (direction == BlockFacing.DOWN)
            shape.rotateX = 270;

        return shape;
    }

    protected override void updateShape(IWorldAccessor worldForResolve)
    {
        Shape = GetShape();
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        return false;
    }

    public override void WasPlaced(BlockFacing connectedOnFacing)
    {
    }
}