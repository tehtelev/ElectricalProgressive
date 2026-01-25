using ElectricalProgressive;
using ElectricalProgressive.Utils;
using EPImmersive.Content.Block;
using EPImmersive.Content.Block.CableDot;
using EPImmersive.Content.Block.CableSwitch;
using EPImmersive.Content.Block.EAccumulator;
using EPImmersive.Content.Block.EGenerator;
using EPImmersive.Content.Block.EMotor;
using EPImmersive.Content.Block.HVSFonar;
using EPImmersive.Content.Block.HVTower;
using EPImmersive.Interface;
using EPImmersive.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using static EPImmersive.Content.Block.ImmersiveWireBlock;
using static EPImmersive.ElectricalProgressiveImmersive;


[assembly: ModDependency("game", "1.21.0")]
[assembly: ModInfo(
    "Electrical Progressive: CoreImmersive",
    "electricalprogressivecoreimmersive",
    Website = "https://github.com/tehtelev/ElectricalProgressiveImmersive",
    Description = "Electrical logic library.",
    Version = "2.6.2",
    Authors = ["Tehtelev", "Kotl"]
)]



namespace EPImmersive
{
    public class ElectricalProgressiveImmersive : ModSystem
    {
        public readonly HashSet<ImmersiveNetwork> Networks = [];
        public readonly Dictionary<BlockPos, ImmersiveNetworkPart> Parts = new(new BlockPosComparer()); // Хранит все элементы всех цепей

        private Dictionary<BlockPos, List<EnergyPacket>> _packetsByPosition = new(new BlockPosComparer()); //Словарь для хранения пакетов по позициям

        private readonly List<ImmersiveEnergyPacket> _globalEnergyPackets = []; // Глобальный список пакетов энергии

        private ImmersiveAsyncPathFinder _immersiveAsyncPathFinder = null!;

        private ImmersiveNetworkInformation _result = new();

        private Dictionary<BlockPos, float> _sumEnergy = new();

        public ICoreAPI Api = null!;
        public ICoreClientAPI _capi = null!;
        private ICoreServerAPI _sapi = null!;

        public static IClientNetworkChannel? clientWireChannel;
        public static IServerNetworkChannel? serverWireChannel;


        private readonly BlockingCollection<ImmersiveNetwork> _networkProcessingQueue = new(); // коллекция для сетей
        private readonly List<Thread> _networkProcessingThreads = new();                //список потоков работников
        private volatile bool _networkProcessingRunning = true;                         //сети работают?
        private readonly CountdownEvent _networkProcessingCompleted = new(0); // ивент для окончания ожидания потоков
        private readonly ConcurrentBag<List<ImmersiveEnergyPacket>> _networkResults = new();      // список для пакетов в потоках



        private ImmersiveNetwork _localNetwork = new();




        public static AssetLocation soundElectricShok;

        public int TickTimeMs;
        private float _elapsedMs = 0f;

        int _envUpdater = 0;

        private long _listenerId1;
        //private long listenerId2;

        //private ImmersiveNetworkInformation _result = new();

        /// <summary>
        /// Запуск модификации
        /// </summary>
        /// <param name="api"></param>
        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            this.Api = api;

            soundElectricShok = new AssetLocation("electricalprogressivecore:sounds/electric-shock.ogg");


            api.RegisterBlockClass("BlockEIAccumulator1", typeof(BlockEIAccumulator1));
            api.RegisterBlockEntityClass("BlockEntityEIAccumulator1", typeof(BlockEntityEIAccumulator1));
            api.RegisterBlockEntityBehaviorClass("BEBehaviorEIAccumulator1", typeof(BEBehaviorEIAccumulator1));

            api.RegisterBlockClass("BlockEMotor1", typeof(BlockEMotor1));
            api.RegisterBlockEntityClass("BlockEntityEMotor1", typeof(BlockEntityEMotor1));
            api.RegisterBlockEntityBehaviorClass("BEBehaviorEMotor1", typeof(BEBehaviorEMotor1));


            api.RegisterBlockClass("BlockEGenerator1", typeof(BlockEGenerator1));
            api.RegisterBlockEntityClass("BlockEntityEGenerator1", typeof(BlockEntityEGenerator1));
            api.RegisterBlockEntityBehaviorClass("BEBehaviorEGenerator1", typeof(BEBehaviorEGenerator1));

            api.RegisterBlockClass("BlockHVSFonar", typeof(BlockHVSFonar));
            api.RegisterBlockEntityClass("BlockEntityHVSFonar", typeof(BlockEntityHVSFonar));
            api.RegisterBlockEntityBehaviorClass("BEBehaviorHVSFonar", typeof(BEBehaviorHVSFonar));

            api.RegisterBlockClass("BlockCableDotRoof", typeof(BlockCableDotRoof));
            api.RegisterBlockClass("BlockCableDotDown", typeof(BlockCableDotDown));
            api.RegisterBlockClass("BlockCableDotWall", typeof(BlockCableDotWall));
            api.RegisterBlockEntityClass("BlockEntityCableDotDown", typeof(BlockEntityCableDotDown));
            api.RegisterBlockEntityClass("BlockEntityCableDotWall", typeof(BlockEntityCableDotWall));
            api.RegisterBlockEntityClass("BlockEntityCableDotRoof", typeof(BlockEntityCableDotRoof));
            api.RegisterBlockEntityBehaviorClass("BEBehaviorCableDot", typeof(BEBehaviorCableDot));

            api.RegisterBlockClass("BlockCableSwitchWall", typeof(BlockCableSwitchWall));
            api.RegisterBlockEntityClass("BlockEntityCableSwitch", typeof(BlockEntityCableSwitch));
            api.RegisterBlockEntityBehaviorClass("BEBehaviorCableSwitch", typeof(BEBehaviorCableSwitch));

            api.RegisterBlockClass("BlockHVTower", typeof(BlockHVTower));
            api.RegisterBlockEntityClass("BlockEntityHVTower", typeof(BlockEntityHVTower));
            api.RegisterBlockEntityBehaviorClass("BEBehaviorHVTower", typeof(BEBehaviorHVTower));

            api.RegisterBlockEntityBehaviorClass("ElectricalProgressiveImmersive", typeof(BEBehaviorEPImmersive));
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
                _immersiveAsyncPathFinder?.Stop();
                _immersiveAsyncPathFinder = null;
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

            Networks.Clear();
            Parts.Clear();
            ImmersivePathCacheManager.Dispose();
        }

