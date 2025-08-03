using System;

namespace ElectricalProgressive.Utils
{
    public class Customer
    {
        /// <summary>
        /// Уникальный идентификатор клиента.
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// Требуемое количество товара клиентом.
        /// </summary>
        public float Required { get; }

        /// <summary>
        /// Расстояния до каждого магазина, индекс соответствует Id магазина.
        /// </summary>
        public int[] StoreDistances { get; }

        /// <summary>
        /// Полученное количество товара от каждого магазина, индекс соответствует Id магазина.
        /// </summary>
        public float[] Received { get; }

        /// <summary>
        /// Массив идентификаторов магазинов, отсортированных по расстоянию до клиента.
        /// </summary>
        private int[] orderedStoreIds;


        /// <summary>
        /// Инициализирует новый экземпляр класса Customer.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="required"></param>
        /// <param name="storeDistances"></param>
        public Customer(int id, float required, int[] storeDistances)
        {
            Id = id;
            Required = required;
            StoreDistances = storeDistances;
            Received = new float[storeDistances.Length];
            orderedStoreIds= new int[storeDistances.Length];
            UpdateOrderedStores();
        }

        /// <summary>
        /// Возвращает количество товара, которое клиент еще должен получить.
        /// </summary>
        public float Remaining
        {
            get
            {
                float receivedSum = 0;
                for (int i = 0; i < Received.Length; i++)
                {
                    receivedSum += Received[i];
                }
                return Required - receivedSum;
            }
        }

        /// <summary>
        /// Возвращает общее расстояние, которое клиент должен пройти для получения товара.
        /// </summary>
        public double TotalDistance
        {
            get
            {
                double total = 0;
                for (int i = 0; i < Received.Length; i++)
                {
                    total += StoreDistances[i] * Received[i];
                }
                return total;
            }
        }

        /// <summary>
        /// Обновляет массив идентификаторов магазинов, отсортированных по расстоянию до клиента.
        /// </summary>
        private void UpdateOrderedStores()
        {
            orderedStoreIds = new int[StoreDistances.Length];
            for (int i = 0; i < orderedStoreIds.Length; i++)
            {
                orderedStoreIds[i] = i;
            }
            Array.Sort(orderedStoreIds, (a, b) => StoreDistances[a].CompareTo(StoreDistances[b]));
        }

        /// <summary>
        /// Возвращает массив идентификаторов магазинов, отсортированных по расстоянию до клиента.
        /// </summary>
        /// <returns></returns>
        public int[] GetAvailableStoreIds() => orderedStoreIds;
    }
}


