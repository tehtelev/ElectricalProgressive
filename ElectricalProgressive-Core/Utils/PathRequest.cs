using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Класс для представления запроса на поиск пути
    /// </summary>
    public class PathRequest
    {
        public BlockPos Start { get; }
        public BlockPos End { get; }
        public Network Network { get; }

        public PathRequest(BlockPos start, BlockPos end, Network network)
        {
            Start = start;
            End = end;
            Network = network;
        }
    }


    public class PathRequestImmersive
    {
        public BlockPos Start { get; }
        public BlockPos End { get; }
        public ImmersiveNetwork ImmersiveNetwork { get; }

        public PathRequestImmersive(BlockPos start, BlockPos end, ImmersiveNetwork immersiveNetwork)
        {
            Start = start;
            End = end;
            ImmersiveNetwork = immersiveNetwork;
        }
    }
}
