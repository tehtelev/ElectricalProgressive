using System.Collections.Generic;
using System.IO;
using System.Text;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    public static class ConnectionDataSerializer
    {
        /// <summary>
        /// Сериализует список соединений в байтовый массив
        /// </summary>
        public static byte[] SerializeConnections(List<ConnectionData> connections, BlockPos currentPos)
        {
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                // Записываем количество соединений
                writer.Write(connections.Count);

                foreach (var conn in connections)
                {
                    // LocalNodeIndex - byte
                    writer.Write(conn.LocalNodeIndex);

                    // Сохраняем ОТНОСИТЕЛЬНЫЕ координаты соседа
                    writer.Write(conn.NeighborPos.X - currentPos.X);    // int
                    writer.Write(conn.NeighborPos.Y - currentPos.Y);    // int
                    writer.Write(conn.NeighborPos.Z - currentPos.Z);    // int

                    // NeighborNodeIndex - byte
                    writer.Write(conn.NeighborNodeIndex);

                    // NeighborNodeLocalPos
                    writer.Write(conn.NeighborNodeLocalPos.X);
                    writer.Write(conn.NeighborNodeLocalPos.Y);
                    writer.Write(conn.NeighborNodeLocalPos.Z);

                    // WireLength - float
                    writer.Write(conn.WireLength);

                    // Сериализуем параметры соединения
                    var paramsData = EParamsSerializer.SerializeSingle(conn.Parameters);
                    writer.Write(paramsData.Length);  // int - длина массива параметров
                    writer.Write(paramsData);         // byte[] - сами параметры
                }
            }
            return ms.ToArray();
        }

        /// <summary>
        /// Десериализует список соединений из байтового массива
        /// </summary>
        public static List<ConnectionData> DeserializeConnections(byte[] data, BlockPos currentPos)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms, Encoding.UTF8);

            // Читаем количество соединений
            var length = reader.ReadInt32();
            var connections = new List<ConnectionData>(length);

            for (var i = 0; i < length; i++)
            {
                var conn = new ConnectionData();

                // LocalNodeIndex - byte
                conn.LocalNodeIndex = reader.ReadByte();

                // Читаем ОТНОСИТЕЛЬНЫЕ координаты и преобразуем в абсолютные
                var relX = reader.ReadInt32();
                var relY = reader.ReadInt32();
                var relZ = reader.ReadInt32();

                conn.NeighborPos = new BlockPos(
                    currentPos.X + relX,
                    currentPos.Y + relY,
                    currentPos.Z + relZ,
                    currentPos.dimension  // Используем ту же размерность, что и у текущего блока
                );

                // NeighborNodeIndex - byte
                conn.NeighborNodeIndex = reader.ReadByte();

                
                var X = reader.ReadDouble();
                var Y = reader.ReadDouble();
                var Z = reader.ReadDouble();

                conn.NeighborNodeLocalPos = new Vec3d(X, Y, Z);
                

                // WireLength - float
                conn.WireLength = reader.ReadSingle();

                // Десериализуем параметры соединения
                var paramsDataLength = reader.ReadInt32();
                var paramsData = reader.ReadBytes(paramsDataLength);
                conn.Parameters = EParamsSerializer.DeserializeSingle(paramsData);

                connections.Add(conn);
            }

            return connections;
        }
    }
}