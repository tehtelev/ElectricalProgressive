using Cairo.Freetype;
using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Block.EHorn;

public class BEBehaviorEHorn : BEBehaviorBase, IElectricConsumer
{
    /// <summary>
    /// Дали энергии  (сохраняется)
    /// </summary>
    private float _powerReceive = 0;
    
    public const string PowerReceiveKey = "electricalprogressive:powerReceive";



    private float _maxTemp;

    /// <summary>
    /// Максимальное потребление
    /// </summary>
    private readonly int _maxConsumption;

    /// <summary>
    /// Максимальная температура
    /// </summary>
    private readonly float _maxTargetTemp;

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

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        //проверяем не сгорел ли прибор
        if (this.Api.World.BlockAccessor.GetBlockEntity(this.Blockentity.Pos) is not BlockEntityEHorn entity)
            return;

        if (IsBurned)
        {
            entity.IsBurning = false;
            return;
        }

        stringBuilder.AppendLine(StringHelper.Progressbar(_powerReceive / _maxConsumption * 100));
        stringBuilder.AppendLine("└ " + Lang.Get("Consumption") + ": " + ((int)_powerReceive).ToString() + "/" + _maxConsumption + " " + Lang.Get("W"));
        stringBuilder.AppendLine("└ " + Lang.Get("Temperature") + ": " + ((int)_maxTemp).ToString() + "° (" + Lang.Get("max") + ")");

        stringBuilder.AppendLine();
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
            _maxTemp = amount * _maxTargetTemp / _maxConsumption;
        }
    }

    public void Update()
    {
        //смотрим надо ли обновить модельку когда сгорает прибор
        if (this.Api.World.BlockAccessor.GetBlockEntity(this.Blockentity.Pos) is not BlockEntityEHorn entity ||
            entity.AllEparams == null)
            return;

        var hasBurnout = entity.AllEparams.Any(e => e.burnout);
        if (hasBurnout)
            ParticleManager.SpawnBlackSmoke(this.Api.World, Pos.ToVec3d().Add(0.1, 0, 0.1));

        bool prepareBurnout = entity.AllEparams.Any(e => e.ticksBeforeBurnout > 0);
        if (prepareBurnout)
        {
            ParticleManager.SpawnWhiteSlowSmoke(this.Api.World, Pos.ToVec3d().Add(0.1, 0, 0.1));
        }

        

        if (!hasBurnout || entity.Block.Variant["state"] == "burned")
            return;

        var side = entity.Block.Variant["side"];

        var types = new string[2] { "state", "side" };   //типы горна
        var variants = new string[2] { "burned", side };  //нужный вариант 

        this.Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariants(types, variants)).BlockId, Pos);
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

    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        _powerReceive = tree.GetFloat(PowerReceiveKey);

    }
}