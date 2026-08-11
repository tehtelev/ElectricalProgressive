using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EBlastFurnace;

public class BEBehaviorEBlastFurnace : BlockEntityBehavior, IElectricConsumer
{
    public enum FurnaceState
    {
        Idle,           // Не работает
        Heating,        // Нагревает предмет
        Smelting        // Плавит предмет
    }

    public int PowerSetting { get; set; }
    public const string PowerSettingKey = "electricalprogressive:powersetting";
    
    private float _accumulatedEnergy = 0f;
    private float _lastEnergyTime = 0f;
    private FurnaceState _currentState = FurnaceState.Idle;

    public bool IsBurned => this.Block.Code.GetName().Contains("burned");
    public float AvgConsumeCoeff { get; set; }

    private readonly int _maxConsumption;
    private float _recipeProgress;
    private bool hasBurnout;
    private bool prepareBurnout;

    public FurnaceState CurrentState 
    { 
        get => _currentState;
        set => _currentState = value;
    }

    public BEBehaviorEBlastFurnace(BlockEntity blockEntity) : base(blockEntity)
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 800);
    }

    public bool IsWorking => _currentState != FurnaceState.Idle;

    private float GetMeltingPoint(ItemStack stack)
    {
        if (stack?.Collectible == null) return 0f;
        
        var block = stack.Collectible as Vintagestory.API.Common.Block;
        if (block != null && block.Attributes != null && block.Attributes["combustibleProps"]?["meltingPoint"]?.Exists == true)
        {
            return block.Attributes["combustibleProps"]["meltingPoint"].AsFloat(0f);
        }
        
        var item = stack.Collectible as Vintagestory.API.Common.Item;
        if (item != null && item.Attributes != null && item.Attributes["combustibleProps"]?["meltingPoint"]?.Exists == true)
        {
            return item.Attributes["combustibleProps"]["meltingPoint"].AsFloat(0f);
        }
        
        return 0f;
    }

    public bool CanStartWorking
    {
        get
        {
            if (Blockentity is BlockEntityEBlastFurnace entity)
            {
                if (!entity.StructureComplete)
                    return false;

                if (entity.ElectricalProgressive == null &&
                    entity.ElectricalProgressive.AllEparams == null &&
                    entity.ElectricalProgressive.AllEparams.Any(e => e.burnout))
                    return false;

                var entityStack = entity.Inventory[0]?.Itemstack;
                if (entityStack is null || entityStack.StackSize == 0 || entityStack.Collectible == null)
                    return false;

                entityStack = entity.Inventory[1]?.Itemstack;
                if (entityStack is null || entityStack.StackSize == 0 || entityStack.Collectible == null)
                    return false;

                var hasRecipe = BlockEntityEBlastFurnace.FindMatchingRecipe(ref entity.CurrentRecipe, ref entity.CurrentRecipeName, entity.inventory);
                _recipeProgress = entity.RecipeProgress;
                
                return hasRecipe;
            }
            return false;
        }
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        if (this.Blockentity is not BlockEntityEBlastFurnace entity)
            return;

        if (IsBurned) return;

        if (!entity.StructureComplete)
        {
            stringBuilder.AppendLine(Lang.Get("electricalprogressivecore:construction-incomplete"));
            stringBuilder.AppendLine(Lang.Get("electricalprogressivecore:construction-hint"));
            stringBuilder.AppendLine();
            return;
        }

        stringBuilder.AppendLine(StringHelper.Progressbar(PowerSetting * 100.0f / _maxConsumption));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Consumption") + ": " + PowerSetting + "/" + _maxConsumption + " " + Lang.Get("electricalprogressivebasics:W"));

        // Отображаем текущее состояние
        switch (_currentState)
        {
            case FurnaceState.Heating:
                stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:State") + ": " + Lang.Get("electricalprogressivebasics:Heating"));
                if (entity.InputSlot1?.Itemstack != null)
                {
                    float currentTemp = entity.InputSlot1.Itemstack.Collectible.GetTemperature(Api.World, entity.InputSlot1.Itemstack);
                    float meltingPoint = GetMeltingPoint(entity.InputSlot1.Itemstack);
                    if (meltingPoint > 0)
                    {
                        int percent = (int)((currentTemp / meltingPoint) * 100);
                        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Temperature") + ": " + (int)currentTemp + "°C / " + (int)meltingPoint + "°C (" + percent + "%)");
                    }
                }
                break;
            case FurnaceState.Smelting:
                stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:State") + ": " + Lang.Get("electricalprogressivebasics:Smelting"));
                if (entity.CurrentRecipe != null && entity.CurrentRecipe.EnergyOperation > 0)
                {
                    int percent = (int)(entity.RecipeProgress * 100);
                    stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:SmeltingProgress") + ": " + percent + "%");
                }
                break;
        }

        stringBuilder.AppendLine();
    }

    #region IElectricConsumer

    public float Consume_request()
    {
        if (Blockentity is not BlockEntityEBlastFurnace entity)
            return PowerSetting = 0;

        if (!entity.StructureComplete)
            return PowerSetting = 0;
        
        var hasRecipe = BlockEntityEBlastFurnace.FindMatchingRecipe(ref entity.CurrentRecipe, ref entity.CurrentRecipeName, entity.inventory);
        
        if (hasRecipe && entity.CurrentRecipe != null)
            return _maxConsumption;
        
        return PowerSetting = 0;
    }

    public void Consume_receive(float amount)
    {
        if (Blockentity is not BlockEntityEBlastFurnace entity)
        {
            PowerSetting = 0;
            _accumulatedEnergy = 0;
            _lastEnergyTime = 0;
            _currentState = FurnaceState.Idle;
            return;
        }

        if (!entity.StructureComplete)
        {
            PowerSetting = 0;
            _accumulatedEnergy = 0;
            _lastEnergyTime = 0;
            _currentState = FurnaceState.Idle;
            return;
        }

        var hasRecipe = BlockEntityEBlastFurnace.FindMatchingRecipe(ref entity.CurrentRecipe, ref entity.CurrentRecipeName, entity.inventory);
        
        if (!hasRecipe || entity.CurrentRecipe == null)
        {
            PowerSetting = 0;
            _accumulatedEnergy = 0;
            _lastEnergyTime = 0;
            _currentState = FurnaceState.Idle;
            return;
        }

        if (PowerSetting != (int)amount)
            PowerSetting = (int)amount;
        
        if (amount > 0)
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
        if (Blockentity is not BlockEntityEBlastFurnace entity ||
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

            if (eParam.burnout) anyBurnout = true;
            if (eParam.ticksBeforeBurnout > 0) anyPrepareBurnout = true;
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
    public float getPowerRequest() => CanStartWorking ? _maxConsumption : (PowerSetting = 0);

    #endregion

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt(PowerSettingKey, PowerSetting);
        tree.SetFloat("recipeProgress", _recipeProgress);
        tree.SetFloat("accumulatedEnergy", _accumulatedEnergy);
        tree.SetFloat("lastEnergyTime", _lastEnergyTime);
        tree.SetInt("furnaceState", (int)_currentState);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        PowerSetting = tree.GetInt(PowerSettingKey);
        _recipeProgress = tree.GetFloat("recipeProgress");
        _accumulatedEnergy = tree.GetFloat("accumulatedEnergy");
        _lastEnergyTime = tree.GetFloat("lastEnergyTime");
        _currentState = (FurnaceState)tree.GetInt("furnaceState", 0);
    }
}