using ElectricalProgressive.Content.Block.EGenerator;
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

namespace ElectricalProgressive.Content.Block.EMotor;

public class BEBehaviorEMotor : BEBehaviorMPBase, IElectricConsumer
{

    /// <summary>
    /// Нужно энергии (сохраняется)
    /// </summary>
    private float powerRequest;
    public const string PowerRequestKey = "electricalprogressive:powerRequest";

    /// <summary>
    /// Дали энергии  (сохраняется)
    /// </summary>
    private float powerReceive;
    public const string PowerReceiveKey = "electricalprogressive:powerReceive";

    /// <summary>
    /// Минимальный ток
    /// </summary>
    private float I_min;
    /// <summary>
    /// Максимальный ток
    /// </summary>
    private float I_max;
    /// <summary>
    /// Максимальный крутящий момент
    /// </summary>
    private float torque_max;
    /// <summary>
    /// Пиковый КПД
    /// </summary>
    private float kpd_max;
    /// <summary>
    /// Максимальная скорость вращения
    /// </summary>
    private float speed_max;
    /// <summary>
    /// Множитель сопротивления
    /// </summary>
    private static float resistance_factor;
    /// <summary>
    /// Базовое сопротивление
    /// </summary>
    private float base_resistance;
    /// <summary>
    /// Текущий крутящий момент
    /// </summary>
    private float torque;
    /// <summary>
    /// Ток потребления
    /// </summary>
    private float I_value;
    /// <summary>
    /// КПД
    /// </summary>
    //private float kpd;

    /// <summary>
    /// Заглушка. I_min , I_max, torque_max, kpd_max, speed_max, resistance_factor, base_resistance 
    /// </summary>
    private float[] def_Params => new[] { 10.0F, 100.0F, 0.5F, 0.75F, 0.5F, 0.1F, 0.05F };
    /// <summary>
    /// Сюда берем параметры из ассетов
    /// </summary>
    private float[] Params = { 0, 0, 0, 0, 0, 0, 0 };


    private static readonly int[][] _axisSigns = new int[][]
    {
        new[] { +0, +0, -1 }, // index 0
        new[] { -1, +0, +0 }, // index 1
        new[] { +0, +0, -1 }, // index 2
        new[] { -1, +0, +0 }, // index 3
        new[] { +0, -1, +0 }, // index 4
        new[] { +0, +1, +0 }  // index 5
    };


    private bool IsBurned => Block.Variant["type"] == "burned";



    protected CompositeShape? CompositeShape;  //не трогать уровни доступа



    private BlockFacing _outFacingForNetworkDiscovery = null!;
    private int[] _axisSign = null!;


