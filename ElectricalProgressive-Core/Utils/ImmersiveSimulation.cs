using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    public class ImmersiveSimulation
    {
        public BlockPos[][] Path = new BlockPos[100][];
        public byte[][] NodeIndices = new byte[100][]; // Индексы узлов для каждого пути
        public int[] Voltage = new int[100];

        public int CountWorkingStores;
        public int CountWorkingCustomers;

        // Массив для хранения всех расстояний между клиентами и магазинами
        public int[] Distances = new int[100];


        public float[] PathLengths= new float[100];  // Массив фактических длин путей

        /// <summary>
        /// Список клиентов, участвующих в симуляции.
        /// </summary>
        public ImmersiveCustomer[] Customers = new ImmersiveCustomer[100];

        /// <summary>
        /// Список магазинов, участвующих в симуляции.
        /// </summary>
        public ImmersiveStore[] Stores = new ImmersiveStore[100];

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
        /// <param name="immersiveCustomer"></param>
        /// <param name="remaining"></param>
        /// <param name="storeIds"></param>
        private void ProcessStoresArray(ImmersiveCustomer immersiveCustomer, float remaining, int[] storeIds)
        {
            while (immersiveCustomer.HasMoreStores() && remaining > 0.001f)
            {
                var s = immersiveCustomer.GetNextStoreIndex();
                var store = Stores![storeIds[s]];
                if (store.Stock <= 0.001f && store.ImNull)
                    continue;

                var requested = remaining;
                store.CurrentRequests[immersiveCustomer.Id] = requested;
                remaining -= requested;
            }
        }
    }
}