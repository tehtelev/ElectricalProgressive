using ElectricalProgressive.Utils;
using ProtoBuf;
using System.Collections.Generic;

namespace EPImmersive.Utils
{
    /// <summary>
    /// Сборщик информации о сети иммерсивных проводов
    /// </summary>
    public class ImmersiveNetworkInformation
    {
        public float Consumption;
        public float Capacity;    //Емкость батарей
        public float MaxCapacity; //Максимальная емкость батарей
        public float Production;
        public float Request;
        public int NumberOfAccumulators;
        public int NumberOfBlocks;
        public int NumberOfConsumers;
        public int NumberOfProducers;
        public int NumberOfTransformators;
        public int NumberOfConnections; // Количество подключенных проводов
        public int NumberOfNetworks; // Количество независимых сетей
        public List<NetworkData> Networks = new(); // Информация о каждой сети
        public EParams eParamsInNetwork = new();
        public float current;
        public bool IsConductorOpen; // Проводник разомкнут

        /// <summary>
        /// Сбросить все значения до стандартных
        /// </summary>
        public void Reset()
        {
            Consumption = 0f;
            Capacity = 0f;
            MaxCapacity = 0f;
            Production = 0f;
            Request = 0f;
            NumberOfAccumulators = 0;
            NumberOfBlocks = 0;
            NumberOfConsumers = 0;
            NumberOfProducers = 0;
            NumberOfTransformators = 0;
            NumberOfConnections = 0;
            NumberOfNetworks = 0;
            Networks.Clear();
            eParamsInNetwork = new();
            current = 0f;
        }
    }

    /// <summary>
    /// Информация об одной сети
    /// </summary>
    public class NetworkData
    {
        public int NumberOfAccumulators;
        public int NumberOfConsumers;
        public int NumberOfProducers;
        public int NumberOfTransformators;
        public int NumberOfConductors;
        public float Consumption;
        public float Capacity;
        public float MaxCapacity;
        public float Production;
        public float Request;
        
    }
}