using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Interface;

public interface IElectricTransformator
{
    /// <summary>
    /// Координата трансформатора
    /// </summary>
    public BlockPos Pos { get; }

    /// <summary>
    /// Высокое напряжение трансформатора
    /// </summary>
    public int highVoltage { get; }

    /// <summary>
    /// Низкое напряжение трансформатора
    /// </summary>
    public int lowVoltage { get; }

    /// <summary>
    /// Сколько мощность сейчас на трансформаторе
    /// </summary>
    /// <returns></returns>
    public float getPower();

    /// <summary>
    /// Обновляем трансформатору мощность его
    /// </summary>
    /// <returns></returns>
    public void setPower(float power);

    /// <summary>
    /// Обновляем Entity
    /// </summary>
    public void Update();
}
