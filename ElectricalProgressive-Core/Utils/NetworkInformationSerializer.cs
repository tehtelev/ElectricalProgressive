using System.IO;
using System.Text;

namespace ElectricalProgressive.Utils
{
    public static class NetworkInformationSerializer
    {
        public static byte[] Serialize(NetworkInformation info)
        {
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                writer.Write(info.Consumption);
                writer.Write(info.Capacity);
                writer.Write(info.MaxCapacity);
                writer.Write(info.Production);
                writer.Write(info.Request);
                writer.Write((int)info.Facing);
                writer.Write(info.NumberOfAccumulators);
                writer.Write(info.NumberOfBlocks);
                writer.Write(info.NumberOfConsumers);
                writer.Write(info.NumberOfProducers);
                writer.Write(info.NumberOfTransformators);

                var eparam = info.eParamsInNetwork;
                writer.Write(eparam.voltage);
                writer.Write(eparam.maxCurrent);
                writer.Write(eparam.material);
                writer.Write(eparam.resistivity);
                writer.Write(eparam.lines);
                writer.Write(eparam.crossArea);
                writer.Write(eparam.burnout);
                writer.Write(eparam.isolated);
                writer.Write(eparam.isolatedEnvironment);
                writer.Write(eparam.causeBurnout);
                writer.Write(eparam.ticksBeforeBurnout);
                writer.Write(eparam.current);

                writer.Write(info.current);
            }
            return ms.ToArray();
        }

        public static NetworkInformation Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms, Encoding.UTF8);
            var info = new NetworkInformation
            {
                Consumption = reader.ReadSingle(), Capacity = reader.ReadSingle(), MaxCapacity = reader.ReadSingle(),
                Production = reader.ReadSingle(),
                Request = reader.ReadSingle(),
                Facing = (Facing)reader.ReadInt32(),
                NumberOfAccumulators = reader.ReadInt32(),
                NumberOfBlocks = reader.ReadInt32(),
                NumberOfConsumers = reader.ReadInt32(),
                NumberOfProducers = reader.ReadInt32(),
                NumberOfTransformators = reader.ReadInt32()
            };

            var eparam = new EParams
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
            info.eParamsInNetwork = eparam;

            info.current = reader.ReadSingle();

            return info;
        }
    }
}