using ElectricalProgressive.Content.ItemInsertionPipe;
using ElectricalProgressive.Content.NormalPipe;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
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
        RebuildAllNetworks();
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

    public void RemovePipe(BlockPos pos)
    {
        if (pipeToNetwork.TryGetValue(pos, out long networkId))
        {
            pipeToNetwork.Remove(pos);

            if (networks.TryGetValue(networkId, out PipeNetwork network))
            {
                network.RemovePipe(pos);

                // Проверяем, не развалилась ли сеть на части
                if (network.Pipes.Count > 0)
                {
                    // Ищем разъединенные компоненты
                    var allPipes = new List<BlockPos>(network.Pipes);
                    HashSet<BlockPos> processed = [];

                    foreach (var pipePos in allPipes)
                    {
                        if (!processed.Contains(pipePos))
                        {
                            BlockEntity pipe = api.World.BlockAccessor.GetBlockEntity(pipePos);
                            if (pipe != null)
                            {
                                var component = FindConnectedComponent(pipePos);
                                processed.UnionWith(component);

                                if (component.Count < allPipes.Count)
                                {
                                    // Создаем новую сеть для этого компонента
                                    long newId = nextNetworkId++;
                                    PipeNetwork newNetwork = new PipeNetwork(newId);

                                    foreach (var componentPos in component)
                                    {
                                        BlockEntity componentPipe =
                                            api.World.BlockAccessor.GetBlockEntity(componentPos);
                                        if (componentPipe != null)
                                        {
                                            newNetwork.AddPipe(componentPos, componentPipe);
                                            pipeToNetwork[componentPos] = newId;
                                            network.RemovePipe(componentPos);
                                        }
                                    }

                                    networks[newId] = newNetwork;
                                }
                            }
                        }
                    }
                }

                // Удаляем пустую сеть
                if (network.Pipes.Count == 0)
                {
                    networks.Remove(networkId);
                }
            }
        }
    }

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
            else if (pipeEntity is BEItemInsertionPipe inserter)
            {
                connectedSides = inserter.ConnectedSides;
            }

            if (connectedSides == null) continue;

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

    private void RebuildAllNetworks()
    {
        networks?.Clear();
        pipeToNetwork?.Clear();
        nextNetworkId = 1;

        // Ищем все трубы в мире через проход по чанкам
        var allPipes = new List<BlockPos>();

        // Используем IMapChunk для доступа к чанкам
        int chunkSize = GlobalConstants.ChunkSize;
        int worldSize = api.World.BlockAccessor.MapSizeY / chunkSize;

        // Получаем все загруженные чанки
        // Вместо GetChunks() используем GetMapChunkAtBlockPos для прохода по координатам
        // или используем другой подход

        // Простой подход - проход по всем блокам в радиусе от центра мира
        // Это неэффективно, но работает
        int searchRadius = 1000; // Большой радиус для поиска всех труб

        for (int x = -searchRadius; x <= searchRadius; x += 16)
        {
            for (int z = -searchRadius; z <= searchRadius; z += 16)
            {
                for (int y = 0; y < api.World.BlockAccessor.MapSizeY; y += 16)
                {
                    BlockPos chunkPos = new BlockPos(x, y, z);

                    // Пытаемся получить чанк
                    IMapChunk mapChunk = api.World.BlockAccessor.GetMapChunkAtBlockPos(chunkPos);
                    if (mapChunk != null)
                    {
                        // Проходим по всем блокам в чанке
                        for (int cx = 0; cx < chunkSize; cx++)
                        {
                            for (int cy = 0; cy < chunkSize; cy++)
                            {
                                for (int cz = 0; cz < chunkSize; cz++)
                                {
                                    BlockPos blockPos = new BlockPos(
                                        chunkPos.X + cx,
                                        chunkPos.Y + cy,
                                        chunkPos.Z + cz
                                    );

                                    Vintagestory.API.Common.Block block = api.World.BlockAccessor.GetBlock(blockPos);
                                    if (block is BlockPipeBase)
                                    {
                                        allPipes.Add(blockPos.Copy());
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // Альтернативный, более правильный подход:
        // Используем IWorldChunk если доступно

        /*
        // Получаем размеры мира в чанках
        int chunksX = api.World.BlockAccessor.MapSizeX / chunkSize;
        int chunksZ = api.World.BlockAccessor.MapSizeZ / chunkSize;

        for (int chunkX = 0; chunkX < chunksX; chunkX++)
        {
            for (int chunkZ = 0; chunkZ < chunksZ; chunkZ++)
            {
                for (int chunkY = 0; chunkY < worldSize; chunkY++)
                {
                    // Пытаемся получить чанк
                    IWorldChunk chunk = api.World.BlockAccessor.GetChunk(chunkX, chunkY, chunkZ);
                    if (chunk != null)
                    {
                        // Проходим по всем блокам в чанке
                        for (int lx = 0; lx < chunkSize; lx++)
                        {
                            for (int ly = 0; ly < chunkSize; ly++)
                            {
                                for (int lz = 0; lz < chunkSize; lz++)
                                {
                                    int index = chunk.GetLocalBlockIndex(lx, ly, lz);
                                    int blockId = chunk.Data.GetBlockId(index, 0);

                                    if (blockId != 0)
                                    {
                                        Block block = api.World.GetBlock(blockId);
                                        if (block is BlockPipeBase)
                                        {
                                            BlockPos blockPos = new BlockPos(
                                                chunkX * chunkSize + lx,
                                                chunkY * chunkSize + ly,
                                                chunkZ * chunkSize + lz
                                            );
                                            allPipes.Add(blockPos);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        */

        // Строим сети
        HashSet<BlockPos> processed = [];

        foreach (var pipePos in allPipes)
        {
            if (!processed.Contains(pipePos))
            {
                BEPipe pipe = api.World.BlockAccessor.GetBlockEntity(pipePos) as BEPipe;
                if (pipe != null)
                {
                    var connected = FindConnectedComponent(pipePos);

                    long networkId = nextNetworkId++;
                    PipeNetwork network = new PipeNetwork(networkId);

                    foreach (var connectedPos in connected)
                    {
                        BEPipe connectedPipe =
                            api.World.BlockAccessor.GetBlockEntity(connectedPos) as BEPipe;
                        if (connectedPipe != null)
                        {
                            network.AddPipe(connectedPos, connectedPipe);
                            pipeToNetwork[connectedPos.Copy()] = networkId;
                            processed.Add(connectedPos);
                        }
                    }

                    networks[networkId] = network;
                }
            }
        }
    }

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