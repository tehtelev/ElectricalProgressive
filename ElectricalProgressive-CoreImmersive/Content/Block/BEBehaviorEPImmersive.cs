using ElectricalProgressive.Utils;
using EPImmersive.Interface;
using EPImmersive.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace EPImmersive.Content.Block;




public class WireNode
{
    public byte Index { get; set; }           // Индекс точки подключения
    public int Voltage { get; set; }          // Напряжение точки (максимальное)
    public Vec3d Position { get; set; }       // Локальная позиция относительно позиции блока
    public float Radius { get; set; }         // Радиус области подключения (для размеров рамки выделения вокрг точки)
}





public class ConnectionData
{
    public byte LocalNodeIndex { get; set; }      // Индекс точки подключения на ТЕКУЩЕМ устройстве
    public BlockPos NeighborPos { get; set; }     // Позиция СОСЕДНЕГО устройства
    public byte NeighborNodeIndex { get; set; }   // Индекс точки подключения на СОСЕДНЕМ устройстве

    public EParams Parameters;                    // Параметры этого конкретного соединения

    public float WireLength;

    public Vec3d NeighborNodeLocalPos { get; set; } // хранение локальной позиции соседнего нода
}




public class BEBehaviorEPImmersive : BlockEntityBehavior
{
    // Единый список всех соединений
    private List<ConnectionData> _connections = new List<ConnectionData>();

    // Список точек подключения из JSON атрибутов
    private List<WireNode> _wireNodes = new List<WireNode>();

    public BEBehaviorEPImmersive(BlockEntity blockEntity)
        : base(blockEntity)
    {

    }


    // Константы для пакетов
    public const int PacketIdRequestNetworkInfo = 1122334457;
    public const int PacketIdResponseNetworkInfo = 1122334458;

    // Добавляем поле для хранения информации о сети
    private ImmersiveNetworkInformation networkInformation = new();
    private DateTime lastRequestTime = DateTime.MinValue;



    public const string InterruptionKey = "electricalprogressive:interruption";
    public const string ConnectionKey = "electricalprogressive:connection";
    public const string IsLoadedKey = "electricalprogressive:isloaded";

    private IEImmersiveAccumulator? _accumulator;
    private IEImmersiveConsumer? _consumer;
    private IEImmersiveConductor? _conductor;
    private IEImmersiveProducer? _producer;
    private IEImmersiveTransformator? _transformator;

    public List<Vec3d> ParticlesOffsetPos = new List<Vec3d>(1);
    public List<int[]> ParticlesFramesAnim = new List<int[]>(1);
    public int ParticlesType = 0;
    private BlockEntityAnimationUtil AnimUtil;
    private BlockPos? mainPartPos;



    public global::EPImmersive.ElectricalProgressiveImmersive? System =>
        this.Api?.ModLoader.GetModSystem<global::EPImmersive.ElectricalProgressiveImmersive>();

    private bool _dirty = true;
    private bool _paramsSet = false;
    private (EParams param, byte index) _eparams;
    private bool _isLoaded;

    private EParams _mainEpar;




    /// <summary>
    /// Получает список точек подключения этого блока
    /// </summary>
    public EParams MainEparams()
    {
        return _mainEpar;
    }



    /// <summary>
    /// Получает список точек подключения этого блока
    /// </summary>
    public List<WireNode> GetWireNodes()
    {
        return _wireNodes;
    }

    /// <summary>
    /// Получает точку подключения по индексу
    /// </summary>
    public WireNode? GetWireNode(byte index)
    {
        return _wireNodes.FirstOrDefault(node => node.Index == index);
    }

