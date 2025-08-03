namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Сборщик информации о сети
    /// </summary>
    public class NetworkInformation
    {
        public float Consumption;
        public float Capacity;    //Емкость батарей
        public float MaxCapacity; //Максимальная емкость батарей
        public float Production;
        public float Request;
        public Facing Facing = Facing.None;
        public int NumberOfAccumulators;
        public int NumberOfBlocks;
        public int NumberOfConsumers;
        public int NumberOfProducers;
        public int NumberOfTransformators;
        public EParams eParamsInNetwork = new();
        public float current;

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
            Facing = Facing.None;
            NumberOfAccumulators = 0;
            NumberOfBlocks = 0;
            NumberOfConsumers = 0;
            NumberOfProducers = 0;
            NumberOfTransformators = 0;
            eParamsInNetwork = new();
            current = 0f;
        }
    }



}