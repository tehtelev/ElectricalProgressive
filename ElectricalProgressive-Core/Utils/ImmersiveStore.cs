using System.Collections.Generic;

namespace ElectricalProgressive.Utils
{
    public class ImmersiveStore
    {
        /// <summary>
        /// Уникальный идентификатор магазина.
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Текущее количество товара в магазине.
        /// </summary>
        public float Stock { get; set; }

        /// <summary>
        /// Словарь текущих запросов от клиентов, ключ - Id клиента.
        /// </summary>
        public Dictionary<int, float> CurrentRequests { get; } = new();

        /// <summary>
        /// Флаг, указывающий, что магазин больше не имеет товара.
        /// </summary>
        public bool ImNull { get; private set; }

        /// <summary>
        /// Общее количество товара, запрошенного от магазина за все время.
        /// </summary>
        public float TotalRequest;

        /// <summary>
        /// Инициализирует новый экземпляр класса ImmersiveStore.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="stock"></param>
        public ImmersiveStore(int id, float stock)
        {
            Id = id;
            Stock = stock;
            TotalRequest = 0f;
        }

        /// <summary>
        /// Сбрасывает текущие запросы от клиентов.
        /// </summary>
        public void ResetRequests()
        {
            CurrentRequests.Clear();
        }

        /// <summary>
        /// Обрабатывает запросы от клиентов и распределяет товар по запросам.
        /// </summary>
        /// <param name="customers"></param>
        public void ProcessRequests(ImmersiveCustomer[] customers)
        {
            float totalRequested = 0;
            foreach (var req in CurrentRequests.Values)
            {
                totalRequested += req;
            }

            TotalRequest += totalRequested;

            if (Stock <= 0.001f)
            {
                Stock = 0.0f;
                ImNull = true;
                ResetRequests();
                return;
            }

            if (totalRequested == 0) return;

            if (Stock >= totalRequested)
            {
                foreach (var kvp in CurrentRequests)
                {
                    var customerId = kvp.Key;
                    var requested = kvp.Value;
                    customers[customerId].AddReceived(Id, requested);
                    Stock -= requested;
                }
            }
            else
            {
                var ratio = Stock / totalRequested;
                foreach (var kvp in CurrentRequests)
                {
                    var customerId = kvp.Key;
                    var requested = kvp.Value;
                    var allocated = requested * ratio;
                    customers[customerId].AddReceived(Id, allocated);
                    Stock -= allocated;
                }
            }

            if (Stock <= 0.001f)
            {
                Stock = 0.0f;
                ImNull = true;
            }

            ResetRequests();
        }

        public void Update(int id, float stock)
        {
            Id = id;
            Stock = stock;
            TotalRequest = 0f;
            ImNull = false;
        }
    }
}