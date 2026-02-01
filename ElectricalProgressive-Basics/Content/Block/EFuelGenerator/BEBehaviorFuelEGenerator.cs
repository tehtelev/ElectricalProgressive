using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EFuelGenerator;

/// <summary>
/// Поведение электрического генератора на топливе для производства электроэнергии.
/// Реализует интерфейс IElectricProducer для интеграции в электрическую систему.
/// </summary>
public class BEBehaviorFuelEGenerator : BlockEntityBehavior, IElectricProducer
{
    // === Поля состояния ===
    private float _powerOrder;        // Запрошенная мощность
    private float _powerGive;         // Производимая мощность
    private bool hasBurnout;          // Флаг перегрева
    private bool prepareBurnout;      // Флаг подготовки к перегреву
    
    // === Константы для сохранения состояния ===
    public const string PowerOrderKey = "electricalprogressive:powerOrder";
    public const string PowerGiveKey = "electricalprogressive:powerGive";
    
    // === Свойства ===
    
    /// <summary>
    /// Позиция блока в мире
    /// </summary>
    public new BlockPos Pos => Blockentity.Pos;

    // === Конструктор ===
    
    public BEBehaviorFuelEGenerator(BlockEntity blockEntity) : base(blockEntity) { }

    // === Основные методы ===
    
    /// <summary>
    /// Обновление состояния генератора (вызывается каждый тик)
    /// Обрабатывает состояния перегрева и управляет визуальными эффектами
    /// </summary>
    public void Update()
    {
        if (Blockentity is not BlockEntityEFuelGenerator entity ||
            entity.ElectricalProgressive == null ||
            entity.ElectricalProgressive.AllEparams is null)
        {
            return;
        }

        // Проверка состояния перегрева
        bool anyBurnout = false;
        bool anyPrepareBurnout = false;

        // Анализ всех электрических параметров
        foreach (var eParam in entity.ElectricalProgressive.AllEparams)
        {
            // Обновление состояний перегрева
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

            // Проверка наличия перегрева в любом параметре
            if (eParam.burnout) anyBurnout = true;
            if (eParam.ticksBeforeBurnout > 0) anyPrepareBurnout = true;
        }

        // Сброс состояний перегрева если нет ни в одном параметре
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

        // Управление частицами в зависимости от температуры
        if (!hasBurnout)
        {
            entity.ElectricalProgressive.ParticlesType = entity.GenTemp > 200 ? 3 : 0;
        }
        else
        {
            entity.ElectricalProgressive.ParticlesType = 0;
        }
    }

    // === Реализация интерфейса IElectricProducer ===
    
    /// <summary>
    /// Получить текущую производимую мощность
    /// </summary>
    /// <returns>Текущая мощность в ваттах</returns>
    public float Produce_give()
    {
        if (Blockentity is not BlockEntityEFuelGenerator temp)
            return 0f;

        // Расчет мощности в зависимости от температуры и наличия воды
        _powerGive = (temp.GenTemp > 200 && !temp.WaterSlot.Empty) ? temp.Power : 1f;
        
        return _powerGive;
    }

    /// <summary>
    /// Установить запрошенную мощность
    /// </summary>
    /// <param name="amount">Количество запрашиваемой мощности</param>
    public void Produce_order(float amount)
    {
        _powerOrder = amount;
    }

    /// <summary>
    /// Получить текущую производимую мощность
    /// </summary>
    public float getPowerGive() => _powerGive;

    /// <summary>
    /// Получить запрошенную мощность
    /// </summary>
    public float getPowerOrder() => _powerOrder;

    // === Методы BlockEntityBehavior ===
    
    /// <summary>
    /// Получить информацию о блоке для отображения в интерфейсе
    /// </summary>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        if (Blockentity is not BlockEntityEFuelGenerator entity)
            return;

        // Отображение прогресс-бара производства
        stringBuilder.AppendLine(StringHelper.Progressbar(Math.Min(_powerGive, _powerOrder) / Math.Max(1f, _powerGive) * 100));
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Production") + ": " + ((int)Math.Min(_powerGive, _powerOrder)) + "/" + Math.Max(1f, _powerGive) + " " + Lang.Get("electricalprogressivebasics:W"));
        
        // Отображение информации о воде
        if (!entity.WaterSlot.Empty)
            stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Water") + ": " + entity.WaterAmount.ToString("0.0") + "/" + entity.WaterCapacity + " L");
        else
            stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:No water") + " - " + Lang.Get("electricalprogressivebasics:Reduced power"));
    }

    /// <summary>
    /// Сохранение состояния в дерево атрибутов
    /// </summary>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat(PowerOrderKey, _powerOrder);
        tree.SetFloat(PowerGiveKey, _powerGive);
    }

    /// <summary>
    /// Загрузка состояния из дерева атрибутов
    /// </summary>
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        _powerOrder = tree.GetFloat(PowerOrderKey);
        _powerGive = tree.GetFloat(PowerGiveKey);
    }
}