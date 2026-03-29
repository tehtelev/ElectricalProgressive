using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.NetworkPipe;

public class PipeNetworkManager
{
    private ICoreAPI api;
    private Dictionary<long, PipeNetwork> networks = new ();
    private Dictionary<BlockPos, long> pipeToNetwork = new ();
    private long nextNetworkId = 1;

    public void Initialize(ICoreAPI api)
    {
        this.api = api;
    }

    public void AddPipe(BlockPos pos, BlockEntity pipe)
    {
        // Ищем соседние сети
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
        }
        else if (adjacentNetworks.Count == 1)
        {
            // Добавляем в существующую сеть
            long networkId = adjacentNetworks[0];
            networks[networkId].AddPipe(pos, pipe);
            pipeToNetwork[pos.Copy()] = networkId;
        }
        else
        {
            // Объединяем сети
            long mainNetworkId = adjacentNetworks[0];
            PipeNetwork mainNetwork = networks[mainNetworkId];
            mainNetwork.AddPipe(pos, pipe);

            // Объединяем остальные сети
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
        }
    }


    // Метод для удаления трубы и проверки распада сети
    public void RemovePipe(BlockPos pos)
    {
        if (!pipeToNetwork.TryGetValue(pos, out long networkId))
            return;

        pipeToNetwork.Remove(pos);

        if (!networks.TryGetValue(networkId, out PipeNetwork network))
            return;

        network.RemovePipe(pos);

        if (network.Pipes.Count == 0)
        {
            networks.Remove(networkId);
            return;
        }

        // BFS строго по позициям, оставшимся в сети
        var remaining = new HashSet<BlockPos>(network.Pipes);
        var components = new List<HashSet<BlockPos>>();

        while (remaining.Count > 0)
        {
            BlockPos start = null;
            foreach (var p in remaining) { start = p; break; }

            var component = new HashSet<BlockPos>();
            var queue = new Queue<BlockPos>();

            component.Add(start);
            remaining.Remove(start);
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

                    // Проходим ТОЛЬКО по трубам, которые ещё в сети
                    if (remaining.Contains(neighborPos))
                    {
                        component.Add(neighborPos);
                        remaining.Remove(neighborPos);
                        queue.Enqueue(neighborPos);
                    }
                }
            }

            components.Add(component);
        }

        // Сеть не распалась — ничего не делаем
        if (components.Count == 1)
            return;

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
        }
    }

    private bool[] GetConnectedSides(BlockPos pos)
    {
        var entity = api.World.BlockAccessor.GetBlockEntity(pos);
        if (entity is BEPipe pipe)
            return pipe.ConnectedSides;
        if (entity is BlockEntityPipeBase inserter)
            return inserter.ConnectedSides;
        return null;
    }

    /*
    private List<BlockPos> FindConnectedComponent(BlockPos startPos)
    {
        List<BlockPos> component = [];
        Queue<BlockPos> queue = new Queue<BlockPos>();
        HashSet<BlockPos> visited = [];

        queue.Enqueue(startPos);
        visited.Add(startPos);

        while (queue.Count > 0)
        {
            BlockPos current = queue.Dequeue();
            component.Add(current);

            BlockEntity pipeEntity = api.World.BlockAccessor.GetBlockEntity(current);

            bool[] connectedSides = null;
            if (pipeEntity is BEPipe pipe)
            {
                connectedSides = pipe.ConnectedSides;
            }
            else if (pipeEntity is BlockEntityPipeBase inserter)
            {
                connectedSides = inserter.ConnectedSides;
            }

            if (connectedSides == null)
                continue;

            for (int i = 0; i < 6; i++)
            {
                if (connectedSides[i])
                {
                    BlockFacing facing = BlockFacing.ALLFACES[i];
                    BlockPos neighborPos = current.AddCopy(facing);

                    if (!visited.Contains(neighborPos))
                    {
                        visited.Add(neighborPos);
                        queue.Enqueue(neighborPos);
                    }
                }
            }
        }

        return component;
    }

    */


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

    public List<BlockPos> GetInsertersInNetwork(BlockPos pipePos)
    {
        var network = GetNetwork(pipePos);
        return network?.Inserters ?? [];
    }

    public int GetNetworkCount()
    {
        return networks.Count;
    }
}