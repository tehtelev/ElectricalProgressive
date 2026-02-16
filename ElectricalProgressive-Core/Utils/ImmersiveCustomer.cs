using System;

namespace ElectricalProgressive.Utils
{
    public class ImmersiveCustomer
    {
        /// <summary>
        /// Уникальный идентификатор клиента.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Требуемое количество товара клиентом.
        /// </summary>
        public float Required;

        /// <summary>
        /// Расстояния до каждого магазина, индекс соответствует Id магазина.
        /// </summary>
        public int[] StoreDistances;

        /// <summary>
        /// Полученное количество товара от каждого магазина, индекс соответствует Id магазина.
        /// </summary>
        public float[] Received;

        /// <summary>
        /// Массив идентификаторов магазинов, отсортированных по расстоянию до клиента.
        /// </summary>
        private int[] orderedStoreIds;

        /// <summary>
        /// Сумма полученного товара от всех магазинов.
        /// </summary>
        private float _receivedSum;

        /// <summary>
        /// Индекс текущего магазина, от которого клиент получает товар.
        /// </summary>
        private int _currentStoreIndex;

        // Временные массивы для поразрядной сортировки
        private int[] _radixSortOutput;
        private int[] _radixSortCount;

        /// <summary>
        /// Инициализирует новый экземпляр класса ImmersiveCustomer.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="required"></param>
        /// <param name="storeDistances"></param>
        public ImmersiveCustomer(int id, float required, int[] storeDistances)
        {
            Id = id;
            Required = required;
            StoreDistances = storeDistances;
            Received = new float[storeDistances.Length];
            orderedStoreIds = new int[storeDistances.Length];
            _receivedSum = 0f;
            _currentStoreIndex = 0;
            UpdateOrderedStores();
        }

        /// <summary>
        /// Возвращает количество товара, которое клиент еще должен получить.
        /// </summary>
        public float Remaining => Required - _receivedSum;

        /// <summary>
        /// Возвращает общее расстояние, которое клиент должен пройти для получения товара.
        /// </summary>
        public double TotalDistance
        {
            get
            {
                double total = 0;
                for (var i = 0; i < StoreDistances.Length; i++)
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
            var n = StoreDistances.Length;
            for (var i = 0; i < n; i++)
            {
                orderedStoreIds[i] = i;
            }

            if (n == 0) return;

            // Проверяем и выделяем память для временных массивов при необходимости
            if (_radixSortOutput == null || _radixSortOutput.Length < n)
            {
                _radixSortOutput = new int[n];
                _radixSortCount = new int[256];
            }

            RadixSort(orderedStoreIds, StoreDistances, _radixSortOutput, _radixSortCount);
        }

        /// <summary>
        /// Поразрядная сортировка для целых чисел.
        /// </summary>
        private void RadixSort(int[] indices, int[] keys, int[] output, int[] count)
        {
            var n = indices.Length;

            for (var shift = 0; shift < 4; shift++)
            {
                Array.Fill(count, 0, 0, 256);

                for (var i = 0; i < n; i++)
                {
                    var key = (uint)keys[indices[i]];
                    var b = (byte)((key >> (shift * 8)) & 0xFF);
                    count[b]++;
                }

                for (var i = 1; i < 256; i++)
                    count[i] += count[i - 1];

                for (var i = n - 1; i >= 0; i--)
                {
                    var key = (uint)keys[indices[i]];
                    var b = (byte)((key >> (shift * 8)) & 0xFF);
                    output[--count[b]] = indices[i];
                }

                Array.Copy(output, indices, n);
            }
        }

        /// <summary>
        /// Возвращает массив идентификаторов магазинов, отсортированных по расстоянию до клиента.
        /// </summary>
        /// <returns></returns>
        public int[] GetAvailableStoreIds() => orderedStoreIds;

        /// <summary>
        /// Добавляет полученное количество от магазина.
        /// </summary>
        internal void AddReceived(int storeId, float amount)
        {
            Received[storeId] += amount;
            _receivedSum += amount;
        }

        /// <summary>
        /// Сбрасывает текущий индекс магазина.
        /// </summary>
        internal void ResetStoreIndex()
        {
            _currentStoreIndex = 0;
        }

        /// <summary>
        /// Возвращает следующий доступный индекс магазина.
        /// </summary>
        internal int GetNextStoreIndex() => _currentStoreIndex++;

        /// <summary>
        /// Проверяет, есть ли еще магазины.
        /// </summary>
        internal bool HasMoreStores() => _currentStoreIndex < orderedStoreIds.Length;

        public void Update(int id, float required, int[] storeDistances)
        {
            Id = id;
            Required = required;

            if (StoreDistances == null || StoreDistances.Length != storeDistances.Length)
            {
                StoreDistances = storeDistances;
                Received = new float[storeDistances.Length];
                orderedStoreIds = new int[storeDistances.Length];
                UpdateOrderedStores();
            }
            else
            {
                Array.Copy(storeDistances, StoreDistances, storeDistances.Length);
                Array.Clear(Received, 0, Received.Length);
                UpdateOrderedStores();
            }

            _receivedSum = 0f;
            _currentStoreIndex = 0;
        }
    }
}