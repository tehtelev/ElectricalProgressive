using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using static ElectricalProgressive.ElectricalProgressive;


[assembly: ModDependency("game", "1.22.0-pre.3")]
[assembly: ModInfo(
    "Electrical Progressive: Core",
    "electricalprogressivecore",
    Website = "https://github.com/tehtelev/ElectricalProgressive",
    Description = "Electrical logic library.",
    Version = "3.0.0-rc.1",
    Authors = ["Tehtelev", "Kotl"]
)]



namespace ElectricalProgressive
{
    public class ElectricalProgressive : ModSystem
    {
        public readonly HashSet<Network> Networks = [];
        public readonly Dictionary<BlockPos, NetworkPart> Parts = new(new BlockPosComparer()); // Хранит все элементы всех цепей

        private Dictionary<BlockPos, List<EnergyPacket>> _packetsByPosition = new(new BlockPosComparer()); //Словарь для хранения пакетов по позициям


        private readonly List<EnergyPacket> _globalEnergyPackets = []; // Глобальный список пакетов энергии

        private AsyncPathFinder _asyncPathFinder = null!;



        private Dictionary<BlockPos, float> _sumEnergy = new();



        public ICoreAPI Api = null!;
        public ICoreClientAPI _capi = null!;
        private ICoreServerAPI _sapi = null!;
        private ElectricityConfig? _config;
        public static DamageManager? damageManager;
        public static WeatherSystemServer? WeatherSystemServer;


        private Network _localNetwork = new();

        private readonly BlockingCollection<Network> _networkProcessingQueue = new(); // коллекция для сетей
        private readonly List<Thread> _networkProcessingThreads = [];                //список потоков работников
        private volatile bool _networkProcessingRunning = true;                         //сети работают?
        private readonly CountdownEvent _networkProcessingCompleted = new(0); // ивент для окончания ожидания потоков
        private readonly ConcurrentBag<List<EnergyPacket>> _networkResults = [];      // список для пакетов в потоках

        public static int speedOfElectricity; // Скорость электричества в проводах (блоков в тик)
        public static int timeBeforeBurnout; // Время до сгорания проводника в секундах
        public static int multiThreading; // сколько потоков использовать
        public static int cacheTimeoutCleanupMinutes; // Время очистки кэша путей в минутах
        public static int maxDistanceForFinding; // Максимальное расстояние для поиска пути
        public static float energyLossFactor; // Коэффициент потерь энергии на проводах
        public static bool enableLossCompensation; // Включить компенсацию потерь энергии на проводах


        public static AssetLocation soundElectricShok;

        public int TickTimeMs;
        private float _elapsedMs = 0f;

        int _envUpdater = 0;

        private long _listenerId1;
        //private long listenerId2;

        private NetworkInformation _result = new();

        /// <summary>
        /// Запуск модификации
        /// </summary>
        /// <param name="api"></param>
        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            this.Api = api;

            soundElectricShok = new AssetLocation("electricalprogressivecore:sounds/electric-shock.ogg");
        }




        /// <summary>
        /// Освобождение ресурсов
        /// </summary>
        public override void Dispose()
        {
            base.Dispose();

            // Останавливаем обработку сетей
            _networkProcessingRunning = false;

            // Добавляем null-значения в очередь, чтобы разблокировать потоки
            foreach (var thread in _networkProcessingThreads)
            {
                _networkProcessingQueue.Add(null);
            }

            // Ждем завершения потоков
            foreach (var thread in _networkProcessingThreads)
            {
                thread.Join(1000);
            }

            // Останавливаем поиск путей
            if (_sapi != null)
            {
                _sapi.Event.UnregisterGameTickListener(_listenerId1);
                _asyncPathFinder?.Stop();
                _asyncPathFinder = null;
            }

            // Очистка ресурсов
            _globalEnergyPackets.Clear();
            _sumEnergy.Clear();
            _packetsByPosition.Clear();
            _networkProcessingQueue.Dispose();
            _networkProcessingCompleted.Dispose();

            Api = null!;
            _capi = null!;
            _sapi = null!;
            damageManager = null;
            WeatherSystemServer = null;

            Networks.Clear();
            Parts.Clear();
            PathCacheManager.Dispose();
        }




        /// <summary>
        /// Загрузка конфигурации и начальная инициализация
        /// </summary>
        /// <param name="api"></param>
        public override void StartPre(ICoreAPI api)
        {
            // грузим конфиг
            // если конфиг с ошибкой или не найден, то генерируется стандартный
            _config = api.LoadModConfig<ElectricityConfig>("ElectricityConfig.json") ?? new ElectricityConfig();
            api.StoreModConfig(_config, "ElectricityConfig.json");

            // проверяем, что конфиг валиден, и обрезаются значения
            speedOfElectricity = Math.Clamp(_config.SpeedOfElectricity, 1, 16);
            timeBeforeBurnout = Math.Clamp(_config.TimeBeforeBurnout, 1, 600);
            multiThreading = Math.Clamp(_config.MultiThreading, 2, 32);
            cacheTimeoutCleanupMinutes = Math.Clamp(_config.CacheTimeoutCleanupMinutes, 1, 60);
            maxDistanceForFinding = Math.Clamp(_config.MaxDistanceForFinding, 8, 1000);
            energyLossFactor = Math.Clamp(_config.EnergyLossFactor, 0.0f, 2.0f);
            enableLossCompensation = _config.EnableLossCompensation;

            // устанавливаем время между тиками
            TickTimeMs = 1000 / speedOfElectricity;
        }



        /// <summary>
        /// Запуск клиентской стороны
        /// </summary>
        /// <param name="api"></param>
        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            this._capi = api;
            RegisterAltKeys();


