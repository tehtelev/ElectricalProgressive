using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Interface;

public interface IElectricProducer
{
    /// <summary>
    /// Координата аккумулятора
    /// </summary>
    public BlockPos Pos { get; }

    /// <summary>
    /// Система запрашивает у генератора сколько ей нужно в данный момент выдать
    /// </summary>
    public void Produce_order(float amount);

    /// <summary>
    /// Сколько может выдать генератор сейчас максимум
    /// </summary>
    /// <returns></returns>
    public float getPowerGive();

    /// <summary>
    /// Сколько в данный момент просят с генератора (нагрузка)
    /// </summary>
    /// <returns></returns>
    public float getPowerOrder();

    /// <summary>
    /// Генератор выдает энергию в систему
    /// </summary>
    public float Produce_give();

    /// <summary>
    /// Обновляем Entity
    /// </summary>
    public void Update();
}
