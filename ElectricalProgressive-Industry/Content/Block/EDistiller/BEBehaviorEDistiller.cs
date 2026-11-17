using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Block.EDistiller;

public class BEBehaviorEDistiller : BlockEntityBehavior, IElectricConsumer
{
    public int PowerSetting { get; set; }
    public const string PowerSettingKey = "electricalprogressive:powersetting";
    public float AvgConsumeCoeff { get; set; }
    public bool IsBurned => Block.Code.GetName().Contains("burned");

    private readonly int _maxConsumption;
    private float _accumulatedEnergy;
    private float _lastEnergyTime;
    private float _recipeProgress;
    private bool _hasBurnout;
    private bool _prepareBurnout;

    public BEBehaviorEDistiller(BlockEntity blockEntity) : base(blockEntity)
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(Block, "maxConsumption", 100);
    }

    public bool IsWorking
    {
        get
        {
            if (Blockentity is not BlockEntityEDistiller entity)
                return false;
            if (!entity.StructureComplete)
                return false;
            _recipeProgress = entity.RecipeProgress;
            return entity.CanWork();
        }
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);
        if (Blockentity is not BlockEntityEDistiller entity || IsBurned || !entity.StructureComplete)
            return;

        stringBuilder.AppendLine(StringHelper.Progressbar(PowerSetting * 100.0f / _maxConsumption));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Consumption") + ": " +
                                PowerSetting + "/" + _maxConsumption + " " +
                                Lang.Get("electricalprogressivebasics:W"));
        stringBuilder.AppendLine(Lang.Get("electricalprogressiveindustry:edistiller-temp-value",
            entity.MashTemperature.ToString("0"), entity.TargetTemperature.ToString("0")));
        stringBuilder.AppendLine(Lang.Get("electricalprogressiveindustry:edistiller-mash") + ": " +
                                 entity.MashLitres.ToString("0.#") + "/" + entity.MashCapacity.ToString("0") + " L");
        stringBuilder.AppendLine(Lang.Get("electricalprogressiveindustry:edistiller-water") + ": " +
                                 entity.WaterLitres.ToString("0.#") + "/" + entity.WaterCapacity.ToString("0") + " L");
        stringBuilder.AppendLine(Lang.Get("electricalprogressiveindustry:edistiller-product") + ": " +
                                 entity.ProductLitres.ToString("0.#") + "/" + entity.ProductCapacity.ToString("0") + " L");
        stringBuilder.AppendLine();
    }

    public float Consume_request() => IsWorking ? _maxConsumption : PowerSetting = 0;

    public void Consume_receive(float amount)
    {
        if (!IsWorking)
        {
            PowerSetting = 0;
            _accumulatedEnergy = 0;
            _lastEnergyTime = 0;
            return;
        }

        if (PowerSetting != (int)amount)
            PowerSetting = (int)amount;

        if (amount <= 0 || Blockentity is not BlockEntityEDistiller entity)
            return;

        var currentTime = (float)(Api.World.ElapsedMilliseconds / 1000.0);
        if (_lastEnergyTime <= 0.01f)
        {
            _lastEnergyTime = currentTime;
            return;
        }

        var deltaTime = currentTime - _lastEnergyTime;
        _lastEnergyTime = currentTime;
        if (deltaTime > 0.1f)
            deltaTime = 0.1f;

        _accumulatedEnergy += amount * deltaTime;
        if (_accumulatedEnergy < 1f)
            return;

        var whole = (int)_accumulatedEnergy;
        _accumulatedEnergy -= whole;
        entity.AddEnergy(whole);
    }

    public void Update()
    {
        if (Blockentity is not BlockEntityEDistiller entity ||
            entity.ElectricalProgressive?.AllEparams is null)
            return;

        var anyBurnout = false;
        var anyPrepare = false;
        foreach (var eParam in entity.ElectricalProgressive.AllEparams)
        {
            if (eParam.burnout)
            {
                anyBurnout = true;
                if (!_hasBurnout)
                {
                    _hasBurnout = true;
                    entity.MarkDirty(true);
                }
            }

            if (eParam.ticksBeforeBurnout > 0)
            {
                anyPrepare = true;
                if (!_prepareBurnout)
                {
                    _prepareBurnout = true;
                    entity.MarkDirty(true);
                }
            }
        }

        if (!anyBurnout && _hasBurnout)
        {
            _hasBurnout = false;
            entity.MarkDirty(true);
        }

        if (!anyPrepare && _prepareBurnout)
        {
            _prepareBurnout = false;
            entity.MarkDirty(true);
        }
    }

    public float getPowerReceive() => PowerSetting;
    public float getPowerRequest() => IsWorking ? _maxConsumption : PowerSetting = 0;

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt(PowerSettingKey, PowerSetting);
        tree.SetFloat("recipeProgress", _recipeProgress);
        tree.SetFloat("accumulatedEnergy", _accumulatedEnergy);
        tree.SetFloat("lastEnergyTime", _lastEnergyTime);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        PowerSetting = tree.GetInt(PowerSettingKey);
        _recipeProgress = tree.GetFloat("recipeProgress");
        _accumulatedEnergy = tree.GetFloat("accumulatedEnergy");
        _lastEnergyTime = tree.GetFloat("lastEnergyTime");
    }
}