            //listenerId2 = capi.Event.RegisterGameTickListener(this.OnGameTickClient, tickTimeMs);
        }






        /// <summary>
        /// Регистрация клавиш Alt
        /// </summary>
        private void RegisterAltKeys()
        {
            _capi.Input.RegisterHotKey("AltPressForNetwork", Lang.Get("electricalprogressivecore:AltPressForNetworkName"), GlKeys.LAlt);
        }


        /// <summary>
        /// Серверная сторона
        /// </summary>
        /// <param name="api"></param>
        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            this._sapi = api;

            WeatherSystemServer = _sapi.ModLoader.GetModSystem<WeatherSystemServer>();
            damageManager = new DamageManager(api);

            _listenerId1 = _sapi.Event.RegisterGameTickListener(this.OnGameTickServer, TickTimeMs);

            // Инициализация поиска путей
            _asyncPathFinder = new AsyncPathFinder(Parts, ElectricalProgressive.multiThreading);

            // Инициализация потоков для обработки сетей
            int threadCount = ElectricalProgressive.multiThreading;
            for (int i = 0; i < threadCount; i++)
            {
                var thread = new Thread(() => ProcessNetworksWorker())
                {
                    Name = $"NetworkProcessor-Classic-{i}",
                    IsBackground = true
                };
                _networkProcessingThreads.Add(thread);
                thread.Start();
            }
        }



        /// <summary>
        /// Метод-воркер для обработки сетей
        /// </summary>
        private void ProcessNetworksWorker()
        {
            while (_networkProcessingRunning)
            {
                try
                {
                    if (_networkProcessingQueue.TryTake(out var network, Timeout.Infinite))
                    {
                        var context = GetContext();
                        try
                        {
                            ProcessNetwork(network, context);

                            if (context.LocalPackets.Count > 0)
                            {
                                var packetsCopy = new List<EnergyPacket>(context.LocalPackets);
                                _networkResults.Add(packetsCopy);
                            }
                        }
                        finally
                        {
                            ReturnContext(context);
                            _networkProcessingCompleted.Signal();
                        }
                    }
                }
                catch
                {
                    // Логирование ошибки
                }
            }
        }



        /// <summary>
        /// Обновление электрической сети
        /// </summary>
        /// <param name="position"></param>
        /// <param name="facing"></param>
        /// <param name="setEparams"></param>
        /// <param name="eparams"></param>
        /// <returns></returns>
        public bool Update(BlockPos position, Facing facing, (EParams, int) setEparams, ref EParams[] eparams, bool isLoaded)
        {
            if (!Parts.TryGetValue(position, out var part))
            {
                if (facing == Facing.None)
                    return false;
                part = Parts[position] = new NetworkPart(position);
            }

            var addedConnections = ~part.Connection & facing;
            var removedConnections = part.Connection & ~facing;

            part.IsLoaded = isLoaded;
            part.eparams = eparams;
            part.Connection = facing;

            AddConnections(ref part, addedConnections, setEparams);
            RemoveConnections(ref part, removedConnections);

            if (part.Connection == Facing.None)
                Parts.Remove(position);

            //Cleaner();
            eparams = part.eparams;
            return true;
        }


        /// <summary>
        /// Удаляем соединения
        /// </summary>
        /// <param name="position"></param>
        public void Remove(BlockPos position)
        {
            if (Parts.TryGetValue(position, out var part))
            {
                Parts.Remove(position);
                RemoveConnections(ref part, part.Connection);
            }
        }




        /// <summary>
        /// Чистка всего и вся
        /// </summary>
        public void Cleaner()
        {
            NetworkPart part;
            EParams[] eparams;

            foreach (var kvp in Parts)
            {
                part = kvp.Value; // сохраняем ссылку 

                // очистка списка пакетов
                part.packets?.Clear();

                eparams = part.eparams;

                // проверка и обновление eparams
                if (eparams is { Length: 6 })
                {
                    // уменьшаем ticksBeforeBurnout только для реальных проводников
                    if (part.Conductor is not VirtualConductor)
                    {
                        for (int i = 0; i < 6; i++)
                        {
                            ref var ep = ref eparams[i];
                            if (!ep.burnout && ep.ticksBeforeBurnout > 0)
                                ep.ticksBeforeBurnout--;
                        }
                    }
                }
                else
                {
                    // создаём новый массив, если он некорректен
                    eparams = new EParams[6]
                    {
                        new(), new(), new(),
                        new(), new(), new()
                    };
                    part.eparams = eparams;
                }

                // обнуление токов
                for (int i = 0; i < 6; i++)
                    eparams[i].current = 0f;

                // сброс накопленной энергии
                _sumEnergy[kvp.Key] = 0f;
            }
        }





        /// <summary>
        /// Логистическая задача
        /// </summary>
        /// <param name="network"></param>
        /// <param name="consumerPositions"></param>
        /// <param name="consumerRequests"></param>
        /// <param name="producerPositions"></param>
        /// <param name="producerGive"></param>
        /// <param name="sim"></param>
        private void LogisticalTask(Network network,
                  List<BlockPos> consumerPositions,
                  List<float> consumerRequests,
                  List<BlockPos> producerPositions,
                  List<float> producerGive,
                  Simulation sim)
        {
            int cP = sim.CountWorkingCustomers = consumerPositions.Count;
            int pP = sim.CountWorkingStores = producerPositions.Count;

            // 1. Кешируем часто используемые поля Simulation
            var distances = sim.Distances;
            var paths = sim.Path;
            var facingFrom = sim.FacingFrom;
            var nowProcessed = sim.NowProcessedFaces;
            var usedConn = sim.UsedConnection;
            var voltages = sim.Voltage;

            // 2. Убеждаемся, что массивы достаточного размера
            int totalSize = cP * pP;
            if (distances.Length < totalSize)
            {
                Array.Resize(ref distances, totalSize);
                sim.Distances = distances;
                Array.Resize(ref paths, totalSize);
                sim.Path = paths;
                Array.Resize(ref facingFrom, totalSize);
                sim.FacingFrom = facingFrom;
                Array.Resize(ref nowProcessed, totalSize);
                sim.NowProcessedFaces = nowProcessed;
                Array.Resize(ref usedConn, totalSize);
                sim.UsedConnection = usedConn;
                Array.Resize(ref voltages, totalSize);
                sim.Voltage = voltages;
            }

            // 3. Массивы для Store и Customer
            if (sim.Stores == null || sim.Stores.Length < pP)
                sim.Stores = new Store[pP];
            if (sim.Customers == null || sim.Customers.Length < cP)
                sim.Customers = new Customer[cP];

            // 4. Кешируем максимальную дистанцию поиска
            int maxDist = ElectricalProgressive.maxDistanceForFinding;

            // 5. Основной цикл: для каждой пары (потребитель, производитель)
            for (int i = 0; i < cP; i++)
            {
                BlockPos start = consumerPositions[i];
                int baseIdx = i * pP; // базовый индекс для этой строки

                for (int j = 0; j < pP; j++)
                {
                    int idx = baseIdx + j; // единственное вычисление индекса

                    // Проверяем эвристику
                    if (PathFinder.Heuristic(start, producerPositions[j]) < maxDist)
                    {
                        if (PathCacheManager.TryGet(start, producerPositions[j],
                                out var cachedPath, out var fFrom, out var nProc,
                                out var usedConns, out var version, out var volt))
                        {
                            distances[idx] = cachedPath?.Length ?? int.MaxValue;
                            paths[idx] = cachedPath;
                            facingFrom[idx] = fFrom;
                            nowProcessed[idx] = nProc;
                            usedConn[idx] = usedConns;
                            voltages[idx] = volt;

                            if (version != network.version)
                                _asyncPathFinder.EnqueueRequest(start, producerPositions[j], network);
                        }
                        else
                        {
                            _asyncPathFinder.EnqueueRequest(start, producerPositions[j], network);
                            // Заполняем значениями по умолчанию
                            distances[idx] = int.MaxValue;
                            paths[idx] = null;
                            facingFrom[idx] = null;
                            nowProcessed[idx] = null;
                            usedConn[idx] = null;
                            voltages[idx] = 0;
                        }
                    }
                    else
                    {
                        distances[idx] = int.MaxValue;
                        paths[idx] = null;
                        facingFrom[idx] = null;
                        nowProcessed[idx] = null;
                        usedConn[idx] = null;
                        voltages[idx] = 0;
                    }
                }
            }

            // 6. Инициализация магазинов (Store)
            for (int j = 0; j < pP; j++)
            {
                var store = sim.Stores[j];
                if (store == null)
                    sim.Stores[j] = new Store(j, producerGive[j]);
                else
                    store.Update(j, producerGive[j]);
            }

            // 7. Инициализация потребителей (Customer) с переиспользованием буфера
            //    ВНИМАНИЕ: предполагается, что Customer.Update() копирует данные из переданного массива,
            //    а не сохраняет ссылку на него. Если это не так, использовать пул или новый массив.
            int[] distBuffer = null; // будет создан при необходимости
            for (int i = 0; i < cP; i++)
            {
                int baseIdx = i * pP;

                // Создаём или переиспользуем буфер нужного размера
                if (distBuffer == null || distBuffer.Length < pP)
                    distBuffer = new int[pP];

                // Копируем строку расстояний во временный буфер
                Array.Copy(distances, baseIdx, distBuffer, 0, pP);

                var cust = sim.Customers[i];
                if (cust == null)
                    sim.Customers[i] = new Customer(i, consumerRequests[i], distBuffer);
                else
                    cust.Update(i, consumerRequests[i], distBuffer);
            }

            // 8. Запуск симуляции
            sim.Run();
        }






        /// <summary>
        /// Обновление электрических сетей
        /// </summary>
        private void UpdateNetworkComponents()
        {
            if (_elapsedMs > 1.0f) //обновляем инфу раз в секунду
            {
                foreach (var part in Parts.Values)
                {
                    // проводники первыми, так как обычно их больше
                    if (part.Conductor is not null && part.IsLoaded) // Проверяем, что загружен и существует
                    {
                        part.Conductor.Update();
                        continue;
                    }

                    if (part.Producer is not null && part.IsLoaded) // Проверяем, что загружен и существует
                    {
                        part.Producer.Update();
                        continue;
                    }

                    if (part.Consumer is not null && part.IsLoaded) // Проверяем, что загружен и существует
                    {
                        part.Consumer.Update();
                        continue;
                    }

                    if (part.Accumulator is not null && part.IsLoaded) // Проверяем, что загружен и существует
                    {
                        part.Accumulator.Update();
                        continue;
                    }

                    if (part.Transformator is not null && part.IsLoaded) // Проверяем, что загружен и существует
                    {
                        part.Transformator.Update();
                        continue;
                    }
                }

                _elapsedMs = 0f; // сбросить накопленное время
            }
        }




        /*
        /// <summary>
        /// Тикаем клиент
        /// </summary>
        /// <param name="deltaTime"></param>
        private void OnGameTickClient(float deltaTime)
        {



        }
        */


        // Добавляем класс для пула контекстов обработки
        private class NetworkProcessingContext
        {
            public List<Consumer> LocalConsumers { get; } = [];
            public List<Producer> LocalProducers { get; } = [];
            public List<Accumulator> LocalAccums { get; } = [];
            public List<EnergyPacket> LocalPackets { get; } = [];

            public List<BlockPos> ConsumerPositions { get; } = [];
            public List<float> ConsumerRequests { get; } = [];
            public List<BlockPos> ProducerPositions { get; } = [];
            public List<float> ProducerGive { get; } = [];
            public List<BlockPos> Consumer2Positions { get; } = [];
            public List<float> Consumer2Requests { get; } = [];
            public List<BlockPos> Producer2Positions { get; } = [];
            public List<float> Producer2Give { get; } = [];

            public Simulation Sim { get; } = new();
            public Simulation Sim2 { get; } = new();

            public void Clear()
            {
                LocalConsumers.Clear();
                LocalProducers.Clear();
                LocalAccums.Clear();
                LocalPackets.Clear();
                ConsumerPositions.Clear();
                ConsumerRequests.Clear();
                ProducerPositions.Clear();
                ProducerGive.Clear();
                Consumer2Positions.Clear();
                Consumer2Requests.Clear();
                Producer2Positions.Clear();
                Producer2Give.Clear();
            }
        }

        // Добавляем пул контекстов
        private readonly ConcurrentBag<NetworkProcessingContext> _contextPool = [];

        // Метод для получения контекста из пула
        private NetworkProcessingContext GetContext()
        {
            if (_contextPool.TryTake(out var context))
            {
                context.Clear();
                return context;
            }
            return new NetworkProcessingContext();
        }

        // Метод для возврата контекста в пул
        private void ReturnContext(NetworkProcessingContext context)
        {
            _contextPool.Add(context);
        }



        private void ProcessNetwork(Network network, NetworkProcessingContext context)
        {
            // Этап 1: Очищаем локальные переменные цикла ----------------------------------------------------------------------------

            if (network == null)
                return;

            // Этап 2: Сбор запросов от потребителей----------------------------------------------------------------------------
            var cons = network.Consumers.Count; // Количество потребителей в сети
            float requestedEnergy; // Запрошенная энергия от потребителей


            foreach (var electricConsumer in network.Consumers)
            {
                if (network.PartPositions.Contains(electricConsumer.Pos) // Проверяем, что потребитель находится в части сети
                    && Parts[electricConsumer.Pos].IsLoaded              // Проверяем, что потребитель загружен
                    && electricConsumer.Consume_request() > 0)             // Проверяем, что потребитель запрашивает энергию вообще
                {
                    context.LocalConsumers.Add(new Consumer(electricConsumer));

                    // Если включена компенсация потерь
                    if (enableLossCompensation)
                    {
                        var received = electricConsumer.getPowerReceive();
                        var requested = electricConsumer.Consume_request();

                        // если ниже нуля, то ставим 1.0
                        if (electricConsumer.AvgConsumeCoeff < 1.0f)
                            electricConsumer.AvgConsumeCoeff = 1.0f;

                        var smoothedCoeff = electricConsumer.AvgConsumeCoeff;


                        // если запрошено больше, чем получено, то увеличиваем коэффициент сглаживания
                        if (requested > received)
                        {
                            if (smoothedCoeff < 2.0)
                            {
                                smoothedCoeff += 0.1f;
                                smoothedCoeff = CalculateEma(0.05f, smoothedCoeff, electricConsumer.AvgConsumeCoeff);
                            }
                        }
                        else
                        {
                            if (smoothedCoeff > 1.0)
                            {
                                smoothedCoeff -= 0.1f;
                                smoothedCoeff = CalculateEma(0.05f, smoothedCoeff, electricConsumer.AvgConsumeCoeff);
                            }
                        }


                        requestedEnergy = requested * smoothedCoeff; // Запрашиваем с учётом сглаженного коэффициента
                        electricConsumer.AvgConsumeCoeff = smoothedCoeff;  // Храним сглаженный coeff (не энергию!)
                    }
                    else
                    {
                        requestedEnergy = electricConsumer.Consume_request();
                    }


                    context.ConsumerPositions.Add(electricConsumer.Pos);
                    context.ConsumerRequests.Add(requestedEnergy);
                }
            }

            // Этап 3: Сбор энергии с генераторов и аккумуляторов----------------------------------------------------------------------------
            var prod = network.Producers.Count + network.Accumulators.Count; // Количество производителей в сети
            float giveEnergy; // Энергия, которую отдают производители

            foreach (var electricProducer in network.Producers)
            {
                if (network.PartPositions.Contains(electricProducer.Pos) // Проверяем, что генератор находится в части сети
                    && Parts[electricProducer.Pos].IsLoaded              // Проверяем, что генератор загружен
                    && electricProducer.Produce_give() > 0)                // Проверяем, что генератор отдает энергию вообще
                {
                    context.LocalProducers.Add(new Producer(electricProducer));
                    giveEnergy = electricProducer.Produce_give();
                    context.ProducerPositions.Add(electricProducer.Pos);
                    context.ProducerGive.Add(giveEnergy);

                }
            }

            foreach (var electricAccum in network.Accumulators)
            {
                if (network.PartPositions.Contains(electricAccum.Pos)   // Проверяем, что аккумулятор находится в части сети
                    && Parts[electricAccum.Pos].IsLoaded                // Проверяем, что аккумулятор загружен
                    && electricAccum.canRelease() > 0)                    // Проверяем, что аккумулятор может отдать энергию вообще
                {
                    context.LocalAccums.Add(new Accumulator(electricAccum));
                    giveEnergy = electricAccum.canRelease();
                    context.ProducerPositions.Add(electricAccum.Pos);
                    context.ProducerGive.Add(giveEnergy);

                }
            }

            // Этап 4: Распределение энергии ----------------------------------------------------------------------------
            LogisticalTask(network, context.ConsumerPositions, context.ConsumerRequests, context.ProducerPositions, context.ProducerGive, context.Sim);



            EnergyPacket packet;   // Временная переменная для пакета энергии
            //BlockPos posStore; // Позиция магазина в мире
            //BlockPos posCustomer; // Позиция потребителя в мире
            var customCount = context.ConsumerPositions.Count; // Количество клиентов в симуляции
            var storeCount = context.ProducerPositions.Count; // Количество магазинов в симуляции
            var k = 0;
            for (var i = 0; i < customCount; i++)
            {
                for (k = 0; k < storeCount; k++)
                {
                    var value = context.Sim.Customers![i].Received[context.Sim.Stores![k].Id];
                    if (value > 0)
                    {

                        // Проверяем, что пути и направления не равны null
                        if (context.Sim.Path[i * storeCount + k] == null)
                            continue;

                        // создаём пакет, не копируя ничего
                        packet = new EnergyPacket(
                            value,
                            context.Sim.Voltage[i * storeCount + k],
                            context.Sim.Path[i * storeCount + k].Length - 1,
                            context.Sim.Path[i * storeCount + k],
                            context.Sim.FacingFrom[i * storeCount + k],
                            context.Sim.NowProcessedFaces[i * storeCount + k],
                            context.Sim.UsedConnection[i * storeCount + k]
                        );


                        // Добавляем пакет в глобальный список
                        context.LocalPackets.Add(packet);


                    }


                }
            }







            // Этап 5: Забираем у аккумуляторов выданное----------------------------------------------------------------------------
            var consIter = 0; // Итератор
            foreach (var accum in context.LocalAccums)
            {
                if (context.Sim.Stores![consIter + context.LocalProducers.Count].Stock < accum.ElectricAccum.canRelease())
                {
                    accum.ElectricAccum.Release(accum.ElectricAccum.canRelease() -
                                                context.Sim.Stores[consIter + context.LocalProducers.Count].Stock);
                }

                consIter++;
            }


            // Этап 6: Зарядка аккумуляторов    ----------------------------------------------------------------------------
            cons = network.Accumulators.Count; // Количество аккумов в сети

            context.LocalAccums.Clear();
            foreach (var electricAccum in network.Accumulators)
            {
                if (network.PartPositions.Contains(electricAccum.Pos)   // Проверяем, что аккумулятор находится в части сети
                    && Parts[electricAccum.Pos].IsLoaded)                // Проверяем, что аккумулятор загружен
                                                                         // Проверяем, что аккумулятор может отдать энергию вообще
                {
                    context.LocalAccums.Add(new Accumulator(electricAccum));
                    requestedEnergy = electricAccum.canStore();

                    context.Consumer2Positions.Add(electricAccum.Pos);
                    context.Consumer2Requests.Add(requestedEnergy);
                }
            }





            // Этап 7: Остатки генераторов  ----------------------------------------------------------------------------
            prod = context.LocalProducers.Count; // Количество производителей в сети
            var prodIter = 0; // Итератор для производителей


            foreach (var producer in context.LocalProducers)
            {
                giveEnergy = context.Sim.Stores![prodIter].Stock;
                context.Producer2Positions.Add(producer.ElectricProducer.Pos);
                context.Producer2Give.Add(giveEnergy);
                prodIter++;
            }


            // Этап 8: Распределение энергии для аккумуляторов ----------------------------------------------------------------------------
            LogisticalTask(network, context.Consumer2Positions, context.Consumer2Requests, context.Producer2Positions, context.Producer2Give, context.Sim2);


            customCount = context.Consumer2Positions.Count; // Количество клиентов в симуляции 2
            storeCount = context.Producer2Positions.Count; // Количество магазинов в симуляции 2

            for (var i = 0; i < customCount; i++)
            {
                for (k = 0; k < storeCount; k++)
                {
                    var value = context.Sim2.Customers![i].Received[context.Sim2.Stores![k].Id];
                    if (value > 0)
                    {
                        // Проверяем, что пути и направления не равны null
                        if (context.Sim2.Path[i * storeCount + k] == null)
                            continue;

                        // создаём пакет, не копируя ничего
                        packet = new EnergyPacket(
                            value,
                            context.Sim2.Voltage[i * storeCount + k],
                            context.Sim2.Path[i * storeCount + k].Length - 1,
                            context.Sim2.Path[i * storeCount + k],
                            context.Sim2.FacingFrom[i * storeCount + k],
                            context.Sim2.NowProcessedFaces[i * storeCount + k],
                            context.Sim2.UsedConnection[i * storeCount + k]
                        );


                        // Добавляем пакет в глобальный список
                        context.LocalPackets.Add(packet);


                    }
                }
            }





            // Этап 9: Сообщение генераторам о нагрузке ----------------------------------------------------------------------------
            var j = 0;
            foreach (var producer in context.LocalProducers)
            {
                var totalOrder = context.Sim.Stores![j].TotalRequest + context.Sim2.Stores![j].TotalRequest;
                producer.ElectricProducer.Produce_order(totalOrder);
                j++;
            }



            // Обновляем инфу об электрических цепях
            UpdateNetworkInfo(network);


        }




        /// <summary>
        /// Тикаем сервер
        /// </summary>
        /// <param name="deltaTime"></param>
        private void OnGameTickServer(float deltaTime)
        {
            if (_sapi == null)
                return;

            // Очистка старых путей
            if (_sapi.World.Rand.NextDouble() < 0.01d)
            {
                PathCacheManager.Cleanup();
            }

            Cleaner();

            // Очищаем результаты предыдущего тика
            _networkResults.Clear();

            // Сбрасываем CountdownEvent на количество сетей
            _networkProcessingCompleted.Reset(Networks.Count);

            // Добавляем все сети в очередь обработки
            foreach (var network in Networks)
            {
                _networkProcessingQueue.Add(network);
            }

            // Ждем завершения обработки всех сетей
            _networkProcessingCompleted.Wait();

            // Собираем результаты
            foreach (var packets in _networkResults)
            {
                _globalEnergyPackets.AddRange(packets);
            }

            // Обновление электрических компонентов
            _elapsedMs += deltaTime;
            UpdateNetworkComponents();




            // Этап 11: Потребление энергии пакетами и Этап 12: Перемещение пакетов-----------------------------------------------
            ConsumeAndMovePackets();




            foreach (var pair in _sumEnergy)
            {
                if (Parts.TryGetValue(pair.Key, out var parta))
                {
                    if (parta.Consumer != null)
                        parta.Consumer!.Consume_receive(pair.Value);
                    else if (parta.Accumulator != null)
                        parta.Accumulator!.Store(pair.Value);
                }
                else
                {
                    _sumEnergy.Remove(pair.Key); // Удаляем, если части сети этой уже нет
                }
            }




            // Этап 13: Проверка сгорания проводов и трансформаторов ----------------------------------------------------------------------------

            var bAccessor = _sapi!.World.BlockAccessor; // аксессор для блоков
            BlockPos partPos;                        // Временная переменная для позиции части сети
            NetworkPart part;                        // Временная переменная для части сети
            bool updated;                            // Флаг обновления части сети от повреждения
            EParams faceParams;                      // Параметры грани сети
            int lastFaceIndex;                       // Индекс последней грани в пакете
            float totalEnergy;                       // Суммарная энергия в трансформаторе
            float totalCurrent;                      // Суммарный ток в трансформаторе
            var kons = 0;

            foreach (var partEntry in Parts)
            {
                partPos = partEntry.Key;
                part = partEntry.Value;

                //обновляем каждый блок сети
                updated = kons % 20 == _envUpdater &&
                          part.IsLoaded &&          // блок загружен?
                          (damageManager?.DamageByEnvironment(this._sapi, ref part, ref bAccessor) ?? false);
                kons++;


                if (updated)
                {
                    for (var faceIndex = 0; faceIndex < 6; faceIndex++)
                    {
                        faceParams = part.eparams[faceIndex];
                        if (faceParams.voltage == 0 || !faceParams.burnout)
                            continue;

                        ResetComponents(ref part); // сброс компонентов сети
                    }

                }



                var bufPartTrans = part.Transformator;
                // Обработка трансформаторов
                if (bufPartTrans != null)
                {
                    totalEnergy = 0f;
                    totalCurrent = 0f;

                    foreach (var packet2 in part.packets)
                    {

                        totalEnergy += packet2.energy;
                        totalCurrent += packet2.energy / packet2.voltage;


                        if (packet2.voltage == bufPartTrans.HighVoltage)
                            packet2.voltage = bufPartTrans.LowVoltage;
                        else if (packet2.voltage == bufPartTrans.LowVoltage)
                            packet2.voltage = bufPartTrans.HighVoltage;

                    }


                    var transformatorFaceIndex =
                        FacingHelper.GetFaceIndex(
                            FacingHelper.FromFace(FacingHelper.Faces(part.Connection)
                                .First())); // Индекс грани трансформатора!

                    part.eparams[transformatorFaceIndex].current = totalCurrent;

                    bufPartTrans.setPower(totalEnergy);

                }


                // Проверка на превышение напряжения
                foreach (var packet2 in part.packets)
                {
                    lastFaceIndex = packet2.facingFrom[packet2.currentIndex];

                    faceParams = part.eparams[lastFaceIndex];
                    if (faceParams.voltage != 0 && packet2.voltage > faceParams.voltage)
                    {
                        part.eparams[lastFaceIndex].prepareForBurnout(2);

                        if (packet2.path[packet2.currentIndex] == partPos)
                            packet2.shouldBeRemoved = true;


                        ResetComponents(ref part);
                        //break;
                    }
                }





                // Проверка на превышение тока
                for (var faceIndex = 0; faceIndex < 6; faceIndex++)
                {

                    faceParams = part.eparams[faceIndex];
                    if (faceParams.voltage == 0 ||
                        Math.Abs(faceParams.current) <= faceParams.maxCurrent * faceParams.lines)
                        continue;

                    part.eparams[faceIndex].prepareForBurnout(1);


                    foreach (var p in _globalEnergyPackets)
                    {
                        if (p.path[p.currentIndex] == partPos &&
                            p.nowProcessedFaces.LastOrDefault()?[faceIndex] == true)
                        {
                            p.shouldBeRemoved = true;
                        }
                    }

                    ResetComponents(ref part);
                }
            }


            _envUpdater++;
            if (_envUpdater > 19)
                _envUpdater = 0;



            //Удаление ненужных пакетов
            _globalEnergyPackets.RemoveAll(p => p.shouldBeRemoved);


        }




        /// <summary>
        /// Потребление и перемещение пакетов энергии
        /// </summary>
        private void ConsumeAndMovePackets()
        {
            BlockPos pos;                   // Временная переменная для позиции
            float resistance, current, lossEnergy;  // Переменные для расчета сопротивления, тока и потерь энергии                    
            int curIndex, currentFacingFrom;        // текущий индекс и направление в пакете
            BlockPos currentPos;           // текущая и следующая позиции в пути пакета
            //NetworkPart currentPart;      // Временные переменные для частей сети



            // Заполняем списки пакетов по позициям
            foreach (var packet in _globalEnergyPackets)
            {
                pos = packet.path[packet.currentIndex];
                if (Parts.TryGetValue(pos, out var partValue))
                {
                    if (partValue.packets == null)
                        partValue.packets = [];
                    else
                    {
                        partValue.packets.Add(packet);
                    }
                }
                else // чтобы не застревали в частях сети, которые перестали существовать
                {
                    packet.shouldBeRemoved = true; 
                }

            }


            foreach (var partValue in Parts.Values)
            {
                // если пакетов нет тут, то пропускаем
                if (partValue.packets == null || partValue.packets.Count == 0)
                    continue;

                int deltaX, deltaY, deltaZ;

                foreach (var packet in partValue.packets)
                {
                    curIndex = packet.currentIndex; //текущий индекс в пакете

                    if (curIndex == 0)
                    {
                        pos = packet.path[0];

                        if (Parts.TryGetValue(pos, out var part2))
                        {
                            var isValid = false;
                            // Ручная проверка условий 
                            foreach (var s in part2.eparams)
                            {
                                if (s.voltage > 0
                                    && !s.burnout
                                    && packet.voltage >= s.voltage)
                                {
                                    isValid = true;
                                    break;
                                }
                            }

                            if (isValid)
                            {
                                if (!_sumEnergy.TryAdd(pos, packet.energy))
                                {
                                    _sumEnergy[pos] += packet.energy;
                                }
                            }
                        }

                        packet.shouldBeRemoved = true;
                    }
                    else
                    {
                        currentPos = packet.path[curIndex]; // текущая позиция в пути пакета
                        currentFacingFrom = packet.facingFrom[curIndex]; // текущая грань, с которой пришел пакет

                        if (!partValue.eparams[packet.facingFrom[curIndex]].burnout) //проверяем не сгорела ли грань в след блоке
                        {
                            if ((partValue.Connection & packet.usedConnections[curIndex]) == packet.usedConnections[curIndex]) // проверяем совпадает ли путь в пакете с путем в части сети
                            {
                                // считаем сопротивление
                                resistance = ElectricalProgressive.energyLossFactor *
                                    partValue.eparams[currentFacingFrom].resistivity /
                                             (partValue.eparams[currentFacingFrom].lines *
                                              partValue.eparams[currentFacingFrom].crossArea);

                                // Провод в изоляции теряет меньше энергии
                                if (partValue.eparams[currentFacingFrom].isolated)
                                    resistance /= 2.0f;

                                // считаем ток по закону Ома
                                current = packet.energy / packet.voltage;

                                // считаем потерю энергии по закону Джоуля
                                lossEnergy = current * current * resistance;
                                packet.energy = Math.Max(packet.energy - lossEnergy, 0);

                                // пересчитаем ток уже с учетом потерь
                                current = packet.energy / packet.voltage;



                                // далее учитываем правило алгебраического сложения встречных токов
                                // 1) Определяем вектор движения
                                var nextPos = packet.path[curIndex - 1];

                                var sign = true;

                                deltaX = nextPos.X - currentPos.X;
                                deltaY = nextPos.InternalY - currentPos.InternalY;
                                deltaZ = nextPos.Z - currentPos.Z;


                                if (deltaX < 0) sign = !sign;
                                if (deltaY < 0) sign = !sign;
                                if (deltaZ < 0) sign = !sign;

                                // 2) Прописываем токи на нужные грани
                                var j = 0;
                                foreach (var face in packet.nowProcessedFaces[packet.currentIndex])
                                {
                                    if (face)
                                    {
                                        if (sign)
                                            partValue.eparams[j].current += current; // добавляем ток в следующую часть сети
                                        else
                                            partValue.eparams[j].current -= current; // добавляем ток в следующую часть сети
                                    }

                                    j++;
                                }

                                // 3) Если энергия пакета почти нулевая — удаляем пакет
                                if (packet.energy <= 0.001f)
                                {
                                    packet.shouldBeRemoved = true;
                                }

                                // переходим к следующему блоку в пути
                                packet.currentIndex--;

                            }
                            else
                            {
                                // если все же путь не совпадает с путем в пакете, то чистим кэши
                                PathCacheManager.RemoveAll(packet.path[0], packet.path.Last());
                                packet.shouldBeRemoved = true;

                            }
                        }
                        else
                        {
                            packet.shouldBeRemoved = true;
                        }

                    }
                }
            }


            //Удаление ненужных пакетов
            _globalEnergyPackets.RemoveAll(p => p.shouldBeRemoved);

        }





        /// <summary>
        /// Обновление информации о сети
        /// </summary>
        /// <param name="network"></param>
        private void UpdateNetworkInfo(Network network)
        {
            // расчет емкости
            var capacity = 0f; // Суммарная емкость сети
            var maxCapacity = 0f; // Максимальная емкость сети

            foreach (var electricAccum in network.Accumulators)
            {
                if (network.PartPositions.Contains(electricAccum.Pos)   // Проверяем, что аккумулятор находится в части сети
                    && Parts[electricAccum.Pos].IsLoaded)               // Проверяем, что аккумулятор загружен
                                                                        // Проверяем, что аккумулятор может отдать энергию вообще
                {
                    capacity += electricAccum.GetCapacity();
                    maxCapacity += electricAccum.GetMaxCapacity();
                }


            }

            network.Capacity = capacity;
            network.MaxCapacity = maxCapacity;



            // Расчет производства (чистая генерация генераторами)
            var production = 0f;
            foreach (var electricProducer in network.Producers)
            {
                if (network.PartPositions.Contains(electricProducer.Pos)    // Проверяем, что генератор находится в части сети
                    && Parts[electricProducer.Pos].IsLoaded)                // Проверяем, что генератор загружен
                {
                    production += Math.Min(electricProducer.getPowerGive(), electricProducer.getPowerOrder());
                }
            }

            network.Production = production;


            // Расчет необходимой энергии для потребителей!
            var requestSum = 0f;
            foreach (var electricConsumer in network.Consumers)
            {
                if (network.PartPositions.Contains(electricConsumer.Pos) // Проверяем, что потребитель находится в части сети
                    && Parts[electricConsumer.Pos].IsLoaded) // Проверяем, что потребитель загружен
                {
                    requestSum += electricConsumer.getPowerRequest();
                }
            }

            network.Request = Math.Max(requestSum, 0f);


            // Расчет потребления (только потребителями)
            var consumption = 0f;

            // потребление в первой симуляции
            foreach (var electricConsumer in network.Consumers)
            {
                if (network.PartPositions.Contains(electricConsumer.Pos) // Проверяем, что потребитель находится в части сети
                    && Parts[electricConsumer.Pos].IsLoaded) // Проверяем, что потребитель загружен
                {
                    consumption += electricConsumer.getPowerReceive();
                }
            }


            network.Consumption = consumption;
        }




        // Вынесенный метод сброса компонентов
        private static void ResetComponents(ref NetworkPart part)
        {
            part.Consumer?.Consume_receive(0f);
            part.Producer?.Produce_order(0f);
            part.Accumulator?.SetCapacity(0f);
            part.Transformator?.setPower(0f);
        }




        /// <summary>
        /// Объединение цепей
        /// </summary>
        /// <param name="networks"></param>
        /// <returns></returns>
        private Network MergeNetworks(HashSet<Network> networks)
        {
            Network? outNetwork = null;

            foreach (var network in networks)
            {
                if (outNetwork == null || outNetwork.PartPositions.Count < network.PartPositions.Count)
                {
                    outNetwork = network;
                }
            }

            if (outNetwork != null)
            {
                foreach (var network in networks)
                {
                    if (outNetwork == network)
                    {
                        continue;
                    }

                    foreach (var position in network.PartPositions)
                    {
                        var part = this.Parts[position];
                        foreach (var face in BlockFacing.ALLFACES)
                        {
                            if (part.Networks[face.Index] == network)
                            {
                                part.Networks[face.Index] = outNetwork;
                            }
                        }

                        if (part.Conductor is { } conductor) outNetwork.Conductors.Add(conductor);
                        if (part.Consumer is { } consumer) outNetwork.Consumers.Add(consumer);
                        if (part.Producer is { } producer) outNetwork.Producers.Add(producer);
                        if (part.Accumulator is { } accumulator) outNetwork.Accumulators.Add(accumulator);
                        if (part.Transformator is { } transformator) outNetwork.Transformators.Add(transformator);

                        outNetwork.PartPositions.Add(position);
                    }

                    network.PartPositions.Clear();
                    this.Networks.Remove(network);
                }
            }

            outNetwork ??= this.CreateNetwork();

            return outNetwork;
        }



        /// <summary>
        /// Удаляем сеть
        /// </summary>
        /// <param name="network"></param>
        private void RemoveNetwork(ref Network network)
        {
            var partPositions = new BlockPos[network.PartPositions.Count];
            network.PartPositions.CopyTo(partPositions);
            network.version++;
            this.Networks.Remove(network);                                  //удаляем цепь из списка цепей

            foreach (var position in partPositions)                         //перебираем по всем бывшим элементам этой цепи
            {
                if (this.Parts.TryGetValue(position, out var part))         //есть такое соединение?
                {
                    foreach (var face in BlockFacing.ALLFACES)              //перебираем по всем 6 направлениям
                    {
                        if (part.Networks[face.Index] == network)           //если нашли привязку к этой цепи
                        {
                            part.Networks[face.Index] = null;               //обнуляем ее
                        }
                    }
                }
            }

            foreach (var position in partPositions)                                 //перебираем по всем бывшим элементам этой цепи
            {
                if (this.Parts.TryGetValue(position, out var part))                 //есть такое соединение?
                {
                    this.AddConnections(ref part, part.Connection, (new EParams(), 0));     //добавляем соединения???
                }
            }
        }


        /// <summary>
        /// Создаем новую цепь
        /// </summary>
        /// <returns></returns>
        private Network CreateNetwork()
        {
            var network = new Network();
            this.Networks.Add(network);

            return network;
        }


        /// <summary>
        /// Добавляем соединения
        /// </summary>
        /// <param name="part"></param>
        /// <param name="addedConnections"></param>
        /// <param name="setEparams"></param>
        /// <exception cref="Exception"></exception>
        private void AddConnections(ref NetworkPart part, Facing addedConnections, (EParams, int) setEparams)
        {
            HashSet<Network>[] networksByFace =
            [
                [], [], [], [], [],
                []
            ];

            foreach (var face in FacingHelper.Faces(part.Connection))           //ищет к каким сетям эти провода могут относиться
            {
                networksByFace[face.Index].Add(part.Networks[face.Index] ?? this.CreateNetwork());
            }


            //поиск соседей по граням
            foreach (var direction in FacingHelper.Directions(addedConnections))
            {
                var directionFilter = FacingHelper.FromDirection(direction);
                var neighborPosition = part.Position.AddCopy(direction);

                if (this.Parts.TryGetValue(neighborPosition, out var neighborPart))         //проверяет, если в той стороне сосед
                {
                    foreach (var face in FacingHelper.Faces(addedConnections & directionFilter))
                    {
                        // 1) Соединение своей грани face с противоположной гранью соседа
                        if ((neighborPart.Connection & FacingHelper.From(face, direction.Opposite)) != 0)
                        {
                            if (neighborPart.Networks[face.Index] is { } network)
                            {
                                networksByFace[face.Index].Add(network);
                            }
                        }

                        // 2) Тоже, но наоборот
                        if ((neighborPart.Connection & FacingHelper.From(direction.Opposite, face)) != 0)
                        {
                            if (neighborPart.Networks[direction.Opposite.Index] is { } network)
                            {
                                networksByFace[face.Index].Add(network);
                            }
                        }
                    }
                }

                //поиск соседей по ребрам
                directionFilter = FacingHelper.FromDirection(direction);

                foreach (var face in FacingHelper.Faces(addedConnections & directionFilter))
                {
                    neighborPosition = part.Position.AddCopy(direction).AddCopy(face);

                    if (this.Parts.TryGetValue(neighborPosition, out neighborPart))
                    {
                        // 1) Проверяем соединение через ребро direction–face
                        if ((neighborPart.Connection & FacingHelper.From(direction.Opposite, face.Opposite)) != 0)
                        {
                            if (neighborPart.Networks[direction.Opposite.Index] is { } network)
                            {
                                networksByFace[face.Index].Add(network);
                            }
                        }

                        // 2) Тоже, но наоборот
                        if ((neighborPart.Connection & FacingHelper.From(face.Opposite, direction.Opposite)) != 0)
                        {
                            if (neighborPart.Networks[face.Opposite.Index] is { } network)
                            {
                                networksByFace[face.Index].Add(network);
                            }
                        }
                    }
                }


                // ищем соседей по перпендикулярной грани
                directionFilter = FacingHelper.FromDirection(direction);

                foreach (var face in FacingHelper.Faces(addedConnections & directionFilter))
                {
                    neighborPosition = part.Position.AddCopy(face);

                    if (this.Parts.TryGetValue(neighborPosition, out neighborPart))
                    {
                        // 1) Проверяем перпендикулярную грань соседа
                        if ((neighborPart.Connection & FacingHelper.From(direction, face.Opposite)) != 0)
                        {
                            if (neighborPart.Networks[direction.Index] is { } network)
                            {
                                networksByFace[face.Index].Add(network);
                            }
                        }

                        // 2) Тоже, но наоборот
                        if ((neighborPart.Connection & FacingHelper.From(face.Opposite, direction)) != 0)
                        {
                            if (neighborPart.Networks[face.Opposite.Index] is { } network)
                            {
                                networksByFace[face.Index].Add(network);
                            }
                        }

                    }
                }
            }








            foreach (var face in FacingHelper.Faces(part.Connection))
            {
                var network = this.MergeNetworks(networksByFace[face.Index]);

                if (part.Conductor is { } conductor)
                {
                    network.Conductors.Add(conductor);
                }

                if (part.Consumer is { } consumer)
                {
                    network.Consumers.Add(consumer);
                }

                if (part.Producer is { } producer)
                {
                    network.Producers.Add(producer);
                }

                if (part.Accumulator is { } accumulator)
                {
                    network.Accumulators.Add(accumulator);
                }

                if (part.Transformator is { } transformator)
                {
                    network.Transformators.Add(transformator);
                }

                network.PartPositions.Add(part.Position);
                network.version++; // Увеличиваем версию сети 

                part.Networks[face.Index] = network;            //присваиваем в этой точке эту цепь

                var i = 0;
                if (part.eparams == null)
                {
                    part.eparams =
                    [
                        new EParams(),
                        new EParams(),
                        new EParams(),
                        new EParams(),
                        new EParams(),
                        new EParams()
                    ];
                }

                foreach (var ams in part.eparams)
                {
                    if (ams.Equals(new EParams()))
                        part.eparams[i] = new EParams();
                    i++;
                }

                if (!setEparams.Item1.Equals(new EParams()) && part.eparams[face.Index].maxCurrent == 0)
                    part.eparams[face.Index] = setEparams.Item1;      //аналогично с параметрами электричества
            }





            foreach (var direction in FacingHelper.Directions(part.Connection))
            {
                var directionFilter = FacingHelper.FromDirection(direction);

                foreach (var face in FacingHelper.Faces(part.Connection & directionFilter))
                {
                    if ((part.Connection & FacingHelper.From(direction, face)) != 0)
                    {
                        if (part.Networks[face.Index] is { } network1 && part.Networks[direction.Index] is { } network2)
                        {
                            var networks = new HashSet<Network>
                        {
                            network1, network2
                        };

                            this.MergeNetworks(networks);
                        }
                        else
                        {
                            throw new Exception();
                        }
                    }
                }
            }


        }



        /// <summary>
        /// Удаляем соединения
        /// </summary>
        /// <param name="part"></param>
        /// <param name="removedConnections"></param>
        private void RemoveConnections(ref NetworkPart part, Facing removedConnections)
        {
            foreach (var blockFacing in FacingHelper.Faces(removedConnections))
            {
                if (part.Networks[blockFacing.Index] is { } network)
                {
                    this.RemoveNetwork(ref network);
                    network.version++; // Увеличиваем версию сети после удаления
                }
            }
        }



        /// <summary>
        /// Задать проводник
        /// </summary>
        /// <param name="position"></param>
        /// <param name="conductor"></param>
        public void SetConductor(BlockPos position, IElectricConductor? conductor) =>
        SetComponent(
            position,
            conductor,
            part => part.Conductor,
            (part, c) => part.Conductor = c,
            network => network.Conductors);




        /// <summary>
        /// Задать потребителя
        /// </summary>
        /// <param name="position"></param>
        /// <param name="consumer"></param>
        public void SetConsumer(BlockPos position, IElectricConsumer? consumer) =>
        SetComponent(
            position,
            consumer,
            part => part.Consumer,
            (part, c) => part.Consumer = c,
            network => network.Consumers);


        /// <summary>
        /// Задать генератор
        /// </summary>
        /// <param name="position"></param>
        /// <param name="producer"></param>
        public void SetProducer(BlockPos position, IElectricProducer? producer) =>
            SetComponent(
                position,
                producer,
                part => part.Producer,
                (part, p) => part.Producer = p,
                network => network.Producers);


        /// <summary>
        /// Задать аккумулятор
        /// </summary>
        /// <param name="position"></param>
        /// <param name="accumulator"></param>
        public void SetAccumulator(BlockPos position, IElectricAccumulator? accumulator) =>
            SetComponent(
                position,
                accumulator,
                part => part.Accumulator,
                (part, a) => part.Accumulator = a,
                network => network.Accumulators);


        /// <summary>
        /// Задать трансформатор
        /// </summary>
        /// <param name="position"></param>
        /// <param name="transformator"></param>
        public void SetTransformator(BlockPos position, IElectricTransformator? transformator) =>
            SetComponent(
                position,
                transformator,
                part => part.Transformator,
                (part, a) => part.Transformator = a,
                network => network.Transformators);


        /// <summary>
        /// Задает компоненты разных типов
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="position"></param>
        /// <param name="newComponent"></param>
        /// <param name="getComponent"></param>
        /// <param name="setComponent"></param>
        /// <param name="getCollection"></param>
        private void SetComponent<T>(
            BlockPos position,
            T? newComponent,
            System.Func<NetworkPart, T?> getComponent,
            Action<NetworkPart, T?> setComponent,
            System.Func<Network, ICollection<T>> getCollection)
            where T : class
        {
            if (!this.Parts.TryGetValue(position, out var part))
            {
                if (newComponent == null)
                {
                    return;
                }

                part = this.Parts[position] = new NetworkPart(position);
            }

            var oldComponent = getComponent(part);
            if (oldComponent != newComponent)
            {
                foreach (var network in part.Networks)
                {
                    if (network is null) continue;

                    var collection = getCollection(network);

                    if (oldComponent != null)
                    {
                        collection.Remove(oldComponent);
                    }

                    if (newComponent != null)
                    {
                        collection.Add(newComponent);
                    }
                }

                setComponent(part, newComponent);
            }
        }





        /// <summary>
        /// Cобирает информацию по цепи
        /// </summary>
        /// <param name="position"></param>
        /// <param name="facing"></param>
        /// <param name="method">Метод вывода с какой грани "thisFace"- эту грань, "firstFace"- информация о первой грани из многих, "currentFace" - информация о грани, в которой ток больше 0</param>
        /// <returns></returns>
        public NetworkInformation GetNetworks(BlockPos position, Facing facing, string method = "thisFace")
        {
            _result.Reset(); // сбрасываем значения

            // такое редко, но может произойти, поэтому сбрасываем и выходим
            if (facing == Facing.None)
                return _result;

            if (this.Parts.TryGetValue(position, out var part))
            {
                if (method == "thisFace" || method == "firstFace") // пока так, возможно потом по-разному будет обработка
                {
                    var blockFacing = FacingHelper.Faces(facing).First();

                    if (part.Networks[blockFacing.Index] is { } net)
                    {
                        _localNetwork = net;                                              //выдаем найденную цепь
                        _result.Facing |= FacingHelper.FromFace(blockFacing);            //выдаем ее направления
                        _result.eParamsInNetwork = part.eparams[blockFacing.Index];      //выдаем ее текущие параметры
                        _result.current = part.eparams[blockFacing.Index].current;           //выдаем текущий ток в этой грани
                    }
                    else
                        return _result;
                }
                else if (method == "currentFace") // если ток больше нуля, то выдаем информацию о грани, в которой ток больше нуля
                {
                    var searchIndex = 0;
                    BlockFacing blockFacing = null!;

                    foreach (var blockFacing2 in FacingHelper.Faces(facing))
                    {
                        if (part.Networks[blockFacing2.Index] is not null &&
                            Math.Abs(part.eparams[blockFacing2.Index].current) > 0.0F)
                        {
                            blockFacing = blockFacing2;
                            searchIndex = blockFacing2.Index;
                        }
                    }

                    if (part.Networks[searchIndex] is { } net)
                    {
                        _localNetwork = net;                                              //выдаем найденную цепь
                        _result.Facing |= FacingHelper.FromFace(blockFacing);        //выдаем ее направления
                        _result.eParamsInNetwork = part.eparams[searchIndex];  //выдаем ее текущие параметры
                        _result.current = part.eparams[searchIndex].current;           //выдаем текущий ток в этой грани
                    }
                    else
                        return _result;
                }




                // Если нашли сеть, то заполняем информацию о ней
                _result.NumberOfBlocks = _localNetwork.PartPositions.Count;
                _result.NumberOfConsumers = _localNetwork.Consumers.Count;
                _result.NumberOfProducers = _localNetwork.Producers.Count;
                _result.NumberOfAccumulators = _localNetwork.Accumulators.Count;
                _result.NumberOfTransformators = _localNetwork.Transformators.Count;
                _result.Production = _localNetwork.Production;
                _result.Consumption = _localNetwork.Consumption;
                _result.Capacity = _localNetwork.Capacity;
                _result.MaxCapacity = _localNetwork.MaxCapacity;
                _result.Request = _localNetwork.Request;

            }

            return _result;
        }


        /// <summary>
        /// Вычисление экспоненциального скользящего среднего (EMA) на лету
        /// </summary>
        /// <param name="alpha"></param>
        /// <param name="currentValue"></param>
        /// <param name="previousSmoothedValue"></param>
        /// <returns></returns>
        public static float CalculateEma(float alpha, float currentValue, float previousSmoothedValue)
        {
            return alpha * currentValue + (1 - alpha) * previousSmoothedValue;
        }

    }


    /// <summary>
    /// Проводник тока
    /// </summary>
    internal class Conductor
    {
        public readonly IElectricConductor ElectricConductor;
        public Conductor(IElectricConductor electricConductor) => ElectricConductor = electricConductor;
    }


    /// <summary>
    /// Потребитель
    /// </summary>
    internal class Consumer
    {
        public readonly IElectricConsumer ElectricConsumer;
        public Consumer(IElectricConsumer electricConsumer) => ElectricConsumer = electricConsumer;
    }


    /// <summary>
    /// Трансформатор
    /// </summary>
    internal class Transformator
    {
        public readonly IElectricTransformator ElectricTransformator;
        public Transformator(IElectricTransformator electricTransformator) => ElectricTransformator = electricTransformator;
    }


    /// <summary>
    /// Генератор
    /// </summary>
    internal class Producer
    {
        public readonly IElectricProducer ElectricProducer;
        public Producer(IElectricProducer electricProducer) => ElectricProducer = electricProducer;
    }


    /// <summary>
    /// Аккумулятор
    /// </summary>
    internal class Accumulator
    {
        public readonly IElectricAccumulator ElectricAccum;
        public Accumulator(IElectricAccumulator electricAccum) => ElectricAccum = electricAccum;
    }




    /// <summary>
    /// Конфигуратор сети
    /// </summary>
    public class ElectricityConfig
    {
        public int SpeedOfElectricity = 8;
        public int TimeBeforeBurnout = 30;
        public int MultiThreading = 4;
        public int CacheTimeoutCleanupMinutes = 2;
        public int MaxDistanceForFinding = 200;
        public float EnergyLossFactor = 1.0f;
        public bool EnableLossCompensation = false;
    }

    /// <summary>
    /// Кэш путей
    /// </summary>
    public struct PathCacheEntry
    {
        public BlockPos[]? Path;
        public int[]? FacingFrom;
        public bool[][]? NowProcessedFaces;
        public Facing[]? usedConnections;
        public int Version;
    }



    public class BlockPosComparer : IEqualityComparer<BlockPos>
    {
        public bool Equals(BlockPos a1, BlockPos a2)
        {
            return a1.X == a2.X && a1.Y == a2.Y && a1.Z == a2.Z && a1.dimension == a2.dimension;
        }

        public int GetHashCode(BlockPos pos)
        {
            unchecked
            {
                // Быстрая версия с битовыми операциями и минимальным количеством операций
                var hash = pos.X;
                hash = (hash << 9) ^ (hash >> 23) ^ pos.Y;  // Сдвиги и XOR вместо умножения
                hash = (hash << 9) ^ (hash >> 23) ^ pos.Z;
                return hash ^ (pos.dimension * 269023);
            }
        }
    }
}