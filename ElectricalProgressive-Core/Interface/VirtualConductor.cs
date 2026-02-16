using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Interface;

public class VirtualConductor : IElectricConductor
{
    public BlockPos Pos { get; }

    public VirtualConductor(BlockPos pos)
    {
        this.Pos = pos;
    }

    public void Update() { }
}