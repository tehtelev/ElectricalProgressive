using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Содержит информацию о конкретном соединении между двумя точками подключения
    /// </summary>
    public class ConnectionData
    {
        public byte LocalNodeIndex { get; set; }      // Индекс точки подключения на ТЕКУЩЕМ устройстве
        public BlockPos NeighborPos { get; set; }     // Позиция СОСЕДНЕГО устройства
        public byte NeighborNodeIndex { get; set; }   // Индекс точки подключения на СОСЕДНЕМ устройстве

        public EParams Parameters;                    // Параметры этого конкретного соединения

        public float WireLength;

        public Vec3d NeighborNodeLocalPos { get; set; } // хранение локальной позиции соседнего нода
    }
}
