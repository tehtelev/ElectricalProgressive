using ElectricalProgressive.Interface;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Сеть
    /// </summary>
    public class ImmersiveNetwork
    {
        public readonly HashSet<IEImmersiveAccumulator> Accumulators = [];  //Аккумуляторы
        public readonly HashSet<IEImmersiveConsumer> Consumers = [];       //Потребители
        public readonly HashSet<IEImmersiveConductor> Conductors = [];       //Проводники
        public readonly HashSet<IEImmersiveProducer> Producers = [];           //Генераторы
        public readonly HashSet<IEImmersiveTransformator> Transformators = [];  //Трансформаторы
        public readonly HashSet<BlockPos> PartPositions = [];     //Координаты позиций сети

        public float Consumption; //Потребление
        public float Capacity;    //Емкость батарей
        public float MaxCapacity; //Максимальная емкость батарей
        public float Production;  //Генерация
        public float Request;     //Необходимость
        public int version;       //Версия сети, для отслеживания изменений

        // все соединения сети
        public List<NetworkImmersiveConnection> ImmersiveConnections = []; 

    }
}