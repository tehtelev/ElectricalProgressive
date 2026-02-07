using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EWaterPump;

public class BEBehaviorEWaterPump : BlockEntityBehavior, IElectricConsumer
{
    public int PowerSetting { get; set; }
    public const string PowerSettingKey = "electricalprogressive:powersetting";
    public bool IsBurned => this.Block.Code.GetName().Contains("burned");
    public float AvgConsumeCoeff { get; set; }
    
    private readonly int _maxConsumption;
    private float _pumpProgress;
    private BlockEntityEWaterPump _cachedEntity;

    public BEBehaviorEWaterPump(BlockEntity blockEntity) : base(blockEntity)
    {
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 150);
    }

    public bool IsWorking
    {
        get
        {
            if (Blockentity is BlockEntityEWaterPump entity)
            {
                var status = entity.GetPumpStatus();
                return status == BlockEntityEWaterPump.PumpStatus.Pumping;
            }
            return false;
        }
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        if (this.Blockentity is not BlockEntityEWaterPump)
            return;

        if (IsBurned)
        {
            return;
        }

        stringBuilder.AppendLine(StringHelper.Progressbar(PowerSetting * 100.0f / _maxConsumption));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Consumption") + ": " + 
            PowerSetting + "/" + _maxConsumption + " " + Lang.Get("electricalprogressivebasics:W"));

        stringBuilder.AppendLine();
    }

    #region IElectricConsumer

    public float Consume_request()
    {
        // ВАЖНОЕ ИСПРАВЛЕНИЕ: Запрашиваем энергию на основе физических условий,
        // а не текущего получения энергии (устраняем замкнутый круг)
        
        if (_cachedEntity == null && Blockentity is BlockEntityEWaterPump entity)
            _cachedEntity = entity;
        
        if (_cachedEntity == null)
            return 0;

        // Проверяем физические условия работы (без проверки PowerSetting!)
        if (_cachedEntity.IsFull())
            return 0;
        
        if (!_cachedEntity.HasEnoughWaterInArea())
            return 0;
        
        // Если условия выполнены - запрашиваем максимальную мощность
        return _maxConsumption;
    }

    public void Consume_receive(float amount)
    {
        if (_cachedEntity == null && Blockentity is BlockEntityEWaterPump entity)
            _cachedEntity = entity;
        
        if (_cachedEntity == null)
        {
            PowerSetting = 0;
            return;
        }
        
        // Проверяем, можем ли мы использовать энергию (физические условия)
        bool canUsePower = !_cachedEntity.IsFull() && _cachedEntity.HasEnoughWaterInArea();
        
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
        // Алиас для Consume_request() для совместимости
        return Consume_request();
    }

    #endregion

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt(PowerSettingKey, PowerSetting);
        tree.SetFloat("pumpProgress", _pumpProgress);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        PowerSetting = tree.GetInt(PowerSettingKey);
        _pumpProgress = tree.GetFloat("pumpProgress");
        
        // Сбрасываем кэш при загрузке
        _cachedEntity = null;
    }
}