using System.IO;
using System.Text;

namespace ElectricalProgressive.Utils
{
    public static class EParamsSerializer
    {
        public static byte[] Serialize(EParams[] eparamsArray)
        {
            using (var ms = new MemoryStream())
            {
                using (var writer = new BinaryWriter(ms, Encoding.UTF8))
                {
                    // Записываем длину массива
                    writer.Write(eparamsArray.Length);
                    foreach (var eparam in eparamsArray)
                    {
                        writer.Write(eparam.voltage);            // int, 4 байта
                        writer.Write(eparam.maxCurrent);         // float, 4 байта
                        writer.Write(eparam.material);           // строка с префиксом длины
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
                }
                return ms.ToArray();
            }
        }

        public static EParams[] Deserialize(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            {
                using (var reader = new BinaryReader(ms, Encoding.UTF8))
                {
                    // Читаем длину массива
                    int length = reader.ReadInt32();
                    EParams[] eparamsArray = new EParams[length];
                    for (int i = 0; i < length; i++)
                    {
                        eparamsArray[i] = new EParams
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
                    return eparamsArray;
                }
            }
        }
    }
}