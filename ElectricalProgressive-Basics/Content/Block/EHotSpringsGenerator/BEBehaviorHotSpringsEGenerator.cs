using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EHotSpringsGenerator;

public class BEBehaviorHotSpringsEGenerator : BlockEntityBehavior, IElectricProducer
{
    private float _powerOrder; // Power requested (saved)
    public const string PowerOrderKey = "electricalprogressive:powerOrder";

    private float _powerGive; // Power given (saved)
    public const string PowerGiveKey = "electricalprogressive:powerGive";

    private bool hasBurnout;
    private bool prepareBurnout;

    public new BlockPos Pos => Blockentity.Pos;


    public BEBehaviorHotSpringsEGenerator(BlockEntity blockEntity) : base(blockEntity)
    {

    }


    public void Update()
    {
        if (Blockentity is not BlockEntityEHotSpringsGenerator entity ||
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

    }


    public float Produce_give()
    {
        if (Blockentity is not BlockEntityEHotSpringsGenerator temp)
        {
            return 0f;
        }
        _powerGive = temp.Power * temp.Kpd;
        return _powerGive;
    }


    public void Produce_order(float amount)
    {
        _powerOrder = amount;
    }


    public float getPowerGive() => _powerGive;


    public float getPowerOrder() => _powerOrder;


    /// <summary>
    /// Tooltip when hovering over the block
    /// </summary>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        if (Blockentity is not BlockEntityEHotSpringsGenerator entity)
            return;


        stringBuilder.AppendLine(StringHelper.Progressbar(Math.Min(_powerGive, _powerOrder) / entity.Power * 100));
        stringBuilder.AppendLine("└ " + Lang.Get("Production") + ": " +
                                 ((int)Math.Min(_powerGive, _powerOrder)).ToString() + "/" +
                                 ((int)entity.Power).ToString() + " " + Lang.Get("electricalprogressivebasics:W"));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:kpd") + ": " + (entity.Kpd * 100).ToString("F1") + " %");

        if (!string.IsNullOrEmpty(entity.ErrorMessage)) stringBuilder.AppendLine(Lang.Get(entity.ErrorMessage));
    }


    /// <summary>
    /// Save parameters to attribute tree
    /// </summary>
    /// <param name="tree"></param>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat(PowerOrderKey, _powerOrder);
        tree.SetFloat(PowerGiveKey, _powerGive);
    }


    /// <summary>
    /// Load parameters from attribute tree
    /// </summary>
    /// <param name="tree"></param>
    /// <param name="worldAccessForResolve"></param>
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        _powerOrder = tree.GetFloat(PowerOrderKey);
        _powerGive = tree.GetFloat(PowerGiveKey);
    }
}
