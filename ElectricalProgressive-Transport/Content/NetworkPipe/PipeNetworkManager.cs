using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.NetworkPipe;

/// <summary>
/// Управляет множеством сетей труб, объединяет и разделяет их при изменении
/// </summary>
public class PipeNetworkManager
{
    private ICoreAPI api;
    private Dictionary<long, PipeNetwork> networks = [];
    private Dictionary<BlockPos, long> pipeToNetwork = [];
    private long nextNetworkId = 1;

    public void Initialize(ICoreAPI api)
    {
        this.api = api;
    }

    /// <summary>
    /// Добавляет трубу в сеть или объединяет соседние сети
    /// </summary>
    public void AddPipe(BlockPos pos, BlockEntity pipe)
    {
        // Ищем соседние сети по 6 направлениям
        List<long> adjacentNetworks = [];

        for (int i = 0; i < 6; i++)
        {
            BlockFacing facing = BlockFacing.ALLFACES[i];
            BlockPos neighborPos = pos.AddCopy(facing);

            if (pipeToNetwork.TryGetValue(neighborPos, out long networkId))
            {
                if (!adjacentNetworks.Contains(networkId))
                {
                    adjacentNetworks.Add(networkId);
                }
            }
        }

        if (adjacentNetworks.Count == 0)
        {
            // Создаем новую сеть
            long newId = nextNetworkId++;
            PipeNetwork network = new PipeNetwork(newId);
            network.AddPipe(pos, pipe);
            networks[newId] = network;
            pipeToNetwork[pos.Copy()] = newId;
            RefreshLocalEndpointCache(newId, pos);
        }
        else if (adjacentNetworks.Count == 1)
        {
            // Добавляем в существующую сеть
            long networkId = adjacentNetworks[0];
            networks[networkId].AddPipe(pos, pipe);
            pipeToNetwork[pos.Copy()] = networkId;
            RefreshLocalEndpointCache(networkId, pos);
        }
        else
        {
            // Объединяем сети через главную
            long mainNetworkId = adjacentNetworks[0];
            PipeNetwork mainNetwork = networks[mainNetworkId];
            mainNetwork.AddPipe(pos, pipe);

            for (int i = 1; i < adjacentNetworks.Count; i++)
            {
                long otherId = adjacentNetworks[i];
                if (networks.TryGetValue(otherId, out PipeNetwork otherNetwork))
                {
                    mainNetwork.Merge(otherNetwork);

                    // Обновляем mapping для всех труб в объединенной сети
                    foreach (var pipePos in otherNetwork.Pipes)
                    {
                        pipeToNetwork[pipePos] = mainNetworkId;
                    }

                    networks.Remove(otherId);
                }
            }

            pipeToNetwork[pos.Copy()] = mainNetworkId;
            mainNetwork.RebuildEndpointCache(api);
        }
    }

    ///<summary>
    /// Удаляет трубу и проверяет, не распалась ли сеть на компоненты
    /// </summary>
    public void RemovePipe(BlockPos pos)
    {
        if (!pipeToNetwork.TryGetValue(pos, out long networkId))
            return;

        if (!networks.TryGetValue(networkId, out PipeNetwork network))
            return;

        var adjacentPipes = GetAdjacentPipesInNetwork(pos, network);

        pipeToNetwork.Remove(pos);
        network.RemovePipe(pos);

        if (network.Pipes.Count == 0)
        {
            networks.Remove(networkId);
            return;
        }

        if (adjacentPipes.Count <= 1)
        {
            RefreshEndpointCacheForPositions(network, adjacentPipes);
            return;
        }

        var remaining = new HashSet<BlockPos>(network.Pipes);
        var firstComponent = FindComponent(adjacentPipes[0], remaining);

        // Сеть не распалась: один обход достиг всех оставшихся труб.
        if (firstComponent.Count == network.Pipes.Count)
        {
            RefreshEndpointCacheForPositions(network, adjacentPipes);
            return;
        }

        var components = new List<HashSet<BlockPos>> { firstComponent };
        remaining.ExceptWith(firstComponent);

        while (remaining.Count > 0)
        {
            BlockPos start = null;
            foreach (var pipePos in remaining) { start = pipePos; break; }
            var component = FindComponent(start, remaining);
            components.Add(component);
            remaining.ExceptWith(component);
        }

        // Перестраиваем оригинальную сеть под первый компонент
        network.Pipes.Clear();
        network.Inserters.Clear();
        foreach (var pipePos in components[0])
        {
            var pipe = api.World.BlockAccessor.GetBlockEntity(pipePos);
            if (pipe != null)
                network.AddPipe(pipePos, pipe);
            pipeToNetwork[pipePos] = networkId;
        }
        network.RebuildEndpointCache(api);

        // Создаём новые сети для остальных компонентов
        for (int c = 1; c < components.Count; c++)
        {
            long newId = nextNetworkId++;
            PipeNetwork newNetwork = new PipeNetwork(newId);
            networks[newId] = newNetwork;

            foreach (var pipePos in components[c])
            {
                var pipe = api.World.BlockAccessor.GetBlockEntity(pipePos);
                if (pipe != null)
                    newNetwork.AddPipe(pipePos, pipe);
                pipeToNetwork[pipePos] = newId;
            }

            newNetwork.RebuildEndpointCache(api);
        }
    }