        /// <summary>
        /// Загрузка конфигурации и начальная инициализация
        /// </summary>
        /// <param name="api"></param>
        public override void StartPre(ICoreAPI api)
        {
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

            // регистрируем канал для синхронизации данных о закрепляемых проводах
            clientWireChannel = api.Network.RegisterChannel("EPWireChannel").RegisterMessageType(typeof(WireConnectionData));
        }


        /// <summary>
        /// Регистрация клавиш Alt
        /// </summary>
        private void RegisterAltKeys()
        {
            _capi.Input.RegisterHotKey("AltPressForNetwork", Lang.Get("AltPressForNetworkName"), GlKeys.LAlt);
        }

        /// <summary>
        /// Серверная сторона
        /// </summary>
        /// <param name="api"></param>
        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            this._sapi = api;

            _listenerId1 = _sapi.Event.RegisterGameTickListener(this.OnGameTickServer, TickTimeMs);

            // Инициализация поиска путей
            _immersiveAsyncPathFinder = new ImmersiveAsyncPathFinder(Parts, ElectricalProgressive.ElectricalProgressive.multiThreading);

            // Инициализация потоков для обработки сетей
            int threadCount = ElectricalProgressive.ElectricalProgressive.multiThreading;
            for (int i = 0; i < threadCount; i++)
            {
                var thread = new Thread(() => ProcessNetworksWorker())
                {
                    Name = $"NetworkProcessor-{i}",
                    IsBackground = true
                };
                _networkProcessingThreads.Add(thread);
                thread.Start();
            }

