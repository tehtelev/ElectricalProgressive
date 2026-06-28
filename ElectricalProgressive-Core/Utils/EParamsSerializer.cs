using System;
using System.IO;
using System.Text;

namespace ElectricalProgressive.Utils
{
    public static class EParamsSerializer
    {
        /// <summary>
        /// Сериализует массив объектов EParams в байтовый массив.
        /// </summary>
        public static byte[] Serialize(EParams[] eparamsArray)
        {
            // Защита от null на входе
            if (eparamsArray == null) return Array.Empty<byte>();

            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                writer.Write(eparamsArray.Length);
                foreach (var eparam in eparamsArray)
                {
                    WriteEParams(writer, eparam);
                }
            }
            return ms.ToArray();
        }

        /// <summary>
        /// Десериализует байтовый массив обратно в массив объектов EParams.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static EParams[] Deserialize(byte[] data)
        {
            // Если данные отсутствуют, возвращаем null.
            // Это безопасно для системы ElectricalProgressive, так как она 
            // сама инициализирует массив из 6 граней при получении null.
            if (data == null) return null;

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms, Encoding.UTF8);

            var length = reader.ReadInt32();
            var eparamsArray = new EParams[length];
            for (var i = 0; i < length; i++)
            {
                eparamsArray[i] = ReadEParams(reader);
            }
            return eparamsArray;
        }

        /// <summary>
        /// Сериализует один объект EParams
        /// </summary>
        public static byte[] SerializeSingle(EParams eparam)
        {
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                WriteEParams(writer, eparam);
            }
            return ms.ToArray();
        }

        /// <summary>
        /// Десериализует один объект EParams
        /// </summary>
        public static EParams DeserializeSingle(byte[] data)
        {
            if (data == null) return new EParams(); // Для одиночных объектов безопаснее вернуть дефолтный инстанс
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms, Encoding.UTF8);
            return ReadEParams(reader);
        }
        private static void WriteEParams(BinaryWriter writer, EParams eparam)
        {
            writer.Write(eparam.voltage);            // int, 4 байта
            writer.Write(eparam.maxCurrent);         // float, 4 байта
            writer.Write(eparam.material ?? "");     // строка с префиксом длины
            writer.Write(eparam.resistivity);        // float, 4 байта
            writer.Write(eparam.lines);              // byte, 1 байт
            writer.Write(eparam.crossArea);          // float, 4 байта
            writer.Write(eparam.burnout);            // bool, 1 байт
            writer.Write(eparam.isolated);           // bool, 1 байт
            writer.Write(eparam.isolatedEnvironment);// bool, 1 байт
            writer.Write(eparam.causeBurnout);       // byte, 1 байт
            writer.Write(eparam.ticksBeforeBurnout); // int, 4 байта
            writer.Write(eparam.current);            // float, 4 байта
        }

        /// Читает один объект EParams из BinaryReader
        private static EParams ReadEParams(BinaryReader reader)
        {
            return new EParams
            {
                voltage = reader.ReadInt32(),
                maxCurrent = reader.ReadSingle(),
                material = reader.ReadString(),
                resistivity = reader.ReadSingle(),
                lines = reader.ReadByte(),
                crossArea = reader.ReadSingle(),
                burnout = reader.ReadBoolean(),
                isolated = reader.ReadBoolean(),
                isolatedEnvironment = reader.ReadBoolean(),
                causeBurnout = reader.ReadByte(),
                ticksBeforeBurnout = reader.ReadInt32(),
                current = reader.ReadSingle()
            };
        }
    }
}