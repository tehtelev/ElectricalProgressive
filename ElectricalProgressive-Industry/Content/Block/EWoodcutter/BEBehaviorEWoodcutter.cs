using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ElectricalProgressive.Content.Block.EWoodcutter;

public class BEBehaviorEWoodcutter : BEBehaviorBase, IElectricConsumer
{
    public int PowerSetting { get; set; }

    /// <summary>
    /// Максимальное потребление
    /// </summary>
    private readonly int _maxConsumption;

    private readonly BlockEntityEWoodcutter _entityEWoodcutter;

    public BEBehaviorEWoodcutter(BlockEntity blockentity) : base(blockentity)
    {

        _maxConsumption = MyMiniLib.GetAttributeInt(Block, "maxConsumption", 300);
        _entityEWoodcutter = blockentity as BlockEntityEWoodcutter;

    }

    private float CalculateRequest()
    {
        var request = 0f;
        switch (_entityEWoodcutter.Stage)
        {
            case BlockEntityEWoodcutter.WoodcutterStage.PlantTree:
                request = 10f;
                break;

            case BlockEntityEWoodcutter.WoodcutterStage.WaitFullGrowth:
                request = 5f;
                break;

            case BlockEntityEWoodcutter.WoodcutterStage.ChopTree:
                var woodTier = _entityEWoodcutter.WoodTier;
                var treeResistance = _entityEWoodcutter.TreeResistance;

                const float basePower = 10f;

                if (treeResistance == 0 && woodTier == 0)
                    return basePower;

                const float resistanceFactor = 0.8f;
                const float tierFactor = 1.2f;
                const float nonlinearity = 0.7f;

                var power = basePower + (MathF.Pow(treeResistance, nonlinearity) * resistanceFactor * (1 + woodTier * tierFactor));
                request = Math.Clamp(power, 0, _maxConsumption);
                break;

            case BlockEntityEWoodcutter.WoodcutterStage.None:
            default:
                request = 0f;
                break;
        }

        return request;
    }

    public float Consume_request()
    {
        return CalculateRequest();
    }

    public void Consume_receive(float amount)
    {
        var request = CalculateRequest();

        var newValue = amount < request;
        if (newValue != _entityEWoodcutter.IsNotEnoughEnergy)
        {
            _entityEWoodcutter.IsNotEnoughEnergy = newValue;
            _entityEWoodcutter.MarkDirty();
        }

        PowerSetting = (int)amount;
    }

    public float getPowerReceive()
    {
        return PowerSetting;
    }

    public float getPowerRequest()
    {
        var request = CalculateRequest();
        return Math.Clamp(request, 0, _maxConsumption);
    }

    public void Update()
    {
        //смотрим надо ли обновить модельку когда сгорает прибор
        if (Api.World.BlockAccessor.GetBlockEntity(Blockentity.Pos) is not BlockEntityEWoodcutter entity)
            return;

        var hasBurnout = entity.AllEparams.Any(e => e.burnout);
        if (hasBurnout)
            ParticleManager.SpawnBlackSmoke(Api.World, Pos.ToVec3d().Add(0.5, 0.5, 0.5));

        if (!hasBurnout || entity.Block.Variant["state"] == "burned")
            return;

        var side = entity.Block.Variant["side"];

        var types = new string[2] { "state", "side" };
        var variants = new string[2] { "burned", side };

        Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariants(types, variants)).BlockId, Pos);
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        //проверяем не сгорел ли прибор
        if (Api.World.BlockAccessor.GetBlockEntity(Blockentity.Pos) is BlockEntityEWoodcutter entity)
        {
            if (IsBurned)
            {
                stringBuilder.AppendLine(Lang.Get("Burned"));
            }
            else
            {
                stringBuilder.AppendLine(StringHelper.Progressbar(PowerSetting * 100.0f / _maxConsumption));
                stringBuilder.AppendLine("└ " + Lang.Get("Consumption") + ": " + PowerSetting + "/" + _maxConsumption + " " + Lang.Get("W"));
            }
        }

        stringBuilder.AppendLine();
    }
}