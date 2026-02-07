using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EDrawing;

public class BEBehaviorEDrawing : BlockEntityBehavior, IElectricConsumer
{
    /// <summary>
    /// Текущее потребление
    /// </summary>
    public int PowerSetting { get; set; }

    public bool IsBurned => this.Block.Code.GetName().Contains("burned"); // пока так 

    public const string PowerSettingKey = "electricalprogressive:powersetting";

    public float AvgConsumeCoeff { get; set; }

    /// <summary>
    /// Максимальное потребление
    /// </summary>
    private readonly int _maxConsumption;
    
    /// <summary>
    /// Прогресс текущего крафта (0-1)
    /// </summary>
    private float _recipeProgress;
    private bool hasBurnout;
    private bool prepareBurnout;

    public BEBehaviorEDrawing(BlockEntity blockEntity) : base(blockEntity)
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
    }




    public bool IsWorking
    {
        get
        {
            if (Blockentity is BlockEntityEDrawing entity)
            {
                // прибор сгорел?
                if (entity.ElectricalProgressive==null &&
                    entity.ElectricalProgressive.AllEparams == null &&
                    entity.ElectricalProgressive.AllEparams.Any(e => e.burnout))
                    return false;


                var entityStack = entity.Inventory[0]?.Itemstack;

                // со стаком что - то не так?
                if (entityStack is null ||
                    entityStack.StackSize == 0 ||
                    entityStack.Collectible == null ||
                    entityStack.Collectible.Attributes == null)
                    return false;

                entityStack = entity.Inventory[1]?.Itemstack;

                // со стаком что - то не так?
                if (entityStack is null ||
                    entityStack.StackSize == 0 ||
                    entityStack.Collectible == null ||
                    entityStack.Collectible.Attributes == null)
                    return false;


                var hasRecipe = BlockEntityEDrawing.FindMatchingRecipe(ref entity.CurrentRecipe, ref entity.CurrentRecipeName, entity.inventory);
                _recipeProgress = entity.RecipeProgress;
                return hasRecipe;
                    
            }
            return false;
        }
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        //проверяем не сгорел ли прибор
        if (this.Blockentity is not BlockEntityEDrawing entity)
            return;

        if (IsBurned)
        {
            return;
        }

        stringBuilder.AppendLine(StringHelper.Progressbar(PowerSetting * 100.0f / _maxConsumption));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Consumption") + ": " + PowerSetting + "/" + _maxConsumption + " " + Lang.Get("electricalprogressivebasics:W"));

        stringBuilder.AppendLine();
    }

    #region IElectricConsumer

    public float Consume_request()
    {
        if (IsWorking)
            return _maxConsumption;

        return PowerSetting = 0;
    }

    public void Consume_receive(float amount)
    {
        if (!IsWorking)
            amount = 0;

        if (PowerSetting != amount)
            PowerSetting = (int)amount;
    }

    public void Update()
    {
        //смотрим надо ли обновить модельку когда сгорает прибор
        if (Blockentity is not BlockEntityEDrawing entity ||
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

    public float getPowerReceive()
    {
        return this.PowerSetting;
    }


    public float getPowerRequest()
    {
        if (IsWorking)
            return _maxConsumption;

        return PowerSetting = 0;
    }

    #endregion




    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt(PowerSettingKey, PowerSetting);
        tree.SetFloat("recipeProgress", _recipeProgress);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        PowerSetting = tree.GetInt(PowerSettingKey);
        _recipeProgress = tree.GetFloat("recipeProgress");
    }
}