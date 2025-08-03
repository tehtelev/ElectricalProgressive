using Vintagestory.API.MathTools;
using ElectricalProgressive.Interface;

namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Часть сети
    /// </summary>
    public class NetworkPart
    {
        public readonly Network?[] Networks = new Network?[6];
        public EParams[] eparams = new EParams[] { };
        public readonly BlockPos Position;
        public Facing Connection = Facing.None;
        public IElectricAccumulator? Accumulator;
        public IElectricConsumer? Consumer;
        public IElectricConductor? Conductor;
        public IElectricProducer? Producer;
        public IElectricTransformator? Transformator;
        public bool IsLoaded = false;

        public NetworkPart(BlockPos position)
        {
            Position = position;
        }
    }
}