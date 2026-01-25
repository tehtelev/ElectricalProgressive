using ElectricalProgressive.Interface;
using Vintagestory.API.MathTools;

namespace EPImmersive.Interface;

public interface IEImmersiveConductor : IElectricConductor
{
    /// <summary>
    /// Замкнут ли ключ
    /// </summary>
    bool IsOpen { get; set; }
}
