using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    

    /// <summary>
    /// Состояние узла в поиске пути (позиция + индекс точки подключения)
    /// </summary>
    public struct NodeState : IEquatable<NodeState>
    {
        public FastPosKey Position;
        public byte NodeIndex;

        public NodeState(FastPosKey position, byte nodeIndex)
        {
            Position = position;
            NodeIndex = nodeIndex;
        }

        public bool Equals(NodeState other) =>
            Position.Equals(other.Position) && NodeIndex == other.NodeIndex;

        public override bool Equals(object obj) => obj is NodeState other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return Position.GetHashCode() * 397 ^ NodeIndex.GetHashCode();
            }
        }
    }

    /// <summary>
    /// Поисковик путей для иммерсивной электрической сети
    /// Использует алгоритм A* для нахождения кратчайшего пути между двумя точками в сети
    /// Вес пути - длина провода (WireLength), а не фиксированное значение
    /// </summary>
    public class ImmersivePathFinder
    {
        private PriorityQueue<NodeState, int> _queue = new();
        private Dictionary<NodeState, NodeState> _cameFrom = new();
        private Dictionary<NodeState, int> _costSoFar = new();
        private HashSet<NodeState> _visited = [];

        // Буфер для хранения соседних узлов и весов переходов к ним
        private List<(NodeState state, int cost)> _neighborsBuffer = new(10);

        /// <summary>
        /// Очищает все внутренние структуры для нового поиска
        /// </summary>
        public void Clear()
        {
            _queue?.Clear();
            _cameFrom?.Clear();
            _costSoFar?.Clear();
            _visited?.Clear();
        }

        /// <summary>
        /// Эвристическая функция для A* (манхэттенское расстояние)
        /// Используется для оценки оставшегося пути до цели
        /// </summary>
        public static int Heuristic(BlockPos a, BlockPos b)
            => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);

        /// <summary>
        /// Находит кратчайший путь между двумя позициями в сети
        /// </summary>
        /// <param name="start">Начальная позиция</param>
        /// <param name="end">Конечная позиция</param>
        /// <param name="immersiveNetwork">Сеть для поиска</param>
        /// <param name="parts">Словарь частей сети</param>
        /// <returns>Кортеж (массив позиций пути, массив индексов узлов, суммарная длина пути)</returns>
        public (BlockPos[], byte[], float) FindShortestPath(
            BlockPos start, BlockPos end,
            ImmersiveNetwork immersiveNetwork,
            Dictionary<BlockPos, ImmersiveNetworkPart> parts)
        {
            Clear();

            var startKey = new FastPosKey(start.X, start.Y, start.Z, start.dimension, start);
            var endKey = new FastPosKey(end.X, end.Y, end.Z, end.dimension, end);

            // Проверяем, что обе позиции существуют в сети
            if (!parts.TryGetValue(start, out var startPart) ||
                !parts.TryGetValue(end, out var endPart))
                return (null, null, 0f);

            // Добавляем все стартовые узлы в очередь с нулевой стоимостью
            foreach (var wireNode in startPart.WireNodes)
            {
                var startState = new NodeState(startKey, wireNode.Index);
                _queue.Enqueue(startState, 0);
                _cameFrom[startState] = new NodeState();
                _costSoFar[startState] = 0;
            }

            var pathLength = 0f;

            while (_queue.Count > 0)
            {
                var current = _queue.Dequeue();

                // Если достигли цели - восстанавливаем путь
                if (current.Position.Equals(endKey))
                {
                    var (path, nodeIndices) = ReconstructPath(current);
                    // Рассчитываем фактическую длину пути
                    pathLength = CalculatePathLength(path, nodeIndices, immersiveNetwork, parts);
                    return (path, nodeIndices, pathLength);
                }

                if (_visited.Contains(current))
                    continue;

                _visited.Add(current);

                // Получаем соседей текущего узла с весами переходов
                GetNeighbors(current, immersiveNetwork, parts, _neighborsBuffer);

                foreach (var neighbor in _neighborsBuffer)
                {
                    if (_visited.Contains(neighbor.state))
                        continue;

                    // Используем длину провода как стоимость перехода (округляем до int для очереди)
                    var newCost = _costSoFar[current] + neighbor.cost;

                    if (!_costSoFar.TryGetValue(neighbor.state, out var value) || newCost < value)
                    {
                        value = newCost;
                        _costSoFar[neighbor.state] = value;
                        // Приоритет = стоимость пути + эвристика до цели
                        var priority = newCost + Heuristic(neighbor.state.Position.Pos, end);
                        _queue.Enqueue(neighbor.state, priority);
                        _cameFrom[neighbor.state] = current;
                    }
                }

                _neighborsBuffer?.Clear();
            }

            // Путь не найден
            return (null, null, 0f);
        }

        /// <summary>
        /// Рассчитывает фактическую длину пути на основе WireLength соединений
        /// </summary>
        private static float CalculatePathLength(BlockPos[] path, byte[] nodeIndices,
            ImmersiveNetwork network, Dictionary<BlockPos, ImmersiveNetworkPart> parts)
        {
            if (path == null || nodeIndices == null || path.Length <= 1)
                return 0f;

            var totalLength = 0f;

            for (var i = 0; i < path.Length - 1; i++)
            {
                var currentPos = path[i];
                var nextPos = path[i + 1];
                var currentNodeIndex = nodeIndices[i];
                var nextNodeIndex = nodeIndices[i + 1];

                // Ищем соединение между текущим и следующим блоком
                if (parts.TryGetValue(currentPos, out var currentPart))
                {
                    foreach (var connection in currentPart.Connections)
                    {
                        if (connection.LocalNodeIndex == currentNodeIndex &&
                            connection.NeighborPos.Equals(nextPos) &&
                            connection.NeighborNodeIndex == nextNodeIndex)
                        {
                            totalLength += connection.WireLength;
                            break;
                        }
                    }
                }
            }

            return totalLength;
        }

        /// <summary>
        /// Получает всех соседей текущего узла с весами переходов
        /// </summary>
        /// <param name="current">Текущий узел</param>
        /// <param name="network">Сеть</param>
        /// <param name="parts">Части сети</param>
        /// <param name="neighbors">Список для заполнения (сосед, стоимость перехода)</param>

        private void GetNeighbors(
            NodeState current,
            ImmersiveNetwork network,
            Dictionary<BlockPos, ImmersiveNetworkPart> parts,
            List<(NodeState state, int cost)> neighbors)
        {
            if (!parts.TryGetValue(current.Position.Pos, out var currentPart))
                return;

            // Ищем исходящие соединения из текущего положения
            bool allConn;
            foreach (var connection in currentPart.Connections)
            {
                allConn = false;
                // если проводник замкнут
                if (currentPart.Conductor != null && !currentPart.Conductor.IsOpen)
                    allConn = true;

                if (allConn || connection.LocalNodeIndex == current.NodeIndex)
                {
                    var neighborKey = new FastPosKey(
                        connection.NeighborPos.X, connection.NeighborPos.Y,
                        connection.NeighborPos.Z, connection.NeighborPos.dimension,
                        connection.NeighborPos);

                    var neighborState = new NodeState(neighborKey, connection.NeighborNodeIndex);

                    // Проверяем, что соседняя позиция находится в сети
                    if (network.PartPositions.Contains(connection.NeighborPos))
                    {
                        // Используем длину провода как стоимость перехода
                        // Округляем вверх, так как длина провода обычно дробная
                        var cost = (int)Math.Ceiling(connection.WireLength);
                        neighbors.Add((neighborState, cost));
                    }
                }
            }

            
        }

        /// <summary>
        /// Восстанавливает путь от конечного узла к начальному
        /// </summary>
        /// <param name="endState">Конечный узел</param>
        /// <returns>Кортеж (путь позиций, индексы узлов)</returns>
        private (BlockPos[], byte[]) ReconstructPath(NodeState endState)
        {
            var path = new List<BlockPos>();
            var nodeIndices = new List<byte>();

            var current = endState;

            // Проходим по цепочке предков от конца к началу
            while (!current.Equals(new NodeState()))
            {
                path.Add(current.Position.Pos);
                nodeIndices.Add(current.NodeIndex);

                if (!_cameFrom.TryGetValue(current, out current))
                    break;
            }

            // Переворачиваем, чтобы получить путь от начала к концу
            path.Reverse();
            nodeIndices.Reverse();

            return (path.ToArray(), nodeIndices.ToArray());
        }
    }
}