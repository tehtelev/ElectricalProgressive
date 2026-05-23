using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ESolarGenerator;

public class BEBehaviorSolarEGenerator : BlockEntityBehavior, IElectricProducer
{
    private float _powerOrder; // Просят столько энергии (сохраняется)
    public const string PowerOrderKey = "electricalprogressive:powerOrder";

    private float _powerGive; // Отдаем столько энергии (сохраняется)
    public const string PowerGiveKey = "electricalprogressive:powerGive";

    private bool hasBurnout;
    private bool prepareBurnout;

    public new BlockPos Pos => Blockentity.Pos;


    public BEBehaviorSolarEGenerator(BlockEntity blockEntity) : base(blockEntity)
    {
        
    }


    public void Update()
    {
        if (Blockentity is not BlockEntityESolarGenerator entity ||
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
        if (Blockentity is not BlockEntityESolarGenerator temp)
        {
            return 0f;
        }
        // Only give
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
    /// Подсказка при наведении на блок
    /// </summary>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        if (Blockentity is not BlockEntityESolarGenerator entity)
            return;


        stringBuilder.AppendLine(StringHelper.Progressbar(Math.Min(_powerGive, _powerOrder) / entity.Power * 100));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Production") + ": " +
                                 ((int)Math.Min(_powerGive, _powerOrder)).ToString() + "/" +
                                 ((int)entity.Power).ToString() + " " + Lang.Get("electricalprogressivebasics:W"));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Prod_potential") + ": " + ((int)_powerGive).ToString() + " " + Lang.Get("electricalprogressivebasics:W"));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:kpd") + ": " + (entity.Kpd * 100).ToString("F1") + " %");
    }


    /// <summary>
    /// Сохранение параметров в дерево атрибутов
    /// </summary>
    /// <param name="tree"></param>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat(PowerOrderKey, _powerOrder);
        tree.SetFloat(PowerGiveKey, _powerGive);
    }


    /// <summary>
    /// Загрузка параметров из дерева атрибутов
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