    /// <summary>
    /// Добавляет направление подключений иммерсивных проводов
    /// </summary>
    public void AddImmersiveConnection(byte indexHere, BlockPos neighborPos, byte indexNeighbor)
    {
        // начальная точка крепления
        var startWorldPos = new Vec3d(
            Pos.X + _wireNodes[indexHere].Position.X,
            Pos.Y + _wireNodes[indexHere].Position.Y,
            Pos.Z + _wireNodes[indexHere].Position.Z
        );

        // Получаем нод конца
        WireNode endNode = null;
        var neighborEntity = Api.World.BlockAccessor.GetBlockEntity(neighborPos);
        var neighborBehavior = neighborEntity?.GetBehavior<BEBehaviorEPImmersive>();
        if (neighborBehavior != null)
        {
            endNode = neighborBehavior.GetWireNode(indexNeighbor);
        }

        // конечная точка крепления
        var endWorldPos = new Vec3d(
            neighborPos.X + endNode.Position.X,
            neighborPos.Y + endNode.Position.Y,
            neighborPos.Z + endNode.Position.Z
        );

        // точная длина провода
        float distance = startWorldPos.DistanceTo(endWorldPos);

        // Сохраняем позицию нода соседа (если не получили, используем нули)
        var neighborNodeLocalPos = endNode?.Position ?? new Vec3d(0, 0, 0);

        // создаем соединение
        var newConnection = new ConnectionData
        {
            LocalNodeIndex = indexHere,
            NeighborPos = neighborPos,
            NeighborNodeIndex = indexNeighbor,
            NeighborNodeLocalPos = neighborNodeLocalPos, // Сохраняем позицию
            Parameters = new EParams(),
            WireLength = distance
        };

        _connections.Add(newConnection);


        this._dirty = true;
        this._paramsSet = false;

        this.Update();
    }

    /// <summary>
    /// Отдает список подключений этого блока
    /// </summary>
    public List<ConnectionData> GetImmersiveConnections()
    {
        return _connections;
    }

    /// <summary>
    /// Добавляет или обновляет параметры для соединения по индексу
    /// </summary>
    public void AddEparamsAt(EParams param, byte index)
    {
        if (_connections.Count > index)
        {
            _connections[index].Parameters = param;
        }
        else
        {
            // Если индекс выходит за границы, создаем новое соединение с параметрами
            var newConnection = new ConnectionData
            {
                LocalNodeIndex = index,
                NeighborPos = Blockentity.Pos, // Временная позиция, должна быть установлена позже
                NeighborNodeIndex = 0,
                Parameters = param
            };
            _connections.Add(newConnection);
        }

        this._dirty = true;
        this._paramsSet = true;
        this._eparams = (param, index);
        this.Update();
    }

    /// <summary>
    /// Добавляет или обновляет основыне параметры для текущего блока
    /// </summary>
    public void AddMainEparams(EParams param)
    {
        _mainEpar = param;

        this._dirty = true;
        this._paramsSet = false;

        this.Update();
    }






    /// <summary>
    /// Находит соединение по параметрам
    /// </summary>
    public ConnectionData? FindConnection(byte localIndex, BlockPos neighborPos, byte neighborIndex)
    {
        return _connections.FirstOrDefault(c =>
            c.LocalNodeIndex == localIndex &&
            c.NeighborPos.Equals(neighborPos) &&
            c.NeighborNodeIndex == neighborIndex);
    }

    /// <summary>
    /// Находит соединения по текущему ноду
    /// </summary>
    public List<ConnectionData> FindConnection(byte localIndex)
    {
        return _connections.Where(c => c.LocalNodeIndex == localIndex).ToList();
    }

    /// <summary>
    /// Находит соединения по соседскому ноду
    /// </summary>
    public List<ConnectionData> FindConnection(BlockPos neighborPos, byte neighborIndex)
    {
        return _connections.Where(c => c.NeighborPos.Equals(neighborPos) && c.NeighborNodeIndex == neighborIndex).ToList();
    }

    /// <summary>
    /// Получает все соединения с указанным соседом
    /// </summary>
    public List<ConnectionData> GetConnectionsToNeighbor(BlockPos neighborPos)
    {
        return _connections.Where(c => c.NeighborPos.Equals(neighborPos)).ToList();
    }

