using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Block.ECloche;

public class BEBehaviorECloche : BlockEntityBehavior, IElectricConsumer
{
    public const string PowerSettingKey = "electricalprogressive:powersetting";

    private readonly int _maxConsumption;

    public int PowerSetting { get; set; }
    public float AvgConsumeCoeff { get; set; }

    public BEBehaviorECloche(BlockEntity blockEntity) : base(blockEntity)
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(Block, "maxConsumption", 40);
    }

    private BlockEntityECloche Cloche => Blockentity as BlockEntityECloche;

    public bool IsWorking
    {
        get
        {
            var entity = Cloche;
            if (entity?.ElectricalProgressive?.AllEparams == null)
                return false;

            if (entity.ElectricalProgressive.AllEparams.Any(e => e.burnout))
                return false;

            if (entity.WaitingForOutput)
                return false;

            return entity.HasCrop;
        }
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        var entity = Cloche;
        if (entity == null)
            return;

        entity.RefreshClimate();
        stringBuilder.AppendLine(StringHelper.Progressbar(PowerSetting * 100f / _maxConsumption));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Consumption") + ": " + PowerSetting + "/" + (int)entity.DesiredWatts + " " + Lang.Get("electricalprogressivebasics:W"));
        stringBuilder.AppendLine("└ " + entity.CropTitle());
        if (entity.GrowthStages() > 0)
        {
            stringBuilder.AppendLine("└ " + Lang.Get("ecloche-stage", entity.GrowthStage(), entity.GrowthStages(), (int)(entity.Progress * 100f)));
            stringBuilder.AppendLine("└ " + Lang.Get("ecloche-eta", entity.RemainingLabel()));
        }
        stringBuilder.AppendLine("└ " + Lang.Get("ecloche-temp", entity.InsideTemp.ToString("0.0"), entity.OptimalTemp.ToString("0.0"), entity.OutsideTemp.ToString("0.0")));
        stringBuilder.AppendLine("└ " + Lang.Get("ecloche-moist", (entity.Moisture * 100f).ToString("0")));
        stringBuilder.AppendLine("└ " + Lang.Get("ecloche-npk", entity.NutrientN.ToString("0"), entity.NutrientP.ToString("0"), entity.NutrientK.ToString("0")));
        stringBuilder.AppendLine("└ " + Lang.Get("ecloche-water", entity.Litres.ToString("0.0"), entity.Capacity.ToString("0")));

        if (!entity.HasCrop)
            stringBuilder.AppendLine("└ " + Lang.Get("ecloche-need-seed"));
        else if (entity.Litres <= 0.01f && entity.Moisture < 0.3f)
            stringBuilder.AppendLine("└ " + Lang.Get("ecloche-need-water"));
        else if (entity.WaitingForOutput)
            stringBuilder.AppendLine("└ " + Lang.Get("ecloche-output-full"));
        else if (!IsWorking && PowerSetting <= 0)
            stringBuilder.AppendLine("└ " + Lang.Get("ecloche-need-power"));

        stringBuilder.AppendLine();
    }

    public float Consume_request()
    {
        var entity = Cloche;
        if (!IsWorking || entity == null)
        {
            PowerSetting = 0;
            return 0f;
        }

        return entity.ComputeDesiredWatts();
    }

    public void Consume_receive(float amount)
    {
        var entity = Cloche;
        if (!IsWorking || entity == null)
        {
            PowerSetting = 0;
            entity?.SetReceivedPower(0f);
            if (entity != null)
                entity.LampOn = false;
            return;
        }

        PowerSetting = (int)amount;
        entity.SetReceivedPower(amount);
    }

    public void Update()
    {
    }

    public float getPowerReceive() => PowerSetting;

    public float getPowerRequest()
    {
        if (!IsWorking)
        {
            PowerSetting = 0;
            return 0f;
        }

        return Cloche?.ComputeDesiredWatts() ?? 0f;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt(PowerSettingKey, PowerSetting);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        PowerSetting = tree.GetInt(PowerSettingKey);
    }
}