            // регистрируем канал для синхронизации данных о закрепляемых проводах
            serverWireChannel = _sapi.Network.RegisterChannel("EPWireChannel").RegisterMessageType(typeof
                (WireConnectionData)).SetMessageHandler<WireConnectionData>(new
                NetworkClientMessageHandler<WireConnectionData>(ImmersiveWireBlock.OnClientSent));
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
                    // Блокируем поток, пока не появится задача
                    if (_networkProcessingQueue.TryTake(out var network, Timeout.Infinite))
                    {
                        var context = GetContext();
                        try
                        {
                            ProcessNetwork(network, context);

                            if (context.LocalPackets.Count > 0)
                            {
                                var packetsCopy = new List<ImmersiveEnergyPacket>(context.LocalPackets);
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
                catch (Exception ex)
                {
                    // Логирование ошибки
                }
            }
        }


        /// <summary>
        /// Обновление электрической сети для иммерсивных проводов
        /// </summary>
        /// <param name="position">Позиция блока</param>
        /// <param name="wireNodes">Точки подключения</param>
        /// <param name="connections">Соединения</param>
        /// <param name="mainEparam">Основные параметры блока</param>
        /// <param name="currentEparam">Параметры текущего подключения</param>
        /// <param name="isLoaded">Загружен ли блок</param>
        /// <returns>Успешно ли обновлено</returns>
        public bool Update(BlockPos position, List<WireNode> wireNodes, ref List<ConnectionData> connections,
    EParams mainEpar, (EParams param, byte index) currentEpar, bool isLoaded)
        {
            bool hasChanges = false;

            if (!Parts.TryGetValue(position, out var part))
            {
                part = Parts[position] = new ImmersiveNetworkPart(position);
                hasChanges = true;
            }
            else if (!part.IsLoaded && isLoaded)
            {
                // Upgrading from stub to full part
                part.IsLoaded = true;
                hasChanges = true;
            }

            // 1. Проверяем изменения в WireNodes
            if (!AreWireNodesEqual(part.WireNodes, wireNodes))
            {
                part.WireNodes.Clear();
                part.WireNodes.AddRange(wireNodes);
                hasChanges = true;
            }

            // 2. Проверяем изменения в соединениях
            if (!AreConnectionsEqual(part.Connections, connections))
            {
                part.Connections.Clear();
                part.Connections.AddRange(connections);
                hasChanges = true;
            }

            // 3. Проверяем IsLoaded
            if (part.IsLoaded != isLoaded)
            {
                part.IsLoaded = isLoaded;
                hasChanges = true;
            }

            // 4. Проверяем основные параметры блока
            if (!mainEpar.Equals(new EParams()) && !part.MainEparams.Equals(mainEpar))
            {
                part.MainEparams = mainEpar;
                hasChanges = true;
            }

            // 5. Проверяем параметры конкретного подключения
            if (!currentEpar.param.Equals(new EParams()))
            {
                ConnectionData? connectionToUpdate = null;

                if (currentEpar.index < part.Connections.Count)
                    connectionToUpdate = part.Connections[currentEpar.index];

                if (connectionToUpdate != null)
                {
                    if (!connectionToUpdate.Parameters.Equals(currentEpar.param))
                    {
                        connectionToUpdate.Parameters = currentEpar.param;
                        hasChanges = true;
                    }
                }
                else
                {
                    var newConnection = new ConnectionData
                    {
                        LocalNodeIndex = currentEpar.index,
                        Parameters = currentEpar.param
                    };
                    part.Connections.Add(newConnection);
                    hasChanges = true;
                }
            }

            // 6. Обновляем соединения в сети только если были изменения
            if (hasChanges)
            {
                UpdateImmersiveConnections(ref part);
            }

            // Возвращаем обновленный список соединений
            connections = new(part.Connections);
            return hasChanges;
        }


        private bool AreWireNodesEqual(List<WireNode> list1, List<WireNode> list2)
        {
            if (list1.Count != list2.Count) return false;

            for (int i = 0; i < list1.Count; i++)
            {
                var node1 = list1[i];
                var node2 = list2[i];

                if (node1.Index != node2.Index ||
                    node1.Voltage != node2.Voltage ||
                    node1.Position.X != node2.Position.X ||
                    node1.Position.Y != node2.Position.Y ||
                    node1.Position.Z != node2.Position.Z ||
                    node1.Radius != node2.Radius)
                    return false;
            }

            return true;
        }

        private bool AreConnectionsEqual(List<ConnectionData> list1, List<ConnectionData> list2)
        {
            if (list1.Count != list2.Count) return false;

            for (int i = 0; i < list1.Count; i++)
            {
                var conn1 = list1[i];
                var conn2 = list2[i];

                if (conn1.LocalNodeIndex != conn2.LocalNodeIndex ||
                    !conn1.NeighborPos.Equals(conn2.NeighborPos) ||
                    conn1.NeighborNodeIndex != conn2.NeighborNodeIndex ||
                    !conn1.Parameters.Equals(conn2.Parameters) ||
                    Math.Abs(conn1.WireLength - conn2.WireLength) > 0.001f)
                    return false;
            }

            return true;
        }




        /// <summary>
        /// Обновление иммерсивных соединений в сети
        /// </summary>
        private void UpdateImmersiveConnections(ref ImmersiveNetworkPart part)
        {
            var network = GetOrCreateNetworkForPart(part);
            var networksToMerge = new HashSet<ImmersiveNetwork> { network };

            foreach (var connection in part.Connections)
            {
                var neighborPos = connection.NeighborPos;
                if (!Parts.TryGetValue(neighborPos, out var neighborPart))
                {
                    // Create stub for unloaded neighbor
                    neighborPart = new ImmersiveNetworkPart(neighborPos) { IsLoaded = false };
                    Parts[neighborPos] = neighborPart;
                    neighborPart.Network = network;
                    network.PartPositions.Add(neighborPos);
                }

                // Add/update the connection (normalized)
                AddOrUpdateImmersiveConnection(network, part.Position, connection.LocalNodeIndex, neighborPos, connection.NeighborNodeIndex, connection.Parameters);

                if (neighborPart.Network != network)
                {
                    networksToMerge.Add(neighborPart.Network);
                }
            }

            if (networksToMerge.Count > 1)
            {
                network = MergeNetworks(networksToMerge);
                part.Network = network;
            }
        }

        private void AddOrUpdateImmersiveConnection(ImmersiveNetwork network, BlockPos localPos, byte localIndex, BlockPos neighborPos, byte neighborIndex, EParams parameters)
        {
            // Canonicalize order: ensure LocalPos < NeighborPos
            bool swapped = localPos.Equals(neighborPos);
            if (swapped)
            {
                // Swap positions and indices (parameters remain the same)
                var tempPos = localPos;
                localPos = neighborPos;
                neighborPos = tempPos;

                var tempIndex = localIndex;
                localIndex = neighborIndex;
                neighborIndex = tempIndex;
            }

            // Check for existing connection
            var existing = network.ImmersiveConnections.FirstOrDefault(c =>
                c.LocalPos.Equals(localPos) && c.NeighborPos.Equals(neighborPos));

            if (existing != null)
            {
                // Update if parameters differ (assume mirror params are identical; if not, log or merge)
                if (!existing.Parameters.Equals(parameters))
                {
                    existing.Parameters = parameters; // Or merge logic if needed
                }
                return;
            }

            // Add new
            network.ImmersiveConnections.Add(new NetworkImmersiveConnection
            {
                LocalPos = localPos,
                NeighborPos = neighborPos,
                LocalNodeIndex = localIndex,
                NeighborNodeIndex = neighborIndex,
                Parameters = parameters
            });
        }

        /// <summary>
        /// Получает или создает сеть для части
        /// </summary>
        private ImmersiveNetwork GetOrCreateNetworkForPart(ImmersiveNetworkPart part)
        {
            // Ищем существующую сеть для этого блока
            foreach (var network in Networks)
            {
                if (network.PartPositions.Contains(part.Position))
                    return network;
            }

            // Создаем новую сеть если не нашли
            var newNetwork = CreateNetwork();
            newNetwork.PartPositions.Add(part.Position);

            // Добавляем компоненты в сеть
            if (part.Conductor is { } conductor) newNetwork.Conductors.Add(conductor);
            if (part.Consumer is { } consumer) newNetwork.Consumers.Add(consumer);
            if (part.Producer is { } producer) newNetwork.Producers.Add(producer);
            if (part.Accumulator is { } accumulator) newNetwork.Accumulators.Add(accumulator);
            if (part.Transformator is { } transformator) newNetwork.Transformators.Add(transformator);

            return newNetwork;
        }

        /// <summary>
        /// Добавляет иммерсивное соединение в сеть
        /// </summary>
        private void AddImmersiveConnection(ImmersiveNetworkPart part, ConnectionData connection, ImmersiveNetwork network)
        {
            // Проверяем, есть ли уже такое соединение
            var existingConnection = network.ImmersiveConnections
                .FirstOrDefault(c =>
                    c.LocalPos.Equals(part.Position) &&
                    c.LocalNodeIndex == connection.LocalNodeIndex &&
                    c.NeighborPos.Equals(connection.NeighborPos) &&
                    c.NeighborNodeIndex == connection.NeighborNodeIndex);

            if (existingConnection == null)
            {
                // Создаем новое сетевое соединение
                var networkConnection = new NetworkImmersiveConnection
                {
                    LocalPos = part.Position,
                    LocalNodeIndex = connection.LocalNodeIndex,
                    NeighborPos = connection.NeighborPos,
                    NeighborNodeIndex = connection.NeighborNodeIndex,
                    Parameters = connection.Parameters,
                    WireLength = connection.WireLength
                };
                network.ImmersiveConnections.Add(networkConnection);
                network.version++;
            }
            else
            {
                // Обновляем параметры существующего соединения
                existingConnection.Parameters = connection.Parameters;
            }

            // Добавляем соседа в сеть если его там нет
            if (!network.PartPositions.Contains(connection.NeighborPos))
            {
                // Ищем сеть, к которой принадлежит сосед
                ImmersiveNetwork neighborNetwork = null;
                foreach (var net in Networks)
                {
                    if (net.PartPositions.Contains(connection.NeighborPos))
                    {
                        neighborNetwork = net;
                        break;
                    }
                }

                if (neighborNetwork != null && neighborNetwork != network)
                {
                    // Если сосед уже в другой сети, объединяем сети
                    var networksToMerge = new HashSet<ImmersiveNetwork> { network, neighborNetwork };
                    var mergedNetwork = MergeNetworks(networksToMerge);

                    // Обновляем ссылку на сеть
                    network = mergedNetwork;
                }
                else if (neighborNetwork == null)
                {
                    // Сосед не в сети, добавляем его
                    if (Parts.TryGetValue(connection.NeighborPos, out var neighborPart))
                    {
                        network.PartPositions.Add(connection.NeighborPos);

                        // Добавляем компоненты соседа в сеть
                        if (neighborPart.Conductor is { } conductor) network.Conductors.Add(conductor);
                        if (neighborPart.Consumer is { } consumer) network.Consumers.Add(consumer);
                        if (neighborPart.Producer is { } producer) network.Producers.Add(producer);
                        if (neighborPart.Accumulator is { } accumulator) network.Accumulators.Add(accumulator);
                        if (neighborPart.Transformator is { } transformator) network.Transformators.Add(transformator);
                    }
                }
            }
        }

        /// <summary>
        /// Удаляет устаревшие иммерсивные соединения
        /// </summary>
        private void RemoveStaleImmersiveConnections(ImmersiveNetworkPart part, ImmersiveNetwork network)
        {
            // Находим соединения которые больше не актуальны
            var staleConnections = network.ImmersiveConnections
                .Where(c => c.LocalPos.Equals(part.Position) &&
                           !part.Connections.Any(pc =>
                               pc.LocalNodeIndex == c.LocalNodeIndex &&
                               pc.NeighborPos.Equals(c.NeighborPos) &&
                               pc.NeighborNodeIndex == c.NeighborNodeIndex))
                .ToList();

            // Удаляем устаревшие соединения
            foreach (var staleConnection in staleConnections)
            {
                network.ImmersiveConnections.Remove(staleConnection);
                network.version++;
            }

            if (staleConnections.Count > 0)
            {
                CheckAndSplitNetwork(network);
            }
        }

        /// <summary>
        /// Удаляем соединения
        /// </summary>
        /// <param name="position"></param>
        public void Remove(BlockPos position)
        {
            if (Parts.TryGetValue(position, out var part))
            {

                // Удаляем из всех сетей
                foreach (var network in Networks.Where(n => n.PartPositions.Contains(position)).ToList())
                {
                    network.PartPositions.Remove(position);

                    // Удаляем соединения связанные с этой позицией
                    network.ImmersiveConnections.RemoveAll(c =>
                        c.LocalPos.Equals(position) || c.NeighborPos.Equals(position));

                    if (part.Consumer != null)
                        network.Consumers.Remove(part.Consumer);

                    if (part.Producer != null)
                        network.Producers.Remove(part.Producer);

                    if (part.Accumulator != null)
                        network.Accumulators.Remove(part.Accumulator);

                    if (part.Conductor != null)
                        network.Conductors.Remove(part.Conductor);


                    network.version++;

                    // Если сеть пустая, удаляем её
                    if (network.PartPositions.Count == 0)
                    {
                        Networks.Remove(network);
                    }
                }

                Parts.Remove(position);
            }
        }

        /// <summary>
        /// Чистка всего и вся
        /// </summary>
        public void Cleaner()
        {
            foreach (var part in Parts)
            {

                // подчищаем списки пакетов, но не в ноль
                part.Value.Packets?.Clear();

                // Обработка параметров для иммерсивных проводов
                foreach (var connection in part.Value.Connections)
                {
                    if (!connection.Parameters.burnout && connection.Parameters.ticksBeforeBurnout > 0 && part.Value.Conductor is not VirtualConductor)
                    {
                        connection.Parameters.ticksBeforeBurnout--;
                    }
                    // Обнуляем токи в соединениях
                    connection.Parameters.current = 0f;
                }

                // Обработка основных параметров
                if (!part.Value.MainEparams.burnout && part.Value.MainEparams.ticksBeforeBurnout > 0 && part.Value.Conductor is not VirtualConductor)
                {
                    part.Value.MainEparams.ticksBeforeBurnout--;
                }

                //заполняем нулями токи и выданные энергии с пакетов
                if (!_sumEnergy.TryAdd(part.Key, 0F))
                {
                    _sumEnergy[part.Key] = 0F;
                }




                // Обнуляем основной ток
                part.Value.MainEparams.current = 0f;
            }
        }

        /// <summary>
        /// Логистическая задача для иммерсивных проводов
        /// </summary>
        private void LogisticalTask(ImmersiveNetwork network,
            List<BlockPos> consumerPositions,
            List<float> consumerRequests,
            List<BlockPos> producerPositions,
            List<float> producerGive,
            ImmersiveSimulation sim)
        {
            var cP = sim.CountWorkingCustomers = consumerPositions.Count;
            var pP = sim.CountWorkingStores = producerPositions.Count;

            BlockPos start;
            BlockPos end;

            // обновляем массив для расстояний, магазинов и клиентов
            if (sim.Distances.Length < cP * pP)
            {
                Array.Resize(ref sim.Distances, cP * pP);
                Array.Resize(ref sim.Path, cP * pP);
                Array.Resize(ref sim.NodeIndices, cP * pP);
                Array.Resize(ref sim.Voltage, cP * pP);
                Array.Resize(ref sim.PathLengths, cP * pP);  // Добавляем массив длин
            }

            if (sim.Stores == null || sim.Stores.Length < pP)
                sim.Stores = new ImmersiveStore[pP];

            if (sim.Customers == null || sim.Customers.Length < cP)
                sim.Customers = new ImmersiveCustomer[cP];

            for (var i = 0; i < cP; i++)
            {
                for (var j = 0; j < pP; j++)
                {
                    start = consumerPositions[i];
                    end = producerPositions[j];

                    if (ImmersivePathFinder.Heuristic(start, end) < ElectricalProgressive.ElectricalProgressive.maxDistanceForFinding * 3)
                    {
                        // Изменяем вызов TryGet - добавляем получение длины пути
                        if (ImmersivePathCacheManager.TryGet(start, end, out var cachedPath,
                            out var nodeIndices, out var cachedPathLength, out var version, out var voltage))
                        {
                            // Используем фактическую длину пути вместо количества блоков
                            sim.Distances[i * pP + j] = cachedPath != null ?
                                (int)Math.Ceiling(cachedPathLength) : int.MaxValue;

                            if (version != network.version)
                            {
                                _immersiveAsyncPathFinder.EnqueueRequest(start, end, network);
                            }

                            sim.Path[i * pP + j] = cachedPath;
                            sim.NodeIndices[i * pP + j] = nodeIndices;
                            sim.Voltage[i * pP + j] = voltage;
                            sim.PathLengths[i * pP + j] = cachedPathLength;  // Сохраняем длину
                        }
                        else
                        {
                            _immersiveAsyncPathFinder.EnqueueRequest(start, end, network);
                            sim.Distances[i * pP + j] = int.MaxValue;
                            sim.Path[i * pP + j] = null;
                            sim.NodeIndices[i * pP + j] = null;
                            sim.Voltage[i * pP + j] = 0;
                            sim.PathLengths[i * pP + j] = 0f;
                        }
                    }
                    else
                    {
                        sim.Distances[i * pP + j] = int.MaxValue;
                        sim.Path[i * pP + j] = null;
                        sim.NodeIndices[i * pP + j] = null;
                        sim.Voltage[i * pP + j] = 0;
                        sim.PathLengths[i * pP + j] = 0f;
                    }
                }
            }



            // инициализируем магазины
            for (var j = 0; j < pP; j++)
            {
                var store = sim.Stores[j];
                if (store == null)
                    sim.Stores[j] = store = new ImmersiveStore(j, producerGive[j]);
                store.Update(j, producerGive[j]);
            }


            // инициализируем клиентов
            for (var i = 0; i < cP; i++)
            {
                var distBuffer = new int[pP];

                for (var j = 0; j < pP; j++)
                {
                    distBuffer[j] = sim.Distances[i * pP + j];
                }

                var cust = sim.Customers[i];
                if (cust == null)
                    sim.Customers[i] = cust = new ImmersiveCustomer(i, consumerRequests[i], distBuffer);
                else
                    cust.Update(i, consumerRequests[i], distBuffer);
            }


            sim.Run(); // Запускаем симуляцию для распределения энергии между потребителями и производителями
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



        // Добавляем класс для пула контекстов обработки
        private class NetworkProcessingContext
        {
            public List<Consumer> LocalConsumers { get; } = [];
            public List<Producer> LocalProducers { get; } = [];
            public List<Accumulator> LocalAccums { get; } = [];
            public List<ImmersiveEnergyPacket> LocalPackets { get; } = [];

            public List<BlockPos> ConsumerPositions { get; } = [];
            public List<float> ConsumerRequests { get; } = [];
            public List<BlockPos> ProducerPositions { get; } = [];
            public List<float> ProducerGive { get; } = [];
            public List<BlockPos> Consumer2Positions { get; } = [];
            public List<float> Consumer2Requests { get; } = [];
            public List<BlockPos> Producer2Positions { get; } = [];
            public List<float> Producer2Give { get; } = [];

            public ImmersiveSimulation Sim { get; } = new();
            public ImmersiveSimulation Sim2 { get; } = new();

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



        private void ProcessNetwork(ImmersiveNetwork network, NetworkProcessingContext context)
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
                    if (ElectricalProgressive.ElectricalProgressive.enableLossCompensation)
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



            ImmersiveEnergyPacket packet;   // Временная переменная для пакета энергии
            BlockPos posStore; // Позиция магазина в мире
            BlockPos posCustomer; // Позиция потребителя в мире
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
                        packet = new ImmersiveEnergyPacket(
                            value,
                            context.Sim.Voltage[i * storeCount + k],
                            context.Sim.Path[i * storeCount + k].Length - 1,
                            context.Sim.Path[i * storeCount + k],
                            context.Sim.NodeIndices[i * storeCount + k]
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
                if (context.Sim.Stores![consIter + context.LocalProducers.Count].Stock < accum.ImmersiveAccum.canRelease())
                {
                    accum.ImmersiveAccum.Release(accum.ImmersiveAccum.canRelease() -
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
                context.Producer2Positions.Add(producer.ImmersiveProducer.Pos);
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
                        packet = new ImmersiveEnergyPacket(
                            value,
                            context.Sim2.Voltage[i * storeCount + k],
                            context.Sim2.Path[i * storeCount + k].Length - 1,
                            context.Sim2.Path[i * storeCount + k],
                            context.Sim2.NodeIndices[i * storeCount + k]
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
                producer.ImmersiveProducer.Produce_order(totalOrder);
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
                ImmersivePathCacheManager.Cleanup();
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
            ImmersiveNetworkPart part;                        // Временная переменная для части сети
            bool updated;                            // Флаг обновления части сети от повреждения

            float totalEnergy;                       // Суммарная энергия в трансформаторе
            float totalCurrent;                      // Суммарный ток в трансформаторе
            var kons = 0;

            foreach (var partEntry in Parts)
            {
                partPos = partEntry.Key;
                part = partEntry.Value;

                //обновляем каждый блок сети
                updated = kons % 20 == _envUpdater &&
                          part.IsLoaded && false         // блок загружен?
                                                         //(damageManager?.DamageByEnvironment(this._sapi, ref part, ref bAccessor) ?? false)
                          ;
                kons++;


                if (updated)
                {
                    // Проверяем сгорание для иммерсивных соединений
                    foreach (var connection in part.Connections)
                    {
                        if (connection.Parameters.voltage == 0 || !connection.Parameters.burnout)
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

                    foreach (var packet2 in part.Packets)
                    {

                        totalEnergy += packet2.energy;
                        totalCurrent += packet2.energy / packet2.voltage;


                        if (packet2.voltage == bufPartTrans.HighVoltage)
                            packet2.voltage = bufPartTrans.LowVoltage;
                        else if (packet2.voltage == bufPartTrans.LowVoltage)
                            packet2.voltage = bufPartTrans.HighVoltage;

                    }


                    part.MainEparams.current = totalCurrent;
                    bufPartTrans.setPower(totalEnergy);

                }


                // Проверка на превышение напряжения для иммерсивных соединений
                foreach (var packet2 in part.Packets)
                {
                    foreach (var connection in part.Connections)
                    {
                        if (connection.Parameters.voltage != 0 && packet2.voltage > connection.Parameters.voltage)
                        {
                            connection.Parameters.prepareForBurnout(2);

                            /*
                            var neighborPart = Parts[connection.NeighborPos];

                            ConnectionData connect = null;

                            for (int i = 0; i < neighborPart.Connections.Count; i++)
                            {
                                connect = neighborPart.Connections[i];

                                if (connect.NeighborPos.Equals(part.Position) && connect.NeighborNodeIndex == connection.LocalNodeIndex)
                                    break;

                                connect = null;
                            }

                            connect.Parameters.burnou
                            */
                            /*
                            if (packet2.path[packet2.currentIndex] == partPos)
                                packet2.shouldBeRemoved = true;
                            */
                            //ResetComponents(ref part);
                        }
                    }
                }


                // Проверка на превышение тока для иммерсивных соединений
                foreach (var connection in part.Connections)
                {
                    if (connection.Parameters.voltage == 0 ||
                        Math.Abs(connection.Parameters.current) <= connection.Parameters.maxCurrent * connection.Parameters.lines)
                        continue;

                    connection.Parameters.prepareForBurnout(1);

                    /*
                    foreach (var p in _globalEnergyPackets)
                    {
                        // Здесь нужно добавить логику проверки для иммерсивных соединений
                        if (p.path[p.currentIndex] == partPos)
                        {
                            p.shouldBeRemoved = true;
                        }
                    }
                    */

                    //ResetComponents(ref part);
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
            ImmersiveNetworkPart currentPart;      // Временные переменные для частей сети



            // Заполняем списки пакетов по позициям
            foreach (var packet in _globalEnergyPackets)
            {
                pos = packet.path[packet.currentIndex];
                if (Parts.TryGetValue(pos, out var partValue))
                {
                    if (partValue.Packets == null)
                        partValue.Packets = [];
                    else
                    {
                        partValue.Packets.Add(packet);
                    }
                }
                else // чтобы не застревали в частях сети, которые перестали существовать
                {
                    packet.shouldBeRemoved = true;
                }

            }

            // перебираем все части
            foreach (var partValue in Parts.Values)
            {
                // если пакетов нет тут, то пропускаем
                if (partValue.Packets == null || partValue.Packets.Count == 0)
                    continue;

                //int deltaX, deltaY, deltaZ;

                // перебираем все пакеты, которые могут находиться в этой части
                foreach (var packet in partValue.Packets)
                {
                    curIndex = packet.currentIndex; //текущий индекс в пакете

                    if (curIndex > 0)
                    {
                        // ищем провода которые ведут на слежующий блок в пути

                        ConnectionData connect = null;

                        for (int i = 0; i < partValue.Connections.Count; i++)
                        {
                            connect = partValue.Connections[i];

                            if (connect.NeighborPos.Equals(packet.path[curIndex - 1]) && 
                                 ((Parts.TryGetValue(connect.NeighborPos, out var partNeighborValue) && partNeighborValue.Conductor!=null && !partNeighborValue.Conductor.IsOpen) || connect.NeighborNodeIndex == packet.nodeIndices[curIndex - 1]))
                                break;

                            connect = null;
                        }



                        if (connect == null)
                        {
                            // если все же путь не совпадает с путем в пакете, то чистим кэши
                            ImmersivePathCacheManager.RemoveAll(packet.path[0], packet.path.Last());
                            packet.shouldBeRemoved = true;
                            continue;
                        }

                        // Проверяем иммерсивные провода
                        if (!connect.Parameters.burnout) //проверяем не сгорели ли соединения
                        {

                            // проверяем может ли пакет тут пройти
                            if (connect.Parameters.voltage == 0
                                || connect.Parameters.burnout
                                || packet.voltage > connect.Parameters.voltage)
                            {
                                packet.shouldBeRemoved = true;
                                continue;
                            }


                            // считаем сопротивление для основного блока (используем основные параметры как fallback)
                            resistance = ElectricalProgressive.ElectricalProgressive.energyLossFactor *
                                         connect.WireLength *
                                         connect.Parameters.resistivity /
                                         (connect.Parameters.lines *
                                          connect.Parameters.crossArea);

                            // Провод в изоляции теряет меньше энергии
                            if (connect.Parameters.isolated)
                                resistance /= 2.0f;

                            // считаем ток по закону Ома
                            current = packet.energy / packet.voltage;

                            // считаем потерю энергии по закону Джоуля
                            lossEnergy = current * current * resistance;
                            packet.energy = Math.Max(packet.energy - lossEnergy, 0);

                            // пересчитаем ток уже с учетом потерь
                            current = packet.energy / packet.voltage;

                            // Учитываем ток в основных параметрах
                            connect.Parameters.current += current;

                            // 3) Если энергия пакета почти нулевая — удаляем пакет
                            if (packet.energy <= 0.001f)
                            {
                                packet.shouldBeRemoved = true;
                            }



                        }
                        else
                        {
                            packet.shouldBeRemoved = true; // этот пакет удаляем
                        }

                    }


                    // последний блок?
                    if (curIndex == 0)
                    {
                        pos = packet.path[0];

                        if (Parts.TryGetValue(pos, out var part2))
                        {
                            // ReSharper disable once ReplaceWithSingleAssignment.False
                            bool isValid = false;


                            // Также проверяем основные параметры
                            if (!isValid
                                && part2.MainEparams.voltage > 0
                                && !part2.MainEparams.burnout
                                && packet.voltage == part2.MainEparams.voltage)
                            {
                                isValid = true;
                            }

                            // если все ок, то продолжаем
                            if (isValid)
                            {
                                if (_sumEnergy.ContainsKey(pos))
                                {
                                    _sumEnergy[pos] += packet.energy;
                                }
                                else
                                {
                                    _sumEnergy.Add(pos, packet.energy);
                                }
                            }
                        }

                        packet.shouldBeRemoved = true; // этот пакет удаляем
                    }
                    else
                    {
                        // первый блок пропускаем из расчета
                        if (curIndex == packet.path.Length - 1)
                        {
                            packet.currentIndex--;
                            continue;
                        }

                        //currentPos = packet.path[curIndex]; // текущая позиция в пути пакета


                        // считаем сопротивление для основного блока (используем основные параметры как fallback)
                        resistance = ElectricalProgressive.ElectricalProgressive.energyLossFactor *
                                     partValue.MainEparams.resistivity /
                                     (partValue.MainEparams.lines *
                                      partValue.MainEparams.crossArea);

                        // Провод в изоляции теряет меньше энергии
                        if (partValue.MainEparams.isolated)
                            resistance /= 2.0f;

                        // считаем ток по закону Ома
                        current = packet.energy / packet.voltage;

                        // считаем потерю энергии по закону Джоуля
                        lossEnergy = current * current * resistance;
                        packet.energy = Math.Max(packet.energy - lossEnergy, 0);

                        // пересчитаем ток уже с учетом потерь
                        current = packet.energy / packet.voltage;

                        // Учитываем ток в основных параметрах
                        partValue.MainEparams.current += current;

                        // 3) Если энергия пакета почти нулевая — удаляем пакет
                        if (packet.energy <= 0.001f)
                        {
                            packet.shouldBeRemoved = true;
                        }

                        // переходим к следующему блоку в пути
                        packet.currentIndex--;




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
        private void UpdateNetworkInfo(ImmersiveNetwork network)
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
        private static void ResetComponents(ref ImmersiveNetworkPart part)
        {
            part.Consumer?.Consume_receive(0f);
            part.Producer?.Produce_order(0f);
            part.Accumulator?.SetCapacity(0f);
            part.Transformator?.setPower(0f);
        }




        public void CheckAndSplitNetwork(ImmersiveNetwork network)
        {
            var components = new List<HashSet<BlockPos>>();
            var visited = new HashSet<BlockPos>();

            foreach (var start in network.PartPositions)
            {
                if (visited.Contains(start)) continue;

                var component = new HashSet<BlockPos>();
                var queue = new Queue<BlockPos>();
                queue.Enqueue(start);
                visited.Add(start);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    component.Add(current);

                    if (Parts.TryGetValue(current, out var part))
                    {
                        foreach (var conn in part.Connections)
                        {
                            var neighbor = conn.NeighborPos;
                            if (!visited.Contains(neighbor) && network.PartPositions.Contains(neighbor))
                            {
                                queue.Enqueue(neighbor);
                                visited.Add(neighbor);
                            }
                        }
                    }
                }

                if (component.Count > 0)
                {
                    components.Add(component);
                }
            }

            if (components.Count > 1)
            {
                // Первая компонента остается в исходной сети
                network.PartPositions.Clear();
                network.PartPositions.UnionWith(components[0]);

                // Очищаем и перезаполняем коллекции компонентов
                ClearNetworkComponents(network);
                RebuildNetworkComponents(network);

                // Для остальных компонент создаем новые сети
                for (int i = 1; i < components.Count; i++)
                {
                    var newNetwork = CreateNetwork();
                    newNetwork.PartPositions.UnionWith(components[i]);

                    // Перемещаем компоненты в новую сеть
                    MoveComponentsToNetwork(components[i], newNetwork);
                    Networks.Add(newNetwork);
                }

                network.version++;
            }
        }

        private void ClearNetworkComponents(ImmersiveNetwork network)
        {
            network.Consumers.Clear();
            network.Producers.Clear();
            network.Accumulators.Clear();
            network.Transformators.Clear();
            network.Conductors.Clear();
            // Иммерсивные соединения остаются - они фильтруются по PartPositions
        }

        private void RebuildNetworkComponents(ImmersiveNetwork network)
        {
            foreach (var pos in network.PartPositions)
            {
                if (Parts.TryGetValue(pos, out var part))
                {
                    if (part.Consumer != null) network.Consumers.Add(part.Consumer);
                    if (part.Producer != null) network.Producers.Add(part.Producer);
                    if (part.Accumulator != null) network.Accumulators.Add(part.Accumulator);
                    if (part.Transformator != null) network.Transformators.Add(part.Transformator);
                    if (part.Conductor != null) network.Conductors.Add(part.Conductor);
                }
            }

            // Фильтруем иммерсивные соединения
            network.ImmersiveConnections.RemoveAll(c =>
                !network.PartPositions.Contains(c.LocalPos) ||
                !network.PartPositions.Contains(c.NeighborPos));
        }

        private void MoveComponentsToNetwork(HashSet<BlockPos> positions, ImmersiveNetwork targetNetwork)
        {
            foreach (var pos in positions)
            {
                if (Parts.TryGetValue(pos, out var part))
                {
                    part.Network = targetNetwork;

                    if (part.Consumer != null) targetNetwork.Consumers.Add(part.Consumer);
                    if (part.Producer != null) targetNetwork.Producers.Add(part.Producer);
                    if (part.Accumulator != null) targetNetwork.Accumulators.Add(part.Accumulator);
                    if (part.Transformator != null) targetNetwork.Transformators.Add(part.Transformator);
                    if (part.Conductor != null) targetNetwork.Conductors.Add(part.Conductor);
                }
            }

            // Переносим соединения
            foreach (var connection in targetNetwork.ImmersiveConnections.ToList())
            {
                if (positions.Contains(connection.LocalPos) && positions.Contains(connection.NeighborPos))
                {
                    // Соединение остается в новой сети
                }
                else
                {
                    targetNetwork.ImmersiveConnections.Remove(connection);
                }
            }
        }







        /// <summary>
        /// Объединение цепей
        /// </summary>
        /// <param name="networks"></param>
        /// <returns></returns>
        private ImmersiveNetwork MergeNetworks(HashSet<ImmersiveNetwork> networks)
        {
            ImmersiveNetwork? outNetwork = null;

            foreach (var network in networks)
            {
                if (network == null)
                    continue;

                if (outNetwork == null || outNetwork.PartPositions.Count < network.PartPositions.Count)
                {
                    outNetwork = network;
                }
            }

            if (outNetwork != null)
            {
                foreach (var network in networks)
                {
                    if (network == null)
                        continue;

                    if (outNetwork == network)
                    {
                        continue;
                    }

                    foreach (var position in network.PartPositions)
                    {
                        var part = this.Parts[position];
                        // Для иммерсивных проводов обновляем сеть
                        part.Network = outNetwork;

                        if (part.Conductor is { } conductor) outNetwork.Conductors.Add(conductor);
                        if (part.Consumer is { } consumer) outNetwork.Consumers.Add(consumer);
                        if (part.Producer is { } producer) outNetwork.Producers.Add(producer);
                        if (part.Accumulator is { } accumulator) outNetwork.Accumulators.Add(accumulator);
                        if (part.Transformator is { } transformator) outNetwork.Transformators.Add(transformator);

                        outNetwork.PartPositions.Add(position);
                    }

                    // Переносим иммерсивные соединения
                    outNetwork.ImmersiveConnections.AddRange(network.ImmersiveConnections);

                    network.PartPositions.Clear();
                    network.ImmersiveConnections.Clear();
                    this.Networks.Remove(network);
                }
            }

            outNetwork ??= this.CreateNetwork();

            // After merging, rebuild components with loaded filter (similar to above)
            RebuildNetworkComponents(outNetwork);

            return outNetwork;
        }

        /// <summary>
        /// Удаляем сеть
        /// </summary>
        /// <param name="network"></param>
        private void RemoveNetwork(ref ImmersiveNetwork network)
        {
            var partPositions = new BlockPos[network.PartPositions.Count];
            network.PartPositions.CopyTo(partPositions);
            network.version++;
            this.Networks.Remove(network);                                  //удаляем цепь из списка цепей

            foreach (var position in partPositions)                         //перебираем по всем бывшим элементам этой цепи
            {
                if (this.Parts.TryGetValue(position, out var part))         //есть такое соединение?
                {
                    part.Network = null;                                    //обнуляем сеть
                }
            }

            // Для иммерсивных проводов создаем новые сети для оставшихся частей
            foreach (var position in partPositions)                                 //перебираем по всем бывшим элементам этой цепи
            {
                if (this.Parts.TryGetValue(position, out var part))                 //есть такое соединение?
                {
                    UpdateImmersiveConnections(ref part);                           //обновляем соединения
                }
            }
        }

        /// <summary>
        /// Создаем новую цепь
        /// </summary>
        /// <returns></returns>
        private ImmersiveNetwork CreateNetwork()
        {
            var network = new ImmersiveNetwork();
            this.Networks.Add(network);

            return network;
        }

        /// <summary>
        /// Задать проводник
        /// </summary>
        /// <param name="position"></param>
        /// <param name="conductor"></param>
        public void SetConductor(BlockPos position, IEImmersiveConductor? conductor) =>
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
        public void SetConsumer(BlockPos position, IEImmersiveConsumer? consumer) =>
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
        public void SetProducer(BlockPos position, IEImmersiveProducer? producer) =>
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
        public void SetAccumulator(BlockPos position, IEImmersiveAccumulator? accumulator) =>
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
        public void SetTransformator(BlockPos position, IEImmersiveTransformator? transformator) =>
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
            System.Func<ImmersiveNetworkPart, T?> getComponent,
            Action<ImmersiveNetworkPart, T?> setComponent,
            System.Func<ImmersiveNetwork, ICollection<T>> getCollection)
            where T : class
        {
            if (!this.Parts.TryGetValue(position, out var part))
            {
                if (newComponent == null)
                {
                    return;
                }

                part = this.Parts[position] = new ImmersiveNetworkPart(position);
            }

            var oldComponent = getComponent(part);
            if (oldComponent != newComponent)
            {
                // Находим сети, к которым принадлежит эта часть
                var networks = Networks.Where(n => n.PartPositions.Contains(position)).ToList();

                foreach (var network in networks)
                {
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
        /// Собирает информацию по цепи для иммерсивных проводов с учетом подключений
        /// </summary>
        /// <param name="position">Позиция блока</param>
        /// <param name="nodeIndex">Индекс точки подключения (если -1 - общая информация)</param>
        /// <returns>Информация о сети</returns>
        public ImmersiveNetworkInformation GetNetworkForImmersiveWire(BlockPos position, int nodeIndex = -1)
        {
            _result.Reset(); // сбрасываем значения

            if (this.Parts.TryGetValue(position, out var part))
            {
                // Количество подключенных проводов к блоку
                _result.NumberOfConnections = part.Connections.Count;

                // Основные параметры блока
                _result.eParamsInNetwork = part.MainEparams;
                _result.current = part.MainEparams.current;

                // Проверяем, замкнуты ли узлы
                if (part.Conductor!=null && part.Conductor.IsOpen)
                {
                    _result.IsConductorOpen = true;
                }
                else
                {
                    _result.IsConductorOpen = false;
                }

                // Собираем информацию о всех сетях, к которым подключен блок
                var networks = new HashSet<ImmersiveNetwork>();

                if (nodeIndex == -1)
                {
                    // Для всех соединений блока
                    foreach (var connection in part.Connections)
                    {
                        // Ищем сеть для каждого соединения
                        var network = Networks.FirstOrDefault(n =>
                            n.PartPositions.Contains(position) &&
                            n.ImmersiveConnections.Any(c =>
                                (c.LocalPos.Equals(position) && c.LocalNodeIndex == connection.LocalNodeIndex) ||
                                (c.NeighborPos.Equals(position) && c.NeighborNodeIndex == connection.LocalNodeIndex)));

                        if (network != null)
                        {
                            networks.Add(network);
                        }
                    }
                }
                else
                {
                    // Для конкретного узла
                    var network = Networks.FirstOrDefault(n =>
                        n.PartPositions.Contains(position) &&
                        n.ImmersiveConnections.Any(c =>
                            (c.LocalPos.Equals(position) && c.LocalNodeIndex == nodeIndex) ||
                            (c.NeighborPos.Equals(position) && c.NeighborNodeIndex == nodeIndex)));

                    if (network != null)
                    {
                        networks.Add(network);
                    }
                }

                // Если не нашли сети по соединениям, ищем сеть, к которой принадлежит блок
                if (networks.Count == 0)
                {
                    var network = Networks.FirstOrDefault(n => n.PartPositions.Contains(position));
                    if (network != null)
                    {
                        networks.Add(network);
                    }
                }

                _result.NumberOfNetworks = networks.Count;

                // Собираем информацию по каждой сети
                foreach (var network in networks)
                {
                    var networkData = new NetworkData
                    {
                        NumberOfAccumulators = network.Accumulators.Count,
                        NumberOfConsumers = network.Consumers.Count,
                        NumberOfProducers = network.Producers.Count,
                        NumberOfTransformators = network.Transformators.Count,
                        NumberOfConductors = network.Conductors.Count,
                        Consumption = network.Consumption,
                        Capacity = network.Capacity,
                        MaxCapacity = network.MaxCapacity,
                        Production = network.Production,
                        Request = network.Request
                    };



                    _result.Networks.Add(networkData);

                    // Суммируем общую информацию (для обратной совместимости)
                    _result.NumberOfBlocks += network.PartPositions.Count;
                    _result.NumberOfConsumers += networkData.NumberOfConsumers;
                    _result.NumberOfProducers += networkData.NumberOfProducers;
                    _result.NumberOfAccumulators += networkData.NumberOfAccumulators;
                    _result.NumberOfTransformators += networkData.NumberOfTransformators;
                    _result.Production += networkData.Production;
                    _result.Consumption += networkData.Consumption;
                    _result.Capacity += networkData.Capacity;
                    _result.MaxCapacity += networkData.MaxCapacity;
                    _result.Request += networkData.Request;
                }

                // Если нет сетей, но есть часть
                if (networks.Count == 0)
                {
                    _result.NumberOfBlocks = 1; // Только этот блок
                    _result.NumberOfNetworks = 0;
                }
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
        public readonly IEImmersiveConductor ImmersiveConductor;
        public Conductor(IEImmersiveConductor immersiveConductor) => ImmersiveConductor = immersiveConductor;
    }

    /// <summary>
    /// Потребитель
    /// </summary>
    internal class Consumer
    {
        public readonly IEImmersiveConsumer ImmersiveConsumer;
        public Consumer(IEImmersiveConsumer immersiveConsumer) => ImmersiveConsumer = immersiveConsumer;
    }

    /// <summary>
    /// Трансформатор
    /// </summary>
    internal class Transformator
    {
        public readonly IEImmersiveTransformator ImmersiveTransformator;
        public Transformator(IEImmersiveTransformator immersiveTransformator) => ImmersiveTransformator = immersiveTransformator;
    }


    /// <summary>
    /// Генератор
    /// </summary>
    internal class Producer
    {
        public readonly IEImmersiveProducer ImmersiveProducer;
        public Producer(IEImmersiveProducer immersiveProducer) => ImmersiveProducer = immersiveProducer;
    }


    /// <summary>
    /// Аккумулятор
    /// </summary>
    internal class Accumulator
    {
        public readonly IEImmersiveAccumulator ImmersiveAccum;
        public Accumulator(IEImmersiveAccumulator immersiveAccum) => ImmersiveAccum = immersiveAccum;
    }




}

