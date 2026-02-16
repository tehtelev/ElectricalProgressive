using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Interface;

public interface IEImmersiveConductor
{
    /// <summary>
    /// Координата проводника
    /// </summary>
    public BlockPos Pos { get; }



    /// <summary>
    /// Обновляем Entity
    /// </summary>
    public void Update();
    /// <summary>
    /// Замкнут ли ключ
    /// </summary>
    bool IsOpen { get; set; }
}
