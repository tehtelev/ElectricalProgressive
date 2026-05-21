﻿using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EFruitPress;

public class BEBehaviorEFruitPress : BlockEntityBehavior, IElectricConsumer
{
    public int PowerSetting { get; set; }
    public const string PowerSettingKey = "electricalprogressive:powersetting";
    
    /// <summary>
    /// Накопленная энергия (дробная часть)
    /// </summary>
    private float _accumulatedEnergy = 0f;
    
    public bool IsBurned => this.Block.Code.GetName().Contains("burned");
    public float AvgConsumeCoeff { get; set; }
    
    private readonly int _maxConsumption;
    private float _recipeProgress;

    public BEBehaviorEFruitPress(BlockEntity blockEntity) : base(blockEntity)
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 150);
    }

    public bool IsWorking
    {
        get
        {
            if (Blockentity is BlockEntityEFruitPress entity)
            {
                if (entity.ElectricalProgressive == null ||
                    entity.ElectricalProgressive.AllEparams == null ||
                    entity.ElectricalProgressive.AllEparams.Any(e => e.burnout))
                    return false;

                var fruitStack = entity.FruitSlot?.Itemstack;
                
                if (fruitStack is null || fruitStack.StackSize == 0 ||
                    fruitStack.Collectible == null ||
                    fruitStack.Collectible.Attributes == null)
                    return false;

                // Проверяем, можно ли выжать сок из этого предмета
                var juiceableProps = fruitStack.ItemAttributes?["juiceableProperties"];
                if (juiceableProps == null || !juiceableProps.Exists) return false;
                
                // Проверяем, есть ли еще сок для отжима
                double juiceableLitresLeft = GetJuiceableLitresLeft(fruitStack, entity);
                
                if (juiceableLitresLeft <= 0.01) return false;
                
                // Проверяем, не полон ли бак
                if (entity.IsFull()) return false;
                
                // Проверяем совместимость жидкости
                if (!entity.IsJuiceCompatible()) return false;
                
                _recipeProgress = entity.SqueezeProgress;
                return true;
            }
            return false;
        }
    }

    private double GetJuiceableLitresLeft(ItemStack fruitStack, BlockEntityEFruitPress entity)
    {
        var juiceableProps = fruitStack.ItemAttributes?["juiceableProperties"];
        if (juiceableProps == null || !juiceableProps.Exists) return 0;
        
        var props = juiceableProps.AsObject<BlockEntityEFruitPress.JuiceableProperties>(null, fruitStack.Collectible.Code.Domain);
        
        if (props?.LiquidStack != null)
        {
            props.LiquidStack.Resolve(Api.World, "juiceable properties liquidstack", fruitStack.Collectible.Code);
        }
        
        if (props?.LitresPerItem.HasValue == true)
        {
            return fruitStack.StackSize * props.LitresPerItem.Value;
        }
        
        return fruitStack.Attributes?.GetDouble("juiceableLitresLeft", 0) ?? 0;
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        if (this.Blockentity is not BlockEntityEFruitPress)
            return;

        if (IsBurned)
        {
            return;
        }

        stringBuilder.AppendLine(StringHelper.Progressbar(PowerSetting * 100.0f / _maxConsumption));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Consumption") + ": " + PowerSetting + "/" + _maxConsumption + " " + Lang.Get("electricalprogressivebasics:W"));

        stringBuilder.AppendLine();
    }

    #region IElectricConsumer

    public float Consume_request()
    {
        if (IsWorking)
            return _maxConsumption;

        return 0;
    }

    public void Consume_receive(float amount)
    {
        if (!IsWorking)
        {
            PowerSetting = 0;
            _accumulatedEnergy = 0;
            amount = 0;
        }

        if (PowerSetting != (int)amount)
            PowerSetting = (int)amount;
        
        // Накопление энергии
        if (IsWorking && amount > 0 && Blockentity is BlockEntityEFruitPress entity)
        {
            // Добавляем полученную энергию к накопленной
            _accumulatedEnergy += amount;
            
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
        // Обновление состояния
    }

    public float getPowerReceive()
    {
        return this.PowerSetting;
    }

    public float getPowerRequest()
    {
        if (IsWorking)
            return _maxConsumption;

        return 0;
    }

    #endregion

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt(PowerSettingKey, PowerSetting);
        tree.SetFloat("recipeProgress", _recipeProgress);
        tree.SetFloat("accumulatedEnergy", _accumulatedEnergy);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        PowerSetting = tree.GetInt(PowerSettingKey);
        _recipeProgress = tree.GetFloat("recipeProgress");
        _accumulatedEnergy = tree.GetFloat("accumulatedEnergy");
    }
}