﻿using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EPress;

public class BEBehaviorEPress : BlockEntityBehavior, IElectricConsumer
{
    /// <summary>
    /// Текущее потребление
    /// </summary>
    public int PowerSetting { get; set; }

    public const string PowerSettingKey = "electricalprogressive:powersetting";
    
    /// <summary>
    /// Накопленная энергия (дробная часть)
    /// </summary>
    private float _accumulatedEnergy = 0f;
    
    /// <summary>
    /// Время последнего получения энергии (для расчёта dt)
    /// </summary>
    private float _lastEnergyTime = 0f;

    public bool IsBurned => this.Block.Code.GetName().Contains("burned");

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

    public BEBehaviorEPress(BlockEntity blockEntity) : base(blockEntity)
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 100);
    }

    public bool IsWorking
    {
        get
        {
            if (Blockentity is BlockEntityEPress entity)
            {
                if (!entity.StructureComplete)
                    return false;

                // прибор сгорел?
                if (entity.ElectricalProgressive == null &&
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

                var hasRecipe = BlockEntityEPress.FindMatchingRecipe(ref entity.CurrentRecipe, ref entity.CurrentRecipeName, entity.inventory);
                _recipeProgress = entity.RecipeProgress;
                return hasRecipe;
            }
            return false;
        }
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        if (this.Blockentity is not BlockEntityEPress entity)
            return;

        if (IsBurned)
        {
            return;
        }

        if (!entity.StructureComplete)
        {
            stringBuilder.AppendLine(Lang.Get("electricalprogressivecore:construction-incomplete"));
            stringBuilder.AppendLine(Lang.Get("electricalprogressivecore:construction-hint"));
            stringBuilder.AppendLine();
            return;
        }

        stringBuilder.AppendLine(StringHelper.Progressbar(PowerSetting * 100.0f / _maxConsumption));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Consumption") + ": " + PowerSetting + "/" + _maxConsumption + " " + Lang.Get("electricalprogressivebasics:W"));

        if (entity.FindIngotSlot()?.Itemstack != null)
        {
            float currentTemp = entity.GetInputTemperature();
            stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Temperature") + ": " + (int)currentTemp + "°C / " + (int)BlockEntityEPress.CraftStartTemp + "°C");

            if (currentTemp < BlockEntityEPress.CraftStartTemp)
            {
                stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:State") + ": " + Lang.Get("electricalprogressivebasics:Heating"));
            }
            else if (entity.CurrentRecipe != null && entity.CurrentRecipe.EnergyOperation > 0)
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
            _accumulatedEnergy = 0;
            _lastEnergyTime = 0;
            return;
        }

        if (PowerSetting != (int)amount)
            PowerSetting = (int)amount;
        
        // Накопление энергии с учётом реального времени
        if (IsWorking && amount > 0 && Blockentity is BlockEntityEPress entity)
        {
            // Получаем текущее время в секундах
            float currentTime = (float)(Api.World.ElapsedMilliseconds / 1000.0);
            
            // Инициализация времени при первом вызове
            if (_lastEnergyTime <= 0.01f)
            {
                _lastEnergyTime = currentTime;
                return;
            }
            
            // Вычисляем прошедшее время
            float deltaTime = currentTime - _lastEnergyTime;
            _lastEnergyTime = currentTime;
            
            // Ограничиваем максимальный dt (защита от больших скачков)
            if (deltaTime > 0.1f)
                deltaTime = 0.1f;
            
            // Энергия за этот период (Вт * секунды)
            float energyThisTick = amount * deltaTime;
            
            // Добавляем к накопленной энергии
            _accumulatedEnergy += energyThisTick;
            
            // Если накопилось целое число или больше
            if (_accumulatedEnergy >= 1.0f)
            {
                int wholeUnits = (int)_accumulatedEnergy;
                _accumulatedEnergy -= wholeUnits;
                
                // Передаем целые единицы в рецепт
                entity.AddEnergy(wholeUnits);
            }
        }
    }

    public void Update()
    {
        if (Blockentity is not BlockEntityEPress entity ||
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