    /// <summary>
    /// Удаляет соединение
    /// </summary>
    public void RemoveConnection(byte localIndex, BlockPos neighborPos, byte neighborIndex)
    {
        var connectionHere = FindConnection(localIndex, neighborPos, neighborIndex);
        if (connectionHere != null)
        {
            _connections.Remove(connectionHere);
            this._eparams = (new EParams(), 0);
            this._dirty = true;
            this.Update();

            // После удаления соединения проверяем разделение сети
            var network = System.Networks.FirstOrDefault(n => n.PartPositions.Contains(Pos));
            if (network != null)
            {
                System.CheckAndSplitNetwork(network);
            }

            // Также проверяем сеть соседа, если он в другой сети
            var neighborNetwork = System.Networks.FirstOrDefault(n => n.PartPositions.Contains(neighborPos));
            if (neighborNetwork != null && neighborNetwork != network)
            {
                System.CheckAndSplitNetwork(neighborNetwork);
            }
        }
    }

    public override void Initialize(ICoreAPI api, Vintagestory.API.Datastructures.JsonObject properties)
    {
        base.Initialize(api, properties);

        // не двигать, должно грузиться до UpdateWireNodes
        // Загружаем точки подключения из JSON
        LoadWireNodes();



        GetParticles();



        AnimUtil = Blockentity.GetBehavior<BEBehaviorAnimatable>()?.animUtil!;

        if (api is ICoreClientAPI capi)
        {
            capi.Event.RegisterAsyncParticleSpawner(OnAsyncParticles);
        }


        this._isLoaded = true;
        this._dirty = true;
        this.Update();
    }





    /// <summary>
    /// Загружает точки подключения из JSON атрибутов блока с учетом поворота модели
    /// </summary>
    public void LoadWireNodes()
    {
        
        _wireNodes.Clear();

        var wireNodesAttribute = this.Block?.Attributes?["wireNodes"];
        if (wireNodesAttribute == null) return;

        // Вместо использования AsArray() работаем напрямую с JToken
        var token = wireNodesAttribute.Token;
        if (!(token is JArray wireNodesArray) || wireNodesArray.Count == 0)
            return;

        // Получаем угол поворота модели (по умолчанию 0)
        float rotateY = 0;
        if (this.Block?.Shape != null)
        {
            rotateY = this.Block.Shape.rotateY;
        }


        // Конвертируем угол поворота в радианы
        double angleRad = (360 - rotateY) * GameMath.DEG2RAD;
        double cosAngle = Math.Cos(angleRad);
        double sinAngle = Math.Sin(angleRad);

        // Центр блока для поворота (0.5, 0.5, 0.5 в локальных координатах)
        double centerX = 0.5;
        double centerZ = 0.5;

        // Используем Capacity для предварительного выделения памяти
        _wireNodes.Capacity = wireNodesArray.Count;

        // Обходим массив напрямую без создания промежуточного массива
        foreach (JToken nodeToken in wireNodesArray)
        {
            if (nodeToken == null)
                continue;

            try
            {
                // Загружаем исходные координаты из JSON напрямую из JToken
                double x = nodeToken["x"]?.Value<double>() ?? 0;
                double y = nodeToken["y"]?.Value<double>() ?? 0;
                double z = nodeToken["z"]?.Value<double>() ?? 0;

                // Применяем поворот вокруг центра блока
                if (rotateY != 0)
                {
                    // Смещаем координаты относительно центра
                    double xRel = x - centerX;
                    double zRel = z - centerZ;

                    // Поворачиваем координаты
                    double xRotated = xRel * cosAngle - zRel * sinAngle;
                    double zRotated = xRel * sinAngle + zRel * cosAngle;

                    // Возвращаем обратно в систему координат блока
                    x = xRotated + centerX;
                    z = zRotated + centerZ;
                }

                var wireNode = new WireNode
                {
                    Index = (byte)(nodeToken["index"]?.Value<int>() ?? 0),
                    Voltage = nodeToken["voltage"]?.Value<int>() ?? 0,
                    Position = new Vec3d(x, y, z),
                    Radius = nodeToken["dxdydz"]?.Value<float>() ?? 0.1f
                };

                _wireNodes.Add(wireNode);
            }
            catch (Exception ex)
            {
                //this.Api?.Logger.Warning($"Failed to load wire node for block {Block?.Code} at {Blockentity.Pos}: {ex.Message}");
            }
        }

        // Сортируем по индексу
        _wireNodes.Sort((a, b) => a.Index.CompareTo(b.Index));

        // иммерсивная система?
        if (Block is ImmersiveWireBlock wireBlock)
        {
            // Обновляем меши при загрузке
            if (Api.Side == EnumAppSide.Client)
            {
                ImmersiveWireBlock.InvalidateBlockMeshCache(Pos);

            }
        }
    }





