using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Block.EMetalForming;

public class BEBehaviorEMetalForming : BlockEntityBehavior, IElectricConsumer
{
    public int PowerSetting { get; set; }

    public const string PowerSettingKey = "electricalprogressive:powersetting";

    private float _fractionalEnergy;
    private float _lastEnergyTime;

    public bool IsBurned => Block.Code.GetName().Contains("burned");

    public float AvgConsumeCoeff { get; set; }

    private readonly int _maxConsumption;

    private bool hasBurnout;
    private bool prepareBurnout;

    public BEBehaviorEMetalForming(BlockEntity blockEntity) : base(blockEntity)
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 200);
    }

    public bool IsWorking
    {
        get
        {
            if (Blockentity is not BlockEntityEMetalForming entity)
                return false;

            if (!entity.StructureComplete)
                return false;

            if (entity.ElectricalProgressive == null &&
                entity.ElectricalProgressive.AllEparams == null &&
                entity.ElectricalProgressive.AllEparams.Any(e => e.burnout))
                return false;

            return entity.CanHeatOrCraft;
        }
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        if (this.Blockentity is not BlockEntityEMetalForming entity)
            return;

        if (IsBurned)
            return;

        if (!entity.StructureComplete)
        {
            stringBuilder.AppendLine(Lang.Get("electricalprogressivecore:construction-incomplete"));
            stringBuilder.AppendLine(Lang.Get("electricalprogressivecore:construction-hint"));
            stringBuilder.AppendLine();
            return;
        }

        stringBuilder.AppendLine(StringHelper.Progressbar(PowerSetting * 100.0f / _maxConsumption));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Consumption") + ": " + PowerSetting + "/" + _maxConsumption + " " + Lang.Get("electricalprogressivebasics:W"));

        if (!string.IsNullOrEmpty(entity.CurrentRecipeName))
            stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressiveindustry:emetalforming-recipe") + ": " + entity.CurrentRecipeName);

        if (entity.NeededInputCount > 0)
            stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressiveindustry:emetalforming-ingots") + ": " + entity.NeededInputCount);

        if (entity.SelectedRecipe != null && !entity.RecipeMatchesInput())
            stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressiveindustry:emetalforming-waiting-material"));

        if (entity.InputSlot?.Itemstack != null)
        {
            float currentTemp = entity.GetInputTemperature();
            stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Temperature") + ": " + (int)currentTemp + "°C / " + (int)BlockEntityEMetalForming.CraftStartTemp + "°C");

            if (entity.RecipeMatchesInput() && currentTemp < BlockEntityEMetalForming.CraftStartTemp)
            {
                stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:State") + ": " + Lang.Get("electricalprogressivebasics:Heating"));
            }
            else if (entity.EnergyOperation > 0 && entity.IsForging)
            {
                int percent = (int)(entity.RecipeProgress * 100);
                stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Progress") + ": " + percent + "%");
            }
        }

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
        {
            PowerSetting = 0;
            _fractionalEnergy = 0;
            _lastEnergyTime = 0;
            return;
        }

        if (PowerSetting != (int)amount)
            PowerSetting = (int)amount;

        if (IsWorking && amount > 0 && Blockentity is BlockEntityEMetalForming entity)
        {
            float currentTime = (float)(Api.World.ElapsedMilliseconds / 1000.0);

            if (_lastEnergyTime <= 0.01f)
            {
                _lastEnergyTime = currentTime;
                return;
            }

            float deltaTime = currentTime - _lastEnergyTime;
            _lastEnergyTime = currentTime;
            deltaTime = System.Math.Min(deltaTime, 0.1f);

            float energyThisTick = amount * deltaTime;
            _fractionalEnergy += energyThisTick;

            if (_fractionalEnergy >= 1.0f)
            {
                int wholeUnits = (int)_fractionalEnergy;
                _fractionalEnergy -= wholeUnits;
                entity.AddEnergy(wholeUnits);
            }
        }
    }

    public void Update()
    {
        if (Blockentity is not BlockEntityEMetalForming entity ||
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

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt(PowerSettingKey, PowerSetting);
        tree.SetFloat("fractionalEnergy", _fractionalEnergy);
        tree.SetFloat("lastEnergyTime", _lastEnergyTime);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        PowerSetting = tree.GetInt(PowerSettingKey);
        _fractionalEnergy = tree.GetFloat("fractionalEnergy");
        _lastEnergyTime = tree.GetFloat("lastEnergyTime");
    }

    public float getPowerReceive() => PowerSetting;

    public float getPowerRequest() => IsWorking ? _maxConsumption : 0;

    #endregion
}
