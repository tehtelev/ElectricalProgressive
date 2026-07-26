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
        HashSet<long> adjacentNetworks = [];

        for (int i = 0; i < 6; i++)
        {
            BlockFacing facing = BlockFacing.ALLFACES[i];
            BlockPos neighborPos = pos.AddCopy(facing);

            if (pipeToNetwork.TryGetValue(neighborPos, out long networkId))
                adjacentNetworks.Add(networkId);
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
            long networkId = First(adjacentNetworks);
            networks[networkId].AddPipe(pos, pipe);
            pipeToNetwork[pos.Copy()] = networkId;
            RefreshLocalEndpointCache(networkId, pos);
        }
        else
        {
            // Объединяем сети через главную
            long mainNetworkId = First(adjacentNetworks);
            PipeNetwork mainNetwork = networks[mainNetworkId];
            mainNetwork.AddPipe(pos, pipe);

            foreach (long otherId in adjacentNetworks)
            {
                if (otherId == mainNetworkId)
                    continue;

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
    /// Удаляет трубу и проверяет, не распалась ли сеть на компоненты.
    /// When wrench-swapping pipe type, the old BE is removed after the new BE already
    /// registered at the same pos — must NOT unregister the new one or split the network.
    /// </summary>
    public void RemovePipe(BlockPos pos, BlockEntity? caller = null)
    {
        // Same-cell replace (wrench): current BE at pos is already the new pipe.
        if (caller != null && api?.World != null)
        {
            var current = api.World.BlockAccessor.GetBlockEntity(pos);
            if (current != null && !ReferenceEquals(current, caller)
                && current is BEPipe or BlockEntityPipeBase)
            {
                // Leave network registration owned by the new entity.
                return;
            }
        }

        if (!TryGetNetworkId(pos, out long networkId))
            return;

        if (!networks.TryGetValue(networkId, out PipeNetwork network))
            return;

        var adjacentPipes = GetAdjacentPipesInNetwork(pos, network);

        RemoveMapKey(pos);
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

    /// <summary>
    /// Drop this position from the network map without splitting components, then re-add.
    /// Needed after wrench SetBlock: old BE.OnBlockRemoved(RemovePipe) can run after the new
    /// BE already called AddPipe, wiping the new registration. Place-fresh pipes are fine;
    /// transform must re-register.
    /// </summary>
    public void ReregisterPipe(BlockPos pos, BlockEntity pipe)
    {
        if (pos == null || pipe == null || api == null)
            return;

        SoftUnregister(pos);
        AddPipe(pos, pipe);

        if (TryGetNetworkId(pos, out long networkId)
            && networks.TryGetValue(networkId, out PipeNetwork network))
        {
            // Full rebuild: type swap changes which cells are inserters/sources/sinks.
            network.RebuildEndpointCache(api);
        }
    }

    /// <summary>Remove pipe from maps/sets only — do not split the network.</summary>
    private void SoftUnregister(BlockPos pos)
    {
        if (!TryGetNetworkId(pos, out long networkId))
            return;

        RemoveMapKey(pos);

        if (!networks.TryGetValue(networkId, out PipeNetwork network))
            return;

        network.RemovePipe(pos);
        if (network.Pipes.Count == 0)
            networks.Remove(networkId);
    }

    private bool TryGetNetworkId(BlockPos pos, out long networkId)
    {
        if (pipeToNetwork.TryGetValue(pos, out networkId))
            return true;

        foreach (var kv in pipeToNetwork)
        {
            if (kv.Key.Equals(pos))
            {
                networkId = kv.Value;
                return true;
            }
        }

        networkId = 0;
        return false;
    }

    private void RemoveMapKey(BlockPos pos)
    {
        if (pipeToNetwork.Remove(pos))
            return;

        BlockPos found = null;
        foreach (var kv in pipeToNetwork)
        {
            if (kv.Key.Equals(pos))
            {
                found = kv.Key;
                break;
            }
        }

        if (found != null)
            pipeToNetwork.Remove(found);
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

        for (int i = 0; i < 6; i++)
        {
            BlockPos neighborPos = pos.AddCopy(BlockFacing.ALLFACES[i]);
            if (network.Pipes.Contains(neighborPos))
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
        if (!TryGetNetworkId(pipePos, out long networkId))
            return null;

        return networks.TryGetValue(networkId, out PipeNetwork network) ? network : null;
    }

    /// <summary>
    /// Возвращает список инсертеров в сети
    /// </summary>
    public List<BlockPos> GetInsertersInNetwork(BlockPos pipePos)
    {
        var network = GetNetwork(pipePos);
        return network != null ? new List<BlockPos>(network.Inserters) : [];
    }

    /// <summary
    /// >Возвращает количество активных сетей
    /// </summary>
    public int GetNetworkCount()
    {
        return networks.Count;
    }

    private static long First(HashSet<long> values)
    {
        foreach (long value in values)
            return value;

        return 0;
    }
}