    private void GetParticles()
    {
        ParticlesType = MyMiniLib.GetAttributeInt(this.Block, "particlesType", 0);

        ParticlesOffsetPos.Clear();
        var arrayOffsetPos = MyMiniLib.GetAttributeArrayArrayFloat(this.Block, "particlesOffsetPos", new float[1][] { [0, 0, 0] });
        if (arrayOffsetPos != null)
        {
            for (int i = 0; i < arrayOffsetPos.Length; i++)
            {
                Vec3d buf = new Vec3d(
                    arrayOffsetPos[i][0],
                    arrayOffsetPos[i][1],
                    arrayOffsetPos[i][2]
                );
                ParticlesOffsetPos.Add(buf);
            }
        }

        ParticlesFramesAnim.Clear();
        var arrayFrames = MyMiniLib.GetAttributeArrayArrayInt(this.Block, "particlesFramesAnim", new int[1][] { [-1, -1] });
        if (arrayFrames != null)
        {
            for (int i = 0; i < arrayFrames.Length; i++)
            {
                int[] buf = [arrayFrames[i][0], arrayFrames[i][1]];
                ParticlesFramesAnim.Add(buf);
            }
        }
    }

    private bool OnAsyncParticles(float dt, IAsyncParticleManager manager)
    {
        if (!this._isLoaded || _connections.Count == 0)
            return true;

        // Логика частиц остается прежней
        return this._isLoaded;
    }



    public void Update(bool force = false)
    {
        if (!this._dirty && !force)
            return;

        var system = this.System;
        if (system is null)
        {
            this._dirty = true;
            return;
        }

        this._dirty = false;

        this._consumer = null;
        this._conductor = null;
        this._producer = null;
        this._accumulator = null;
        this._transformator = null;

        foreach (var entityBehavior in this.Blockentity.Behaviors)
        {
            switch (entityBehavior)
            {
                case IEImmersiveConsumer { } consumer:
                    this._consumer = consumer;
                    break;
                case IEImmersiveProducer { } producer:
                    this._producer = producer;
                    break;
                case IEImmersiveAccumulator { } accumulator:
                    this._accumulator = accumulator;
                    break;
                case IEImmersiveTransformator { } transformator:
                    this._transformator = transformator;
                    break;
                case IEImmersiveConductor { } conductor:
                    this._conductor = conductor;
                    break;
            }
        }

        system.SetConductor(this.Blockentity.Pos, this._conductor);
        system.SetConsumer(this.Blockentity.Pos, this._consumer);
        system.SetProducer(this.Blockentity.Pos, this._producer);
        system.SetAccumulator(this.Blockentity.Pos, this._accumulator);
        system.SetTransformator(this.Blockentity.Pos, this._transformator);

        var currentEpar = this._paramsSet ? _eparams : (new(), 0);


        if (system.Update(this.Blockentity.Pos, _wireNodes, ref _connections, _mainEpar, currentEpar, _isLoaded))
        {
            // ВСЕГДА обновляем меш текущего блока
            if (Api.Side == EnumAppSide.Client && Block is ImmersiveWireBlock wireBlock)
            {

                // Обновляем точки крепления
                //wireBlock.UpdateWireNodes(_wireNodes);

                // Откладываем обновление меша на следующий кадр
                Api.Event.EnqueueMainThreadTask(() =>
                {
                    ImmersiveWireBlock.InvalidateBlockMeshCache(Pos);

                    // Также перерисовываем все соседние блоки, но только те, что уже загружены
                    // Это нужно для того, чтобы провода отображались с обеих сторон
                    foreach (var data in _connections)
                    {
                        // Быстрая проверка: есть ли блок на этой позиции
                        var block = Api.World.BlockAccessor.GetBlock(data.NeighborPos);
                        if (block != null && block is ImmersiveWireBlock)
                        {
                            ImmersiveWireBlock.InvalidateBlockMeshCache(data.NeighborPos);
                        }
                    }
                }, "update-wire-meshes");
            }

            try
            {
                this.Blockentity.MarkDirty(true);
            }
            catch { }
        }
    }


