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
        /// Массив текущих запросов от клиентов, индекс соответствует Id клиента.
        /// </summary>
        public float[] CurrentRequests { get; } // Requests from each customer by customer Id

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
        /// <param name="numCustomers"></param>
        public Store(int id, float stock, int numCustomers)
        {
            Id = id;
            Stock = stock;
            CurrentRequests = new float[numCustomers];
        }

        /// <summary>
        /// Сбрасывает текущие запросы от клиентов.
        /// </summary>
        public void ResetRequests()
        {
            Array.Clear(CurrentRequests, 0, CurrentRequests.Length);
        }

        /// <summary>
        /// Обрабатывает запросы от клиентов и распределяет товар по запросам.
        /// </summary>
        /// <param name="customers"></param>
        public void ProcessRequests(List<Customer> customers)
        {
            float totalRequested = 0;
            for (int i = 0; i < CurrentRequests.Length; i++)
            {
                totalRequested += CurrentRequests[i];
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
                for (int i = 0; i < CurrentRequests.Length; i++)
                {
                    if (CurrentRequests[i] > 0)
                    {
                        customers[i].Received[Id] += CurrentRequests[i];
                        Stock -= CurrentRequests[i];
                    }
                }
            }
            else
            {
                float ratio = Stock / totalRequested;
                for (int i = 0; i < CurrentRequests.Length; i++)
                {
                    if (CurrentRequests[i] > 0)
                    {
                        float allocated = CurrentRequests[i] * ratio;
                        customers[i].Received[Id] += allocated;
                        Stock -= allocated;
                    }
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
