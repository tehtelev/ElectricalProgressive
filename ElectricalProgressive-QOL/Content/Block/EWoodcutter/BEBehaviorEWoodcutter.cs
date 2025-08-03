using ElectricalProgressive.Content.Block.EStove;
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
        _maxConsumption = MyMiniLib.GetAttributeInt(Block, "maxConsumption", 100);
        _entityEWoodcutter = (blockentity as BlockEntityEWoodcutter)!;
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

                // TODO: Нужен баланс
                request = Math.Clamp(woodTier * treeResistance * 0.5f, 0, _maxConsumption);
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

        _entityEWoodcutter.IsNotEnoughEnergy = amount < request;
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