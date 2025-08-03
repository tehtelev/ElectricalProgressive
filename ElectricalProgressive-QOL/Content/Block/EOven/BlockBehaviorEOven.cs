using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EOven;

public class BEBehaviorEOven : BEBehaviorBase, IElectricConsumer
{
    public int PowerSetting { get; set; }


    public const string PowerSettingKey = "electricalprogressive:powersetting";
    public const string OvenTemperatureKey = "electricalprogressive:oventemperature";

    /// <summary>
    /// Температура печи
    /// </summary>
    private float _ovenTemperature;

    /// <summary>
    /// Максимальное потребление
    /// </summary>
    private readonly int _maxConsumption;

    public BEBehaviorEOven(BlockEntity blockEntity) : base(blockEntity)
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
    }

    public bool Working
    {
        get
        {
            var working = false;
            if (Blockentity is not BlockEntityEOven entity)
                return working;

            _ovenTemperature = (int)entity.ovenTemperature;

            //проверяем количество занятых слотов и готовой еды
            var stack_count = 0;
            var stack_count_perfect = 0;

            for (var index = 0; index < entity.bakeableCapacity; ++index)
            {
                var itemstack = entity.ovenInv[index].Itemstack;
                if (itemstack == null)
                    continue;

                BakingProperties bakingProperties = BakingProperties.ReadFrom(itemstack);
                if (bakingProperties == null ||
                    
                    !itemstack.Attributes.GetBool("bakeable", true)) //если свойства выпекания не найдены
                {
                    stack_count_perfect++;
                    stack_count++;
                    continue;    // продолжаем цикл, если не выпекаемая еда в этом слоте
                }

                if (itemstack.Class == EnumItemClass.Block)
                {
                    var blockCode = itemstack.Block.Code.ToString();
                    if (blockCode.Contains("perfect") ||
                        blockCode.Contains("charred") ||
                        blockCode.Contains("rot") ||
                        blockCode.Contains("bake1") ||
                        blockCode.Contains("bake2") ||
                        blockCode.Contains("cooked") ||
                        blockCode.Contains("dry"))
                    {
                        stack_count_perfect++;
                    }
                }
                else
                {
                    var itemCode = itemstack.Item.Code.ToString();
                    if (itemCode.Contains("perfect") ||
                        itemCode.Contains("charred") ||
                        itemCode.Contains("rot") ||
                        itemCode.Contains("bake1") ||
                        itemCode.Contains("bake2") ||
                        itemCode.Contains("cooked") ||
                        itemCode.Contains("dry"))
                    {
                        stack_count_perfect++;
                    }
                }

                stack_count++;
            }

            if (stack_count_perfect == stack_count)   // если все готово - не работаем
                return false;
            

            if (stack_count > 0)
                return true;

            return working;
        }
    }


    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        //проверяем не сгорел ли прибор
        if (this.Api.World.BlockAccessor.GetBlockEntity(this.Blockentity.Pos) is not BlockEntityEOven entity)
            return;

        if (IsBurned)
        {
            return;
        }

        stringBuilder.AppendLine(StringHelper.Progressbar(PowerSetting * 100.0f / _maxConsumption));
        stringBuilder.AppendLine("├ " + Lang.Get("Consumption") + ": " + PowerSetting + "/" + _maxConsumption + " " + Lang.Get("W"));
        stringBuilder.AppendLine("└ " + Lang.Get("Temperature") + ": " + ((int)_ovenTemperature).ToString() + "°");

        stringBuilder.AppendLine();
    }

    #region IElectricConsumer

    public float Consume_request()
    {
        if (Working)
            return _maxConsumption;

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
        //смотрим надо ли обновить модельку когда сгорает прибор
        if (this.Api.World.BlockAccessor.GetBlockEntity(this.Blockentity.Pos) is not BlockEntityEOven entity ||
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
        if (Working)
            return _maxConsumption;

        return PowerSetting = 0;
    }

    #endregion



    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt(PowerSettingKey, PowerSetting);
        tree.SetFloat(OvenTemperatureKey, _ovenTemperature);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        PowerSetting = tree.GetInt(PowerSettingKey);
        _ovenTemperature = tree.GetFloat(OvenTemperatureKey, 0f);
    }
}