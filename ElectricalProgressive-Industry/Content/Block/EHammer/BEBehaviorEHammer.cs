using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Block.ECentrifuge;

public class BEBehaviorEHammer : BEBehaviorBase, IElectricConsumer
{
    /// <summary>
    /// Текущее потребление
    /// </summary>
    public int PowerSetting { get; set; }

    public const string PowerSettingKey = "electricalprogressive:powersetting";



    /// <summary>
    /// Максимальное потребление
    /// </summary>
    private readonly int _maxConsumption;
    
    /// <summary>
    /// Прогресс текущего крафта (0-1)
    /// </summary>
    private float _recipeProgress;

    public BEBehaviorEHammer(BlockEntity blockEntity) : base(blockEntity)
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
    }


    public bool IsWorking
    {
        get
        {
            if (Blockentity is BlockEntityEHammer entity)
            {
                bool hasRecipe = entity.FindMatchingRecipe();
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
        if (this.Api.World.BlockAccessor.GetBlockEntity(this.Blockentity.Pos) is not BlockEntityEHammer entity)
            return;

        if (IsBurned)
        {
            return;
        }

        stringBuilder.AppendLine(StringHelper.Progressbar(PowerSetting * 100.0f / _maxConsumption));
        stringBuilder.AppendLine("└ " + Lang.Get("Consumption") + ": " + PowerSetting + "/" + _maxConsumption + " " + Lang.Get("W"));

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
        if (this.Api.World.BlockAccessor.GetBlockEntity(this.Blockentity.Pos) is not BlockEntityEHammer entity ||
            entity.AllEparams == null)
        {
            return;
        }

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

        // MarkDirty не нужен тут
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