using System;
using System.Collections.Generic;

namespace ElectricalProgressive.Utils
{
    public class Store
    {
        /// <summary>
        /// Уникальный идентификатор магазина.
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// Текущее количество товара в магазине.
        /// </summary>
        public float Stock { get; set; }

        /// <summary>
        /// Словарь текущих запросов от клиентов, ключ - Id клиента.
        /// </summary>
        public Dictionary<int, float> CurrentRequests { get; } = new Dictionary<int, float>();

        /// <summary>
        /// Флаг, указывающий, что магазин больше не имеет товара.
        /// </summary>
        public bool ImNull { get; private set; }

        /// <summary>
        /// Общее количество товара, запрошенного от магазина за все время.
        /// </summary>
        public float totalRequest;

        /// <summary>
        /// Инициализирует новый экземпляр класса Store.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="stock"></param>
        public Store(int id, float stock)
        {
            Id = id;
            Stock = stock;
            totalRequest = 0f;
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
        public void ProcessRequests(List<Customer> customers)
        {
            float totalRequested = 0;
            foreach (var req in CurrentRequests.Values)
            {
                totalRequested += req;
            }

            totalRequest += totalRequested;

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
                    int customerId = kvp.Key;
                    float requested = kvp.Value;
                    customers[customerId].AddReceived(Id, requested);
                    Stock -= requested;
                }
            }
            else
            {
                float ratio = Stock / totalRequested;
                foreach (var kvp in CurrentRequests)
                {
                    int customerId = kvp.Key;
                    float requested = kvp.Value;
                    float allocated = requested * ratio;
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
    }
}