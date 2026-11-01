using Vintagestory.API.MathTools;
using ElectricalProgressive.Interface;
using System.Collections.Generic;

namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Часть сети
    /// </summary>
    public class NetworkPart
    {
        public readonly Network[] Networks = new Network[6];
        public EParams[] eparams = [];
        public readonly BlockPos Position;
        public Facing Connection = Facing.None;
        public IElectricAccumulator? Accumulator;
        public IElectricConsumer? Consumer;
        public IElectricConductor? Conductor;
        public IElectricProducer? Producer;
        public IElectricTransformator? Transformator;
        public bool IsLoaded = false;
        public List<EnergyPacket> packets= [];

        public NetworkPart(BlockPos position)
        {
            Position = position;
        }
    }
}