    public override BlockFacing OutFacingForNetworkDiscovery
    {
        get
        {
            if (_outFacingForNetworkDiscovery == null)
            {
                if (Blockentity is BlockEntityEMotor entity && entity.Facing != Facing.None)
                    _outFacingForNetworkDiscovery = FacingHelper.Directions(entity.Facing).First();
                else
                    _outFacingForNetworkDiscovery = BlockFacing.NORTH; // fallback to default direction if not set
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
            if (_axisSign == null)
            {
                int index = OutFacingForNetworkDiscovery.Index;
                _axisSign = (index >= 0 && index < _axisSigns.Length)
                    ? _axisSigns[index]
                    : _axisSigns[0]; // fallback to default
            }

            return _axisSign;
        }
    }





    public new BlockPos Pos => Position;

    /// <inheritdoc />
    public BEBehaviorEMotor(BlockEntity blockentity) : base(blockentity)
    {
        this.GetParams();
        powerReceive = 0;
        powerRequest = I_max;
    }

    /// <summary>
    /// Извлекаем параметры из ассетов
    /// </summary>
    private void GetParams()
    {
        Params = MyMiniLib.GetAttributeArrayFloat(Block, "params", def_Params);
        I_min = Params[0];
        I_max = Params[1];
        torque_max = Params[2];
        kpd_max = Params[3];
        speed_max = Params[4];
        resistance_factor = Params[5];
        base_resistance = Params[6];
    }


    /// <inheritdoc />
    public float Consume_request()
    {
        return powerRequest;
    }

    /// <inheritdoc />
    public void Consume_receive(float amount)
    {
        powerReceive = amount;
    }

    /// <inheritdoc />
    public void Update()
    {
        //смотрим надо ли обновить модельку когда сгорает прибор
        if (this.Api.World.BlockAccessor.GetBlockEntity(this.Blockentity.Pos) is BlockEntityEMotor { AllEparams: not null } entity)
        {
            bool hasBurnout = entity.AllEparams.Any(e => e.burnout);

            if (hasBurnout)
            {
                ParticleManager.SpawnBlackSmoke(this.Api.World, Pos.ToVec3d().Add(0.1, 0, 0.1));
            }

            if (hasBurnout && entity.Block.Variant["type"] != "burned")
            {
                string type = "type";
                string variant = "burned";

                this.Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant(type, variant)).BlockId, Pos);

            }

            bool prepareBurnout = entity.AllEparams.Any(e => e.ticksBeforeBurnout > 0);
            if (prepareBurnout)
            {
                ParticleManager.SpawnWhiteSlowSmoke(this.Api.World, Pos.ToVec3d().Add(0.1, 0, 0.1));
            }
        }

        
    }

    /// <inheritdoc />
    public float getPowerReceive() => powerReceive;

    /// <inheritdoc />
    public float getPowerRequest() => powerRequest;

    // не удалять
    public override float GetResistance()
    {
        if (IsBurned)
            return 9999.0F;

        var spd = Math.Abs(Network?.Speed * GearedRatio ?? 0.0f);


        return base_resistance +
               ((Math.Abs(spd) > speed_max)
                   ? resistance_factor * (float)Math.Pow((spd / speed_max), 2f)
                   : resistance_factor * spd / speed_max);


    }

    /// <summary>
    /// Основной метод поведения двигателя возвращающий момент и сопротивление
    /// </summary>
    public override float GetTorque(long tick, float speed, out float resistance)
    {
        torque = 0f;                            // Текущий крутящий момент
        resistance = GetResistance();         // Вычисляем текущее сопротивление двигателя


        I_value = 0;                        // Ток потребления

        float I_amount = powerReceive;     // Доступно тока/энергии 

        if (I_amount <= I_min)                   // Если ток меньше минимального, двигатель не работает
            return torque;

        I_value = Math.Min(I_amount, I_max);    // Берем, что дают


        if (I_value < I_min)                                        // Если ток меньше минимального, двигатель не работает
            torque = 0.0F;
        else
            torque = I_value / I_max * torque_max;      // линейная


        torque *= kpd_max; // учитываем КПД


        powerRequest = I_max;                                        // Запрашиваем энергии столько, сколько нужно  для работы (работает как положено)


        return propagationDir == OutFacingForNetworkDiscovery     // Возвращаем все значения
            ? 1f * torque
            : -1f * torque;
    }



    /// <summary>
    /// Выдается игре шейп для отрисовки ротора
    /// </summary>
    /// <returns></returns>
    protected override CompositeShape? GetShape()
    {
        if (Api is not { } api || Blockentity is not BlockEntityEMotor entity || entity.Facing == Facing.None ||
            entity.Block.Variant["type"] == "burned")
            return null;

        var direction = OutFacingForNetworkDiscovery;

        if (CompositeShape == null)
        {
            string tier = entity.Block.Variant["tier"];             //какой тир
            string type = "rotor";
            string[] types = new string[2] { "tier", "type" };//типы генератора
            string[] variants = new string[2] { tier, type };//нужные вариант генератора

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

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat(PowerRequestKey, powerRequest);
        tree.SetFloat(PowerReceiveKey, powerReceive);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        powerRequest = tree.GetFloat(PowerRequestKey);
        powerReceive = tree.GetFloat(PowerReceiveKey);
    }


    /// <summary>
    /// Подсказка при наведении на блок
    /// </summary>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        //проверяем не сгорел ли прибор
        if (Api.World.BlockAccessor.GetBlockEntity(Blockentity.Pos) is not BlockEntityEMotor)
            return;

      

        if (IsBurned)
            return;

        stringBuilder.AppendLine(StringHelper.Progressbar(powerReceive / I_max * 100));
        stringBuilder.AppendLine("└ " + Lang.Get("Consumption") + ": " + ((int)powerReceive).ToString() + "/" + I_max + " " + Lang.Get("W"));

        float speed = network?.Speed * GearedRatio ?? 0.0F;
        stringBuilder.AppendLine("└ " + Lang.Get("Speed") + ": " + speed.ToString("F3") + " " + Lang.Get("rps"));

    }

    public override void WasPlaced(BlockFacing connectedOnFacing)
    {

    }
}