using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Block.EHorn;

public class BEBehaviorEHorn : BlockEntityBehavior, IElectricConsumer
{
    /// <summary>
    /// Дали энергии  (сохраняется)
    /// </summary>
    private float _powerReceive = 0;
    
    public const string PowerReceiveKey = "electricalprogressive:powerReceive";
    public const string MaxTempKey = "electricalprogressive:maxTemp";

    public bool IsBurned => this.Block.Code.GetName().Contains("burned"); // пока так 

    private float _maxTemp;
    private bool hasBurnout;
    private bool prepareBurnout;

    /// <summary>
    /// Максимальное потребление
    /// </summary>
    private readonly int _maxConsumption;

    /// <summary>
    /// Максимальная температура
    /// </summary>
    private readonly float _maxTargetTemp;

    public float AvgConsumeCoeff { get; set; }

    public bool HasItems
    {
        get
        {
            var hasItems = false;
            if (Blockentity is BlockEntityEHorn entity)
                hasItems = entity?.Contents?.StackSize > 0;

            return hasItems;
        }
    }

    public BEBehaviorEHorn(BlockEntity blockEntity) : base(blockEntity)
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
        _maxTargetTemp = MyMiniLib.GetAttributeFloat(this.Block, "maxTargetTemp", 1100.0F);
    }





    #region IElectricConsumer

    public float Consume_request()
    {
        if (HasItems)
            return _maxConsumption;

        return 0;
    }


    public void Consume_receive(float amount)
    {
        if (!HasItems)
            amount = 0;

        if (this._powerReceive != amount)
        {
            this._powerReceive = amount;
            _maxTemp = amount * _maxTargetTemp / _maxConsumption; // обновляем показатель максимальной температуры 
        }
    }

    public void Update()
    {
        if (Blockentity is not BlockEntityEHorn entity ||
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
        return this._powerReceive;
    }

    public float getPowerRequest()
    {
        if (HasItems)
            return _maxConsumption;

        return 0;
    }

    #endregion




    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat(PowerReceiveKey, _powerReceive);
        tree.SetFloat(MaxTempKey, _maxTemp);

    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        _powerReceive = tree.GetFloat(PowerReceiveKey);
        _maxTemp = tree.GetFloat(MaxTempKey);
    }


    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        //проверяем не сгорел ли прибор
        if (Blockentity is not BlockEntityEHorn entity)
            return;

        // если сгорел - тушим горн
        if (IsBurned)
        {
            entity.IsBurning = false;
            return;
        }

        stringBuilder.AppendLine(StringHelper.Progressbar(_powerReceive / _maxConsumption * 100));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Consumption") + ": " + ((int)_powerReceive).ToString() + "/" + _maxConsumption + " " + Lang.Get("electricalprogressivebasics:W"));
        stringBuilder.AppendLine("└ " + Lang.Get("Temperature") + ": " + ((int)_maxTemp).ToString() + "° (" + Lang.Get("max") + ")");

        stringBuilder.AppendLine();
    }
}