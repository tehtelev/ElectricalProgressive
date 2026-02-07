using EPImmersive.Content.Block;
using EPImmersive.Interface;
using EPImmersive.Utils;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Часть сети для иммерсивных проводов
    /// </summary>
    public class ImmersiveNetworkPart
    {
        public List<WireNode> WireNodes = [];
        public List<ConnectionData> Connections = [];
        public EParams MainEparams = new(); // Основные параметры устройства
        public ImmersiveNetwork Network;

        public readonly BlockPos Position;
        public IEImmersiveAccumulator? Accumulator;
        public IEImmersiveConsumer? Consumer;
        public IEImmersiveConductor? Conductor;
        public IEImmersiveProducer? Producer;
        public IEImmersiveTransformator? Transformator;
        public bool IsLoaded = false;
        public List<ImmersiveEnergyPacket> Packets = [];

        public ImmersiveNetworkPart(BlockPos position)
        {
            Position = position;
        }
    }

    /// <summary>
    /// Иммерсивное соединение в сети
    /// </summary>
    public class NetworkImmersiveConnection
    {
        public BlockPos LocalPos { get; set; }
        public byte LocalNodeIndex { get; set; }
        public BlockPos NeighborPos { get; set; }
        public byte NeighborNodeIndex { get; set; }
        public EParams Parameters { get; set; } = new EParams();

        public float WireLength { get; set; }
    }
}