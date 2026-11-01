using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    public class Simulation
    {
        public BlockPos[][] Path = new BlockPos[100][];
        public byte[][]? FacingFrom = new byte[100][];
        public bool[][][]? NowProcessedFaces= new bool[100][][];
        public Facing[][]? UsedConnection = new Facing[100][];
        public int[] Voltage = new int[100];




        public int CountWorkingStores;
        public int CountWorkingCustomers;

        // Массив для хранения всех расстояний между клиентами и магазинами
        public int[] Distances = new int[100];




        /// <summary>
        /// Список клиентов, участвующих в симуляции.
        /// </summary>
        public Customer[] Customers = new Customer[100];

        /// <summary>
        /// Список магазинов, участвующих в симуляции.
        /// </summary>
        public Store[] Stores = new Store[100];

        /// <summary>
        /// Запускает симуляцию распределения товара между клиентами и магазинами.
        /// </summary>
        public void Run()
        {
            // Проверяем, что списки клиентов и магазинов инициализированы
            if (Customers == null || Stores == null)
                return;

            for (var i = 0; i < CountWorkingStores; i++)
            {
                Stores[i].TotalRequest = 0;
            }

            bool hasActiveStores;
            bool hasPendingCustomers;

            do
            {
                for (var i = 0; i < CountWorkingStores; i++)
                {
                    Stores[i].ResetRequests();
                }

                for (var c = 0; c < CountWorkingCustomers; c++)
                {
                    var customer = Customers[c];
                    customer.ResetStoreIndex();
                    if (customer.Remaining <= 0.001f)
                        continue;

                    var remaining = customer.Remaining;
                    var availableStoreIds = customer.GetAvailableStoreIds();
                    ProcessStoresArray(customer, remaining, availableStoreIds);
                }

                for (var i = 0; i < CountWorkingStores; i++)
                {
                    Stores[i].ProcessRequests(Customers);
                }

                hasActiveStores = false;
                for (var i = 0; i < CountWorkingStores; i++)
                {
                    if (!Stores[i].ImNull)
                    {
                        hasActiveStores = true;
                        break;
                    }
                }

                hasPendingCustomers = false;
                for (var i = 0; i < CountWorkingCustomers; i++)
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
                var s = customer.GetNextStoreIndex();
                var store = Stores![storeIds[s]];
                if (store.Stock <= 0.001f && store.ImNull)
                    continue;

                var requested = remaining;
                store.CurrentRequests[customer.Id] = requested;
                remaining -= requested;
            }
        }

        /*
        /// <summary>
        /// Сбрасывает состояние симуляции, очищая списки клиентов и магазинов.
        /// </summary>
        public void Reset()
        {
   
        }
        */
    }
}