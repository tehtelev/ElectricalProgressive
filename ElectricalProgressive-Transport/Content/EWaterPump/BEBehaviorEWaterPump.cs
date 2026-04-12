using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.EWaterPump;

public class BEBehaviorEWaterPump : BlockEntityBehavior, IElectricConsumer
{
    // Настройки помпы
    public int PowerSetting { get; set; }  // Текущая установленная мощность в ваттах

    public const string PowerSettingKey = "electricalprogressive:powersetting";  // Ключ для сохранения настройки

    // Состояние помпы
    public bool IsBurned => this.Block.Code.GetName().Contains("burned");  // Помпа сгорела

    public float AvgConsumeCoeff { get; set; }  // Коэффициент потребления

    // Внутреннее состояние
    private readonly int _maxConsumption;  // Максимальное потребление помпы (из атрибута блока)
    private float _pumpProgress;  // Прогресс откачки воды (0-1)
    private BlockEntityEWaterPump _cachedEntity;  // Кэшированный сущность помпы для оптимизации

    public BEBehaviorEWaterPump(BlockEntity blockEntity) : base(blockEntity)
    {
        // Получаем максимальное потребление из атрибута блока (по умолчанию 150 Вт)
        _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 150);
    }

    public bool IsWorking
    {
        get
        {
            // Проверяем, является ли сущность помпой и находится ли она в режиме откачки воды
            if (this.Blockentity is BlockEntityEWaterPump entity)
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
            // Если помпа сгорела - не показываем информацию
            return;
        }

        // Показываем прогресс бар заполнения бака
        stringBuilder.AppendLine(StringHelper.Progressbar(PowerSetting * 100.0f / _maxConsumption));

        // Показываем текущее потребление и максимальное значение
        stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Consumption") + ": " +
            PowerSetting + "/" + _maxConsumption + " " + Lang.Get("electricalprogressivebasics:W"));

        stringBuilder.AppendLine();
    }

    // === МЕТОДЫ IElectricConsumer ===

    public float Consume_request()
    {
        // ВАЖНОЕ ИСПРАВЛЕНИЕ: Запрашиваем энергию на основе физических условий,
        // а не текущего получения энергии (устраняем замкнутый круг)

        if (_cachedEntity == null && this.Blockentity is BlockEntityEWaterPump entity)
            _cachedEntity = entity;

        if (_cachedEntity == null)
            return 0;

        // Проверяем физические условия работы (без проверки PowerSetting!)
        if (_cachedEntity.IsFull())
            return 0;

        if (!_cachedEntity.HasEnoughWaterInArea())
            return 0;

        // Если условия выполнены - запрашиваем максимальную мощность помпы
        return _maxConsumption;
    }

    public void Consume_receive(float amount)
    {
        if (_cachedEntity == null && this.Blockentity is BlockEntityEWaterPump entity)
            _cachedEntity = entity;

        if (_cachedEntity == null)
        {
            // Если помпа не найдена - сбрасываем мощность на 0
            PowerSetting = 0;
            return;
        }

        // Проверяем, можем ли мы использовать энергию (физические условия)
        bool canUsePower = !_cachedEntity.IsFull() && _cachedEntity.HasEnoughWaterInArea();

        if (!canUsePower)
            amount = 0;

        // Обновляем установленную мощность на основе полученной энергии
        if (PowerSetting != amount)
            PowerSetting = (int)amount;
    }

    public void Update()
    {
        // Обновление состояния помпы
    }

    public float getPowerReceive()
    {
        return this.PowerSetting;  // Возвращаем текущую установленную мощность
    }

    public float getPowerRequest()
    {
        // Алиас для Consume_request() для совместимости с интерфейсом IElectricConsumer
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