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
                var status = entity.GetCondensationStatus();
                return status == BlockEntityEAquaAccum.CondensationStatus.Condensing;
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

        // Конденсатор работает всегда, если бак не полон
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
            return;
        }

        // Только проверка на заполненность бака
        bool canUsePower = !_cachedEntity.IsFull();

        if (!canUsePower)
            amount = 0;

        if (PowerSetting != amount)
            PowerSetting = (int)amount;
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
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        PowerSetting = tree.GetInt(PowerSettingKey);
    }
}