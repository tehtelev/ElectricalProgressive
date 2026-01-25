using ElectricalProgressive.Utils;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace EPImmersive.Utils
{
    public static class ImmersiveNetworkInformationSerializer
    {
        public static byte[] Serialize(ImmersiveNetworkInformation info)
        {
            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                writer.Write(info.Consumption);
                writer.Write(info.Capacity);
                writer.Write(info.MaxCapacity);
                writer.Write(info.Production);
                writer.Write(info.Request);
                writer.Write(info.NumberOfAccumulators);
                writer.Write(info.NumberOfBlocks);
                writer.Write(info.NumberOfConsumers);
                writer.Write(info.NumberOfProducers);
                writer.Write(info.NumberOfTransformators);
                writer.Write(info.NumberOfConnections);
                writer.Write(info.NumberOfNetworks);
                writer.Write(info.IsConductorOpen);

                // Сериализуем параметры блока
                var eparam = info.eParamsInNetwork;
                writer.Write(eparam.voltage);
                writer.Write(eparam.maxCurrent);
                writer.Write(eparam.material ?? string.Empty);
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
                

                // Сериализуем список сетей
                writer.Write(info.Networks.Count);
                foreach (var network in info.Networks)
                {
                    writer.Write(network.NumberOfAccumulators);
                    writer.Write(network.NumberOfConsumers);
                    writer.Write(network.NumberOfProducers);
                    writer.Write(network.NumberOfTransformators);
                    writer.Write(network.NumberOfConductors);
                    writer.Write(network.Consumption);
                    writer.Write(network.Capacity);
                    writer.Write(network.MaxCapacity);
                    writer.Write(network.Production);
                    writer.Write(network.Request);
                    
                }
            }
            return ms.ToArray();
        }

        public static ImmersiveNetworkInformation Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms, Encoding.UTF8);

            var info = new ImmersiveNetworkInformation
            {
                Consumption = reader.ReadSingle(),
                Capacity = reader.ReadSingle(),
                MaxCapacity = reader.ReadSingle(),
                Production = reader.ReadSingle(),
                Request = reader.ReadSingle(),
                NumberOfAccumulators = reader.ReadInt32(),
                NumberOfBlocks = reader.ReadInt32(),
                NumberOfConsumers = reader.ReadInt32(),
                NumberOfProducers = reader.ReadInt32(),
                NumberOfTransformators = reader.ReadInt32(),
                NumberOfConnections = reader.ReadInt32(),
                NumberOfNetworks = reader.ReadInt32(),
                IsConductorOpen = reader.ReadBoolean()
            };

            // Десериализуем параметры блока
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

            // Десериализуем список сетей
            int networkCount = reader.ReadInt32();
            for (int i = 0; i < networkCount; i++)
            {
                var network = new NetworkData
                {
                    NumberOfAccumulators = reader.ReadInt32(),
                    NumberOfConsumers = reader.ReadInt32(),
                    NumberOfProducers = reader.ReadInt32(),
                    NumberOfTransformators = reader.ReadInt32(),
                    NumberOfConductors = reader.ReadInt32(),
                    Consumption = reader.ReadSingle(),
                    Capacity = reader.ReadSingle(),
                    MaxCapacity = reader.ReadSingle(),
                    Production = reader.ReadSingle(),
                    Request = reader.ReadSingle()
                };
                info.Networks.Add(network);
            }

            return info;
        }
    }
}