using ElectricalProgressive.Content.Block.EAccumulator;
using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;


namespace ElectricalProgressive.Content.Block.ETransformator;

public class BEBehaviorETransformator : BlockEntityBehavior, IElectricTransformator
{
   
    float _power;      //мощность


    public const string PowerKey = "electricalprogressive:_power";
    

    public BEBehaviorETransformator(BlockEntity blockEntity) : base(blockEntity)
    {
        HighVoltage = MyMiniLib.GetAttributeInt(this.Block, "voltage", 32);
        LowVoltage = MyMiniLib.GetAttributeInt(this.Block, "lowVoltage", 32);
    }

    public bool IsBurned => this.Block.Variant["state"] == "burned";
    public new BlockPos Pos => this.Blockentity.Pos;

    public int HighVoltage { get; set; }

    public int LowVoltage { get; set; }



    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        //проверяем не сгорел ли прибор
        if (Blockentity is not BlockEntityETransformator entity)
            return;
        

        if (IsBurned)
            return;

        //stringBuilder.AppendLine(StringHelper.Progressbar(getPower() / (lowVoltage * maxCurrent) * 100));
        //stringBuilder.AppendLine("└ " + Lang.Get("Power") + ": " + getPower() + " / " + lowVoltage * maxCurrent + " " + Lang.Get("W"));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Power") + ": " + ((int)getPower()).ToString() + " " + Lang.Get("electricalprogressivebasics:W"));
        stringBuilder.AppendLine();


    }


    bool hasBurnout = false;
    bool prepareBurnout = false;


    public void Update()
    {
        if (Blockentity is not BlockEntityETransformator entity ||
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

        // Обработка burnout
        if (hasBurnout)
        {
            // Проверяем и обновляем состояние блока если нужно
            if (entity.Block.Variant["state"] != "burned")
            {
                // Кэшируем блок для обмена
                var burnedBlock = Api.World.GetBlock(Block.CodeWithVariant("state", "burned"));
                Api.World.BlockAccessor.ExchangeBlock(burnedBlock.BlockId, Pos);
            }
        }


    }


    public float getPower()
    {
        return this._power;
    }

    public void setPower(float power)
    {
        this._power = power;
    }



    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat(PowerKey, _power);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        _power = tree.GetFloat(PowerKey);
    }
}
