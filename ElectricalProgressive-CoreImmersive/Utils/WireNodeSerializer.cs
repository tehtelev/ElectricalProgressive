using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EPImmersive.Content.Block;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    public static class WireNodeSerializer
    {
        /// <summary>
        /// Сериализует список WireNode в байтовый массив
        /// </summary>
        public static byte[] SerializeWireNodes(List<WireNode> wireNodes)
        {
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                // Записываем версию формата (для обратной совместимости)
                writer.Write((byte)1);

                // Записываем количество узлов
                writer.Write(wireNodes.Count);

                foreach (var node in wireNodes)
                {
                    // Index - byte
                    writer.Write(node.Index);

                    // Voltage - int
                    writer.Write(node.Voltage);

                    // Position - 3 double
                    writer.Write(node.Position.X);
                    writer.Write(node.Position.Y);
                    writer.Write(node.Position.Z);

                    // Radius - float
                    writer.Write(node.Radius);
                }
            }
            return ms.ToArray();
        }

        /// <summary>
        /// Десериализует список WireNode из байтового массива
        /// </summary>
        public static List<WireNode> DeserializeWireNodes(byte[] data)
        {
            if (data == null || data.Length == 0)
                return [];

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms, Encoding.UTF8);

            // Читаем версию формата
            byte version = 0;
            try
            {
                version = reader.ReadByte();
            }
            catch
            {
                // Если не удалось прочитать версию, значит формат старый
                return [];
            }

            // Пока поддерживаем только версию 1
            if (version != 1)
                return [];

            // Читаем количество узлов
            var count = reader.ReadInt32();
            var wireNodes = new List<WireNode>(count);

            for (var i = 0; i < count; i++)
            {
                try
                {
                    var node = new WireNode
                    {
                        Index = reader.ReadByte(),
                        Voltage = reader.ReadInt32(),
                        Position = new Vec3d(
                            reader.ReadDouble(),
                            reader.ReadDouble(),
                            reader.ReadDouble()
                        ),
                        Radius = reader.ReadSingle()
                    };

                    wireNodes.Add(node);
                }
                catch
                {
                    // Пропускаем поврежденные узлы
                    // Можно добавить логгирование: Api.Logger?.Warning($"Failed to deserialize wire node: {ex.Message}");
                    continue;
                }
            }

            return wireNodes;
        }
    }
}