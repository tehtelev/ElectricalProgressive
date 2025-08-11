using System.Collections.Generic;

namespace ElectricalProgressive.Utils
{
    public class Simulation
    {
        /// <summary>
        /// Список клиентов, участвующих в симуляции.
        /// </summary>
        public List<Customer>? Customers { get; set; }

        /// <summary>
        /// Список магазинов, участвующих в симуляции.
        /// </summary>
        public List<Store>? Stores { get; set; }

        /// <summary>
        /// Запускает симуляцию распределения товара между клиентами и магазинами.
        /// </summary>
        public void Run()
        {
            // Проверяем, что списки клиентов и магазинов инициализированы
            if (Customers == null || Stores == null)
                return;

            for (int i = 0; i < Stores.Count; i++)
            {
                Stores[i].totalRequest = 0;
            }

            bool hasActiveStores;
            bool hasPendingCustomers;

            do
            {
                for (int i = 0; i < Stores.Count; i++)
                {
                    Stores[i].ResetRequests();
                }

                for (int c = 0; c < Customers.Count; c++)
                {
                    var customer = Customers[c];
                    customer.ResetStoreIndex();
                    if (customer.Remaining <= 0.001f)
                        continue;

                    float remaining = customer.Remaining;
                    int[] availableStoreIds = customer.GetAvailableStoreIds();
                    ProcessStoresArray(customer, remaining, availableStoreIds);
                }

                for (int i = 0; i < Stores.Count; i++)
                {
                    Stores[i].ProcessRequests(Customers);
                }

                hasActiveStores = false;
                for (int i = 0; i < Stores.Count; i++)
                {
                    if (!Stores[i].ImNull)
                    {
                        hasActiveStores = true;
                        break;
                    }
                }

                hasPendingCustomers = false;
                for (int i = 0; i < Customers.Count; i++)
                {
                    if (Customers[i].Remaining > 0.001f)
                    {
                        hasPendingCustomers = true;
                        break;
                    }
                }

            }
            while (hasActiveStores && hasPendingCustomers);
        }

        /// <summary>
        /// Обрабатывает массив идентификаторов магазинов для клиента, распределяя оставшееся количество товара между магазинами.
        /// </summary>
        /// <param name="customer"></param>
        /// <param name="remaining"></param>
        /// <param name="storeIds"></param>
        private void ProcessStoresArray(Customer customer, float remaining, int[] storeIds)
        {
            while (customer.HasMoreStores() && remaining > 0.001f)
            {
                int s = customer.GetNextStoreIndex();
                var store = Stores![storeIds[s]];
                if (store.Stock <= 0.001f && store.ImNull)
                    continue;

                float requested = remaining;
                store.CurrentRequests[customer.Id] = requested;
                remaining -= requested;
            }
        }

        /// <summary>
        /// Сбрасывает состояние симуляции, очищая списки клиентов и магазинов.
        /// </summary>
        public void Reset()
        {
            Stores?.Clear();
            Customers?.Clear();
        }
    }
}