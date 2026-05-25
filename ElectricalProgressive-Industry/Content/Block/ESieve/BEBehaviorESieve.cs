// BEBehaviorESieve.cs
using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ESieve;

public class BEBehaviorESieve : BlockEntityBehavior, IElectricConsumer
{
    public int PowerSetting { get; set; }
    public const string PowerSettingKey = "electricalprogressive:powersetting";
    
    private float _accumulatedEnergy = 0f;
    private float _lastEnergyTime = 0f;

    public bool IsBurned => this.Block.Code.GetName().Contains("burned");
    public float AvgConsumeCoeff { get; set; }

    private readonly int _maxConsumption;
    private float _recipeProgress;
    private bool hasBurnout;
    private bool prepareBurnout;

    public BEBehaviorESieve(BlockEntity blockEntity) : base(blockEntity)
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
    }

    public bool IsWorking
    {
        get
        {
            if (Blockentity is BlockEntityESieve entity)
            {
                if (entity.ElectricalProgressive == null ||
                    entity.ElectricalProgressive.AllEparams == null ||
                    entity.ElectricalProgressive.AllEparams.Any(e => e.burnout))
                    return false;

                var entityStack = entity.Inventory[0]?.Itemstack;

                if (entityStack is null ||
                    entityStack.StackSize == 0 ||
                    entityStack.Collectible == null)
                    return false;

                var hasRecipe = BlockEntityESieve.FindMatchingRecipe(ref entity.CurrentRecipe, ref entity.CurrentRecipeName, entity.inventory);
                _recipeProgress = entity.RecipeProgress;
                return hasRecipe;
            }
            return false;
        }
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        if (this.Blockentity is not BlockEntityESieve entity || IsBurned)
            return;

        stringBuilder.AppendLine(StringHelper.Progressbar(PowerSetting * 100.0f / _maxConsumption));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Consumption") + ": " + PowerSetting + "/" + _maxConsumption + " " + Lang.Get("electricalprogressivebasics:W"));

        if (entity.CurrentRecipe != null && entity.CurrentRecipe.EnergyOperation > 0)
        {
            int percent = (int)(entity.RecipeProgress * 100);
            stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:SievingProgress") + ": " + percent + "%");
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
            _accumulatedEnergy = 0;
            _lastEnergyTime = 0;
            return;
        }

        if (PowerSetting != (int)amount)
            PowerSetting = (int)amount;
        
        if (IsWorking && amount > 0 && Blockentity is BlockEntityESieve entity)
        {
            float currentTime = (float)(Api.World.ElapsedMilliseconds / 1000.0);
            
            if (_lastEnergyTime <= 0.01f)
            {
                _lastEnergyTime = currentTime;
                return;
            }
            
            float deltaTime = currentTime - _lastEnergyTime;
            _lastEnergyTime = currentTime;
            
            if (deltaTime > 0.1f)
                deltaTime = 0.1f;
            
            float energyThisTick = amount * deltaTime;
            _accumulatedEnergy += energyThisTick;
            
            if (_accumulatedEnergy >= 1.0f)
            {
                int wholeUnits = (int)_accumulatedEnergy;
                _accumulatedEnergy -= wholeUnits;
                entity.AddEnergy(wholeUnits);
            }
        }
    }

    public void Update()
    {
        if (Blockentity is not BlockEntityESieve entity ||
            entity.ElectricalProgressive == null ||
            entity.ElectricalProgressive.AllEparams is null)
            return;

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

    public float getPowerReceive() => this.PowerSetting;

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