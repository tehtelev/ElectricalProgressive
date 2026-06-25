using ElectricalProgressive.Content.ItemInsertionPipe;
using ElectricalProgressive.Content.LiquidInsertionPipe;
using ElectricalProgressive.Content.NormalPipe;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.NetworkPipe;

/// <summary>
/// Представляет сеть соединенных труб и инсертеров
/// </summary>
public class PipeNetwork
{
    public long NetworkId { get; private set; }
    public HashSet<BlockPos> Pipes { get; private set; }
    public HashSet<BlockPos> Inserters { get; private set; }
    public List<PipeEndpoint> ItemSources { get; private set; }
    public List<PipeEndpoint> LiquidSources { get; private set; }
    public List<PipeEndpoint> LiquidSinks { get; private set; }

    public PipeNetwork(long id)
    {
        NetworkId = id;
        Pipes = [];
        Inserters = [];
        ItemSources = [];
        LiquidSources = [];
        LiquidSinks = [];
    }

    /// <summary>
    /// Добавляет трубу в сеть, если её ещё нет
    /// </summary>
    public void AddPipe(BlockPos pos, BlockEntity pipe)
    {
        if (Pipes.Add(pos.Copy()))
        {
            if (pipe is BEItemInsertionPipe || pipe is BELiquidInsertionPipe)
            {
                Inserters.Add(pos.Copy());
            }
        }
    }

    /// <summary>
    /// Удаляет трубу из списка труб и инсертеров
    /// </summary>
    public void RemovePipe(BlockPos pos)
    {
        Pipes.Remove(pos);
        Inserters.Remove(pos);
        RemoveEndpointsForPipe(pos);
    }

    /// <summary>
    /// Объединяет текущую сеть с другой (без дублирования позиций)
    /// </summary>
    public void Merge(PipeNetwork otherNetwork)
    {
        foreach (var pipePos in otherNetwork.Pipes)
        {
            Pipes.Add(pipePos.Copy());
        }

        foreach (var inserterPos in otherNetwork.Inserters)
        {
            Inserters.Add(inserterPos.Copy());
        }
    }

    public void RebuildEndpointCache(ICoreAPI api)
    {
        ItemSources.Clear();
        LiquidSources.Clear();
        LiquidSinks.Clear();

        if (api == null)
            return;

        foreach (var pipePos in Pipes)
        {
            AddEndpointsForPipe(api, pipePos);
        }
    }

    public void RefreshEndpointsForPipe(ICoreAPI api, BlockPos pipePos)
    {
        RemoveEndpointsForPipe(pipePos);

        if (Pipes.Contains(pipePos))
            AddEndpointsForPipe(api, pipePos);
    }

    private void RemoveEndpointsForPipe(BlockPos pipePos)
    {
        ItemSources.RemoveAll(endpoint => endpoint.PipePos.Equals(pipePos));
        LiquidSources.RemoveAll(endpoint => endpoint.PipePos.Equals(pipePos));
        LiquidSinks.RemoveAll(endpoint => endpoint.PipePos.Equals(pipePos));
    }

    private void AddEndpointsForPipe(ICoreAPI api, BlockPos pipePos)
    {
        for (int i = 0; i < 6; i++)
        {
            BlockFacing facing = BlockFacing.ALLFACES[i];
            BlockPos endpointPos = pipePos.AddCopy(facing);
            var block = api.World.BlockAccessor.GetBlock(endpointPos);

            if (block is BlockPipeBase)
                continue;

            if (IsItemContainer(api, block, endpointPos))
                ItemSources.Add(new PipeEndpoint(pipePos, endpointPos, facing));

            if (IsLiquidSource(api, block, endpointPos))
                LiquidSources.Add(new PipeEndpoint(pipePos, endpointPos, facing));

            if (IsLiquidSink(api, block, endpointPos))
                LiquidSinks.Add(new PipeEndpoint(pipePos, endpointPos, facing));
        }
    }

    private static bool IsItemContainer(ICoreAPI api, Vintagestory.API.Common.Block block, BlockPos pos)
    {
        var blockEntity = api.World.BlockAccessor.GetBlockEntity(pos);
        if (blockEntity is BlockEntityContainer container)
            return container.Inventory != null;

        if (blockEntity is IBlockEntityContainer blockEntityContainer)
            return blockEntityContainer.Inventory != null;

        return false;
    }

    private static bool IsLiquidSource(ICoreAPI api, Vintagestory.API.Common.Block block, BlockPos pos)
    {
        if (block is ILiquidSource)
            return true;

        if (block is BlockMultiblock multiblock)
        {
            BlockPos controlPos = multiblock.GetControlBlockPos(pos);
            return api.World.BlockAccessor.GetBlock(controlPos) is ILiquidSource;
        }

        return false;
    }

    private static bool IsLiquidSink(ICoreAPI api, Vintagestory.API.Common.Block block, BlockPos pos)
    {
        if (block is ILiquidSink)
            return true;

        if (block is BlockMultiblock multiblock)
        {
            BlockPos controlPos = multiblock.GetControlBlockPos(pos);
            return api.World.BlockAccessor.GetBlock(controlPos) is ILiquidSink;
        }

        return false;
    }

    /// <summary>
    /// Находит все соединенные трубы через BFS
    /// </summary>
    public static List<BlockPos> FindConnectedPipes(IWorldAccessor world, BlockPos startPos, BlockPos skipPos = null)
    {
        List<BlockPos> connected = [];
        Queue<BlockPos> toCheck = new Queue<BlockPos>();
        HashSet<BlockPos> visited = [];

        toCheck.Enqueue(startPos.Copy());
        visited.Add(startPos.Copy());

        while (toCheck.Count > 0)
        {
            BlockPos current = toCheck.Dequeue();

            if (skipPos != null && current.Equals(skipPos))
                continue;

            connected.Add(current.Copy());

            BEPipe pipe = world.BlockAccessor.GetBlockEntity(current) as BEPipe;
            if (pipe == null) continue;

            for (int i = 0; i < 6; i++)
            {
                if (pipe.ConnectedSides[i])
                {
                    BlockFacing facing = BlockFacing.ALLFACES[i];
                    BlockPos neighborPos = current.AddCopy(facing);

                    if (!visited.Contains(neighborPos))
                    {
                        Vintagestory.API.Common.Block neighborBlock = world.BlockAccessor.GetBlock(neighborPos);
                        if (neighborBlock is BlockPipeBase)
                        {
                            visited.Add(neighborPos.Copy());
                            toCheck.Enqueue(neighborPos.Copy());
                        }
                    }
                }
            }
        }

        return connected;
    }
}

public readonly struct PipeEndpoint
{
    public readonly BlockPos PipePos;
    public readonly BlockPos EndpointPos;
    public readonly BlockFacing FacingFromPipe;

    public PipeEndpoint(BlockPos pipePos, BlockPos endpointPos, BlockFacing facingFromPipe)
    {
        PipePos = pipePos.Copy();
        EndpointPos = endpointPos.Copy();
        FacingFromPipe = facingFromPipe;
    }
}
