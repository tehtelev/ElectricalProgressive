using System.Collections.Generic;
using Vintagestory.API.MathTools;
using ElectricalProgressive.Interface;

namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Сеть
    /// </summary>
    public class Network
    {
        public readonly HashSet<IElectricAccumulator> Accumulators = new();  //Аккумуляторы
        public readonly HashSet<IElectricConsumer> Consumers = new();       //Потребители
        public readonly HashSet<IElectricConductor> Conductors = new();       //Проводники
        public readonly HashSet<IElectricProducer> Producers = new();           //Генераторы
        public readonly HashSet<IElectricTransformator> Transformators = new();  //Трансформаторы
        public readonly HashSet<BlockPos> PartPositions = new();     //Координаты позиций сети
        public float Consumption; //Потребление
        public float Capacity;    //Емкость батарей
        public float MaxCapacity; //Максимальная емкость батарей
        public float Production;  //Генерация
        public float Request;     //Необходимость
        public int version;       //Версия сети, для отслеживания изменений

    }
}