    /// <summary>
    /// При удалении блока
    /// </summary>
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        this._isLoaded = false;

        // Запоминаем сети, в которых был блок
        var affectedNetworks = System.Networks.Where(n => n.PartPositions.Contains(Pos)).ToList();

        // рвем все соединения
        RemoveConnAndDrop();

        // удаляем подключение в системе
        this.System?.Remove(this.Blockentity.Pos);

        // Проверяем разделение для каждой затронутой сети
        foreach (var network in affectedNetworks)
        {
            System.CheckAndSplitNetwork(network);
        }

        AnimUtil?.Dispose();

        // Обновляем меши проводоа
        if (Api.Side == EnumAppSide.Client && Block is ImmersiveWireBlock wireBlock)
        {
            ImmersiveWireBlock.InvalidateBlockMeshCache(Pos);
        }
    }

    /// <summary>
    /// Разрываем все подключения в этой точке
    /// </summary>
    private void RemoveConnAndDrop()
    {
        // При удалении блока разрываем все соединения и возвращаем кабели

        if (this != null)
        {
            var connections = this.GetImmersiveConnections();
            foreach (ConnectionData connection in connections)
            {

                // роняем провода только на сервере
                if (Api.World.Side == EnumAppSide.Server)
                {
                    int cableLength = (int)Math.Ceiling(connection.WireLength);


                    // Создаем и выбрасываем кабель
                    var cableStack = ImmersiveWireBlock.CreateCableStack(Api, connection.Parameters);

                    cableStack.StackSize = cableLength;

                    Api.World.SpawnItemEntity(cableStack, Pos.ToVec3d());


                }

                var neighborEntity = Api.World.BlockAccessor.GetBlockEntity(connection.NeighborPos);
                var neighborBehavior = neighborEntity?.GetBehavior<BEBehaviorEPImmersive>();

                // Удаляем соединение с соседней стороны
                neighborBehavior?.RemoveConnection(
                    connection.NeighborNodeIndex,
                    Pos,
                    connection.LocalNodeIndex
                );



            }
        }
    }


    /// <summary>
    /// При выгрузке блока очищаем анимации и обновляем сеть
    /// </summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        this._isLoaded = false;
        this._dirty = true;


        this.Update();
        AnimUtil?.Dispose();

        // Optional: If part has no connections and unloaded, remove from Parts (cleanup)
        if (System.Parts.TryGetValue(Pos, out var part) && part.Connections.Count == 0 && !part.IsLoaded)
        {
            System.Parts.Remove(Pos);
        }

        // Обновляем меши проводоа
        if (Api.Side == EnumAppSide.Client && Block is ImmersiveWireBlock wireBlock)
        {
            ImmersiveWireBlock.InvalidateBlockMeshCache(Pos);
        }
    }

    /// <summary>
    /// Информация о блоке
    /// </summary>
    /// <param name="forPlayer"></param>
    /// <param name="stringBuilder"></param>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
    {
        base.GetBlockInfo(forPlayer, stringBuilder);

        if (Api is not ICoreClientAPI capi)
            return;

        // Отправляем запрос на сервер раз в секунду
        if ((DateTime.Now - lastRequestTime).TotalSeconds >= 1.0)
        {
            capi.Network.SendBlockEntityPacket(Pos, PacketIdRequestNetworkInfo, null);
            lastRequestTime = DateTime.Now;
        }

        // Если нет информации, показываем сообщение
        if (networkInformation == null)
        {
            stringBuilder.AppendLine(Lang.Get("electricalprogressivebasics:WaitingNetworkInfo"));
            return;
        }

        // Проверяем нажатие Alt для подробной информации
        var altPressed = capi.Input.IsHotKeyPressed("AltPressForNetwork");
        var nameAltPressed = capi.Input.GetHotKeyByCode("AltPressForNetwork")?.CurrentMapping.ToString() ?? "Alt";

        if (!altPressed)
        {
            stringBuilder.AppendLine(Lang.Get("electricalprogressivebasics:PressForDetails", nameAltPressed));
            stringBuilder.AppendLine("├ " + Lang.Get("electricalprogressivebasics:ConnectedWires", networkInformation.NumberOfConnections));
            return;
        }

        // Подробная информация
        stringBuilder.AppendLine(Lang.Get("electricalprogressivebasics:NetworkInfo"));
        stringBuilder.AppendLine("├ " + Lang.Get("electricalprogressivebasics:ConnectedWires", networkInformation.NumberOfConnections));

        // Информация о блоке
        stringBuilder.AppendLine(Lang.Get("electricalprogressivebasics:BlockParameters"));
        stringBuilder.AppendLine("  ├ " + Lang.Get("electricalprogressivebasics:MaxCurrent") + ": " +
            (networkInformation.eParamsInNetwork.maxCurrent * networkInformation.eParamsInNetwork.lines) +
            " " + Lang.Get("electricalprogressivebasics:A"));
        stringBuilder.AppendLine("  ├ " + Lang.Get("electricalprogressivebasics:Current") + ": " +
            Math.Abs(networkInformation.current).ToString("F3") +
            " " + Lang.Get("electricalprogressivebasics:A"));
        stringBuilder.AppendLine("  ├ " + Lang.Get("electricalprogressivebasics:MaxVoltage") + ": " +
            networkInformation.eParamsInNetwork.voltage +
            " " + Lang.Get("electricalprogressivebasics:V"));
        stringBuilder.AppendLine("  ├ " + Lang.Get("electricalprogressivebasics:Resistivity") + ": " +
            networkInformation.eParamsInNetwork.resistivity.ToString("F3") +
            " " + Lang.Get("electricalprogressivebasics:OmLine"));

        if (networkInformation.IsConductorOpen)
            stringBuilder.AppendLine("  ├ " + Lang.Get("electricalprogressivebasics:ConductorOpen"));
        else
        {
            stringBuilder.AppendLine("  ├ " + Lang.Get("electricalprogressivebasics:ConductorClosed"));
        }

        // Информация по каждой сети
        for (int i = 0; i < networkInformation.Networks.Count; i++)
        {
                var network = networkInformation.Networks[i];
                stringBuilder.AppendLine(Lang.Get("electricalprogressivebasics:NetworkStatus"));
                stringBuilder.AppendLine("  ├ " + Lang.Get("electricalprogressivebasics:GeneratorsShort") + ": " + network.NumberOfProducers);
                stringBuilder.AppendLine("  ├ " + Lang.Get("electricalprogressivebasics:ConsumersShort") + ": " + network.NumberOfConsumers);
                stringBuilder.AppendLine("  ├ " + Lang.Get("electricalprogressivebasics:BatteriesShort") + ": " + network.NumberOfAccumulators);
                stringBuilder.AppendLine("  ├ " + Lang.Get("electricalprogressivebasics:TransformersShort") + ": " + network.NumberOfTransformators);
                stringBuilder.AppendLine("  ├ " + Lang.Get("electricalprogressivebasics:ConductorsShort") + ": " + network.NumberOfConductors);
                stringBuilder.AppendLine("  ├ " + Lang.Get("electricalprogressivebasics:Generation") + ": " + network.Production.ToString("F1") + " " + Lang.Get("electricalprogressivebasics:W"));
                stringBuilder.AppendLine("  ├ " + Lang.Get("electricalprogressivebasics:Consumption") + ": " + network.Consumption.ToString("F1") + " " + Lang.Get("electricalprogressivebasics:W"));
                stringBuilder.AppendLine("  ├ " + Lang.Get("electricalprogressivebasics:Request") + ": " + network.Request.ToString("F1") + " " + Lang.Get("electricalprogressivebasics:W"));

                if (network.MaxCapacity > 0)
                {
                    float capacityPercent = (network.Capacity / network.MaxCapacity) * 100f;
                    stringBuilder.AppendLine("  └ " + Lang.Get("electricalprogressivebasics:Capacity") + ": " +
                        network.Capacity.ToString("F0") + "/" + network.MaxCapacity.ToString("F0") + " " +
                        Lang.Get("electricalprogressivebasics:J") + " (" + capacityPercent.ToString("F1") + "%)");
                }
                else
                {
                    stringBuilder.AppendLine("  └ " + Lang.Get("electricalprogressivebasics:Capacity") + ": 0/0 " +
                        Lang.Get("electricalprogressivebasics:J") + " (0.0%)");
                }
            }

        // Общая сводка (только если больше одной сети)
        if (networkInformation.Networks.Count > 1)
        {
            stringBuilder.AppendLine(Lang.Get("electricalprogressivebasics:Summary"));
            stringBuilder.AppendLine("  ├ " + Lang.Get("electricalprogressivebasics:TotalBlocks") + ": " + networkInformation.NumberOfBlocks);
            stringBuilder.AppendLine("  ├ " + Lang.Get("electricalprogressivebasics:TotalGenerators") + ": " + networkInformation.NumberOfProducers);
            stringBuilder.AppendLine("  ├ " + Lang.Get("electricalprogressivebasics:TotalConsumers") + ": " + networkInformation.NumberOfConsumers);
            stringBuilder.AppendLine("  ├ " + Lang.Get("electricalprogressivebasics:TotalBatteries") + ": " + networkInformation.NumberOfAccumulators);
            stringBuilder.AppendLine("  ├ " + Lang.Get("electricalprogressivebasics:TotalProduction") + ": " + networkInformation.Production.ToString("F1") + " " + Lang.Get("electricalprogressivebasics:W"));
            stringBuilder.AppendLine("  └ " + Lang.Get("electricalprogressivebasics:TotalConsumption") + ": " + networkInformation.Consumption.ToString("F1") + " " + Lang.Get("electricalprogressivebasics:W"));
        }
    }


    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(fromPlayer, packetid, data);

        if (packetid == PacketIdRequestNetworkInfo)
        {
            // Получаем информацию о сети
            var info = System?.GetNetworkForImmersiveWire(Pos);
            if (info != null)
            {
                // Используем кастомный сериализатор
                var serializedData = ImmersiveNetworkInformationSerializer.Serialize(info);
                (Api as ICoreServerAPI)?.Network.SendBlockEntityPacket(fromPlayer as IServerPlayer,
                    Pos, PacketIdResponseNetworkInfo, serializedData);
            }
        }
    }

    public override void OnReceivedServerPacket(int packetid, byte[] data)
    {
        base.OnReceivedServerPacket(packetid, data);

        if (packetid == PacketIdResponseNetworkInfo)
        {
            // Используем кастомный десериализатор
            networkInformation = ImmersiveNetworkInformationSerializer.Deserialize(data);
            // Обновляем отображение
            Blockentity.MarkDirty();
        }
    }


    /// <summary>
    /// Сохраняем текущие важные параметры в сейв
    /// </summary>
    /// <param name="tree"></param>
    /// <summary>
    /// Сохраняем текущие важные параметры в сейв
    /// </summary>
    /// <param name="tree"></param>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        // Сохраняем соединения одним массивом
        if (_connections.Count > 0)
        {
            var connectionsData = ConnectionDataSerializer.SerializeConnections(_connections, Pos);
            tree.SetBytes("ConnectionsData", connectionsData);
        }
        else
        {
            tree.RemoveAttribute("ConnectionsData");
        }

        // Сохраняем параметры текущего блока
        tree.SetBytes("MainEpar", EParamsSerializer.SerializeSingle(_mainEpar));

        // Сохраняем узлы подключения в новом бинарном формате
        if (_wireNodes.Count > 0)
        {
            var wireNodesData = WireNodeSerializer.SerializeWireNodes(_wireNodes);
            tree.SetBytes("WireNodesData", wireNodesData);
        }
        else
        {
            tree.RemoveAttribute("WireNodesData");
        }


        // Сохраняем параметры частиц
        tree.SetInt("ParticlesType", ParticlesType);

        tree.SetInt("ParticlesOffsetPosCount", ParticlesOffsetPos.Count);
        for (int i = 0; i < ParticlesOffsetPos.Count; i++)
        {
            tree.SetDouble($"ParticlesOffsetPosX_{i}", ParticlesOffsetPos[i].X);
            tree.SetDouble($"ParticlesOffsetPosY_{i}", ParticlesOffsetPos[i].Y);
            tree.SetDouble($"ParticlesOffsetPosZ_{i}", ParticlesOffsetPos[i].Z);
        }

        tree.SetInt("ParticlesFramesAnimCount", ParticlesFramesAnim.Count);
        for (int i = 0; i < ParticlesFramesAnim.Count; i++)
        {
            tree.SetInt($"ParticlesFramesAnimMin_{i}", ParticlesFramesAnim[i][0]);
            tree.SetInt($"ParticlesFramesAnimMax_{i}", ParticlesFramesAnim[i][1]);
        }
    }

    /// <summary>
    /// Грузим из сейва текущие важные параметры
    /// </summary>
    /// <param name="tree"></param>
    /// <param name="worldAccessForResolve"></param>
    /// <summary>
    /// Грузим из сейва текущие важные параметры
    /// </summary>
    /// <param name="tree"></param>
    /// <param name="worldAccessForResolve"></param>
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        // Загружаем соединения (новый формат)
        _connections.Clear();
        var connectionsData = tree.GetBytes("ConnectionsData");
        if (connectionsData != null && connectionsData.Length > 0)
        {
            _connections = ConnectionDataSerializer.DeserializeConnections(connectionsData, Pos);
        }

        // Загружаем параметры текущего блока
        var mainEparData = tree.GetBytes("MainEpar");
        _mainEpar = mainEparData != null
            ? EParamsSerializer.DeserializeSingle(mainEparData)
            : new EParams();

        // Загружаем узлы подключения (новый бинарный формат)
        var wireNodesData = tree.GetBytes("WireNodesData");
        if (wireNodesData != null && wireNodesData.Length > 0)
        {
            try
            {
                var loadedNodes = WireNodeSerializer.DeserializeWireNodes(wireNodesData);
                if (loadedNodes != null && loadedNodes.Count > 0)
                {
                    _wireNodes = loadedNodes;
                }

            }
            catch
            {

            }
        }


        // Загрузка параметров частиц
        ParticlesType = tree.GetInt("ParticlesType", 0);

        int count = tree.GetInt("ParticlesOffsetPosCount", 0);
        ParticlesOffsetPos = new(count);
        for (int i = 0; i < count; i++)
        {
            double x = tree.GetDouble($"ParticlesOffsetPosX_{i}", 0.0);
            double y = tree.GetDouble($"ParticlesOffsetPosY_{i}", 0.0);
            double z = tree.GetDouble($"ParticlesOffsetPosZ_{i}", 0.0);
            ParticlesOffsetPos.Add(new Vec3d(x, y, z));
        }

        count = tree.GetInt("ParticlesFramesAnimCount", 0);
        ParticlesFramesAnim = new(count);
        for (int i = 0; i < count; i++)
        {
            int min = tree.GetInt($"ParticlesFramesAnimMin_{i}", -1);
            int max = tree.GetInt($"ParticlesFramesAnimMax_{i}", -1);
            ParticlesFramesAnim.Add([min, max]);
        }

        this._dirty = true;
        this.Update();
    }

   




}