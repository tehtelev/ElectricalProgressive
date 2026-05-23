using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Block.EStove;

public class BEBehaviorEStove : BlockEntityBehavior, IElectricConsumer
{
    /// <summary>
    /// Текущее потребление
    /// </summary>
    public int PowerSetting { get; set; }


    public bool IsBurned => this.Block.Code.GetName().Contains("burned"); // пока так 

    public const string PowerSettingKey = "electricalprogressive:powersetting";

    private int _stoveTemperature;
    private bool hasBurnout;
    private bool prepareBurnout;

    /// <summary>
    /// Максимальное потребление
    /// </summary>
    private readonly int _maxConsumption;

    public BEBehaviorEStove(BlockEntity blockEntity) : base(blockEntity)
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
    }

    public bool Working
    {
        get
        {
            var working = false;
            if (Blockentity is BlockEntityEStove entity)
            {
                working = entity.CanHeatInput();
                _stoveTemperature = (int)entity.StoveTemperature;
            }

            return working;
        }
    }
    
    public float AvgConsumeCoeff { get; set; }
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        //проверяем не сгорел ли прибор
        if (Blockentity is not BlockEntityEStove entity)
            return;

        if (IsBurned)
        {
            entity.IsBurning = false;
            return;
        }

        stringBuilder.AppendLine(StringHelper.Progressbar(PowerSetting * 100.0f / _maxConsumption));
        stringBuilder.AppendLine("├ " + Lang.Get("electricalprogressivebasics:Consumption") + ": " + PowerSetting + "/" + _maxConsumption + " " + Lang.Get("electricalprogressivebasics:W"));
        stringBuilder.AppendLine("└ " + Lang.Get("Temperature") + ": " + ((int)_stoveTemperature).ToString() + "°");

        stringBuilder.AppendLine();
    }

    #region IElectricConsumer

    public float Consume_request()
    {
        if (Blockentity is BlockEntityEStove entity)
        {
            // Проверяем, можно ли нагревать входной слот и не завершен ли процесс плавки
            if (entity.CanHeatInput() && !entity.IsInputSlotCompleted())
                return _maxConsumption;
        }

        return PowerSetting = 0;
    }

    public void Consume_receive(float amount)
    {
        if (!Working)
            amount = 0;

        if (PowerSetting != amount)
            PowerSetting = (int)amount;
    }

    public void Update()
    {
        if (Blockentity is not BlockEntityEStove entity ||
            entity.ElectricalProgressive == null ||
            entity.ElectricalProgressive.AllEparams is null)
        {
            return;
        }

        bool anyBurnout = false;
        bool anyPrepareBurnout = false;

        foreach (var eParam in entity.ElectricalProgressive.AllEparams)
        {
            if (!hasBurnout && eParam.burnout)
            {
                hasBurnout = true;
                entity.MarkDirty(true);
            }

            if (!prepareBurnout && eParam.ticksBeforeBurnout > 0)
            {
                prepareBurnout = true;
                entity.MarkDirty(true);
            }

            if (eParam.burnout)
                anyBurnout = true;

            if (eParam.ticksBeforeBurnout > 0)
                anyPrepareBurnout = true;
        }

        if (!anyBurnout && hasBurnout)
        {
            hasBurnout = false;
            entity.MarkDirty(true);
        }

        if (!anyPrepareBurnout && prepareBurnout)
        {
            prepareBurnout = false;
            entity.MarkDirty(true);
        }

        // Кэшируем позицию для частиц
        var particlePos = Pos.ToVec3d().Add(0.1, 0, 0.1);

        

        if (!hasBurnout || entity.Block.Variant["state"] == "burned")
            return;

        // Кэшируем значение side
        var side = entity.Block.Variant["side"];

        // Используем предварительно созданные массивы для избежания аллокаций
        const string stateType = "state";
        const string sideType = "side";
        const string burnedVariant = "burned";

        // Получаем блок только один раз
        var burnedBlock = Api.World.GetBlock(Block.CodeWithVariants(
            [stateType, sideType],
            [burnedVariant, side]
        ));

        Api.World.BlockAccessor.ExchangeBlock(burnedBlock.BlockId, Pos);
    }

    public float getPowerReceive()
    {
        return this.PowerSetting;
    }


    public float getPowerRequest()
    {
        if (Blockentity is BlockEntityEStove entity)
        {
            if (entity.CanHeatInput() && !entity.IsInputSlotCompleted())
                return _maxConsumption;
        }

        return PowerSetting = 0;
    }

    #endregion




    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt(PowerSettingKey, PowerSetting);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        PowerSetting = tree.GetInt(PowerSettingKey);
    }
}