    public void RefreshNetworkCache(BlockPos pipePos)
    {
        if (pipeToNetwork.TryGetValue(pipePos, out long networkId))
            RefreshLocalEndpointCache(networkId, pipePos);
    }

    private void RefreshLocalEndpointCache(long networkId, BlockPos pipePos)
    {
        if (!networks.TryGetValue(networkId, out PipeNetwork network))
            return;

        network.RefreshEndpointsForPipe(api, pipePos);

        for (int i = 0; i < 6; i++)
        {
            BlockPos neighborPos = pipePos.AddCopy(BlockFacing.ALLFACES[i]);
            if (pipeToNetwork.TryGetValue(neighborPos, out long neighborNetworkId) && neighborNetworkId == networkId)
                network.RefreshEndpointsForPipe(api, neighborPos);
        }
    }

    private void RefreshEndpointCacheForPositions(PipeNetwork network, List<BlockPos> pipePositions)
    {
        foreach (var pipePos in pipePositions)
        {
            network.RefreshEndpointsForPipe(api, pipePos);
        }
    }

    private List<BlockPos> GetAdjacentPipesInNetwork(BlockPos pos, PipeNetwork network)
    {
        var result = new List<BlockPos>();
        var pipes = new HashSet<BlockPos>(network.Pipes);

        for (int i = 0; i < 6; i++)
        {
            BlockPos neighborPos = pos.AddCopy(BlockFacing.ALLFACES[i]);
            if (pipes.Contains(neighborPos))
                result.Add(neighborPos);
        }

        return result;
    }

    private HashSet<BlockPos> FindComponent(BlockPos start, HashSet<BlockPos> validPipes)
    {
        var component = new HashSet<BlockPos>();
        var queue = new Queue<BlockPos>();

        if (start == null || !validPipes.Contains(start))
            return component;

        component.Add(start);
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            BlockPos current = queue.Dequeue();
            bool[] connectedSides = GetConnectedSides(current);
            if (connectedSides == null) continue;

            for (int i = 0; i < 6; i++)
            {
                if (!connectedSides[i]) continue;

                BlockPos neighborPos = current.AddCopy(BlockFacing.ALLFACES[i]);
                if (validPipes.Contains(neighborPos) && component.Add(neighborPos))
                    queue.Enqueue(neighborPos);
            }
        }

        return component;
    }

    /// <summary>
    /// Возвращает массив подключенных сторон для указанной позиции
    /// </summary>
    private bool[] GetConnectedSides(BlockPos pos)
    {
        var entity = api.World.BlockAccessor.GetBlockEntity(pos);
        if (entity is BEPipe pipe)
            return pipe.ConnectedSides;
        if (entity is BlockEntityPipeBase inserter)
            return inserter.ConnectedSides;
        return null;
    }

    /// <summary>
    /// Возвращает сеть, к которой принадлежит труба
    /// </summary>
    public PipeNetwork GetNetwork(BlockPos pipePos)
    {
        if (pipeToNetwork.TryGetValue(pipePos, out long networkId))
        {
            if (networks.TryGetValue(networkId, out PipeNetwork network))
            {
                return network;
            }
        }

        return null;
    }

    /// <summary>
    /// Возвращает список инсертеров в сети
    /// </summary>
    public List<BlockPos> GetInsertersInNetwork(BlockPos pipePos)
    {
        var network = GetNetwork(pipePos);
        return network?.Inserters ?? [];
    }

    /// <summary
    /// >Возвращает количество активных сетей
    /// </summary>
    public int GetNetworkCount()
    {
        return networks.Count;
    }
}
