using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Interface;

public interface IElectricConductor
{
    /// <summary>
    /// Координата проводника
    /// </summary>
    public BlockPos Pos { get; }

   

    /// <summary>
    /// Обновляем Entity
    /// </summary>
    public void Update();
}
