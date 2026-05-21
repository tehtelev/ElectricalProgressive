﻿using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.EAquaAccum;

public class BEBehaviorEAquaAccum : BlockEntityBehavior, IElectricConsumer
{
    // Настройки
    public int PowerSetting { get; set; }
    public const string PowerSettingKey = "electricalprogressive:powersetting";

    // Состояние
    public bool IsBurned => this.Block.Code.GetName().Contains("burned");
    public float AvgConsumeCoeff { get; set; }

    // Внутреннее состояние
    private readonly int _maxConsumption;
    private BlockEntityEAquaAccum _cachedEntity;
    
    /// <summary>
    /// Накопленная энергия для производства воды
    /// </summary>
    private float _accumulatedEnergy = 0f;
    
    /// <summary>
    /// Время последнего получения энергии (для расчёта dt)
    /// </summary>
    private float _lastEnergyTime = 0f;

    public BEBehaviorEAquaAccum(BlockEntity blockEntity) : base(blockEntity)
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 150);
    }

    public bool IsWorking
    {
        get
        {
            if (this.Blockentity is BlockEntityEAquaAccum entity)
            {
                // Работаем если бак не полон
                return !entity.IsFull();
            }
            return false;
        }
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        if (this.Blockentity is not BlockEntityEAquaAccum)
            return;

        if (IsBurned)
            return;

        stringBuilder.AppendLine(StringHelper.Progressbar(PowerSetting * 100.0f / _maxConsumption));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Consumption") + ": " +
            PowerSetting + "/" + _maxConsumption + " " + Lang.Get("electricalprogressivebasics:W"));

        stringBuilder.AppendLine();
    }

    // === МЕТОДЫ IElectricConsumer ===

    public float Consume_request()
    {
        if (_cachedEntity == null && this.Blockentity is BlockEntityEAquaAccum entity)
            _cachedEntity = entity;

        if (_cachedEntity == null)
            return 0;

        // Потребляем энергию только если бак не полон
        if (_cachedEntity.IsFull())
            return 0;

        return _maxConsumption;
    }

    public void Consume_receive(float amount)
    {
        if (_cachedEntity == null && this.Blockentity is BlockEntityEAquaAccum entity)
            _cachedEntity = entity;

        if (_cachedEntity == null)
        {
            PowerSetting = 0;
            _accumulatedEnergy = 0;
            _lastEnergyTime = 0;
            return;
        }

        // Если бак полон - не потребляем энергию
        if (_cachedEntity.IsFull())
        {
            PowerSetting = 0;
            _accumulatedEnergy = 0;
            _lastEnergyTime = 0;
            return;
        }

        // Устанавливаем текущее потребление
        if (PowerSetting != (int)amount)
            PowerSetting = (int)amount;
        
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
        
        // Ограничиваем максимальный dt
        if (deltaTime > 0.1f)
            deltaTime = 0.1f;
        
        // Энергия за этот период (Вт * секунды)
        float energyThisTick = amount * deltaTime;
        
        // Добавляем к накопленной энергии
        _accumulatedEnergy += energyThisTick;
        
        // Если накопилась энергия - передаём в блок для производства воды
        if (_accumulatedEnergy >= 0.01f)
        {
            _cachedEntity.AddEnergy(_accumulatedEnergy);
            _accumulatedEnergy = 0;
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
        return Consume_request();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt(PowerSettingKey, PowerSetting);
        tree.SetFloat("accumulatedEnergy", _accumulatedEnergy);
        tree.SetFloat("lastEnergyTime", _lastEnergyTime);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        PowerSetting = tree.GetInt(PowerSettingKey);
        _accumulatedEnergy = tree.GetFloat("accumulatedEnergy");
        _lastEnergyTime = tree.GetFloat("lastEnergyTime");
    }
}