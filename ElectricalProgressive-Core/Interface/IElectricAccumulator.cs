using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Interface;

public interface IElectricAccumulator
{
    /// <summary>
    /// Координата аккумулятора
    /// </summary>
    public BlockPos Pos { get; }

    /// <summary>
    /// Максимальный ток отдачи/сохранения
    /// </summary>
    public float power { get; }

    /// <summary>
    /// Максимальная емкость аккумулятора
    /// </summary>
    /// <returns></returns>
    public float GetMaxCapacity();

    /// <summary>
    /// Текущая емкость аккумулятора
    /// </summary>
    /// <returns></returns>
    public float GetCapacity();

    /// <summary>
    /// Предыдущее значение емкости аккумулятора
    /// </summary>
    /// <returns></returns>
    public float GetLastCapacity();

    /// <summary>
    /// Задает сразу емкость аккумулятору и множитель емкости максимальной (вызывать только при установке аккумулятора)
    /// </summary>
    /// <returns></returns>
    public void SetCapacity(float value, float multDurab = 1.0F);

    /// <summary>
    /// Сохранить энергию
    /// </summary>
    /// <param name="amount"></param>
    public void Store(float amount);

    /// <summary>
    /// Может сохранить энергии за раз
    /// </summary>
    /// <param name="amount"></param>
    public float canStore();

    /// <summary>
    /// Выдать энергию
    /// </summary>
    /// <param name="amount"></param>
    public float Release(float amount);

    /// <summary>
    /// Может выдать энергии за раз
    /// </summary>
    /// <param name="amount"></param>
    public float canRelease();

    /// <summary>
    /// Обновляем Entity
    /// </summary>
    public void Update();
}