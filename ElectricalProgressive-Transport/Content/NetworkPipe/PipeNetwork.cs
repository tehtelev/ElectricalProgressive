using ElectricalProgressive.Content.ItemInsertionPipe;
using ElectricalProgressive.Content.LiquidInsertionPipe;
using ElectricalProgressive.Content.NormalPipe;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.NetworkPipe;

/// <summary>
/// Представляет сеть соединенных труб и инсертеров
/// </summary>
public class PipeNetwork
{
    public long NetworkId { get; private set; }
    public List<BlockPos> Pipes { get; private set; }
    public List<BlockPos> Inserters { get; private set; }

    public PipeNetwork(long id)
    {
        NetworkId = id;
        Pipes = [];
        Inserters = [];
    }

    /// <summary>
    /// Добавляет трубу в сеть, если её ещё нет
    /// </summary>
    public void AddPipe(BlockPos pos, BlockEntity pipe)
    {
        if (!Pipes.Contains(pos))
        {
            Pipes.Add(pos.Copy());

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
    }

    /// <summary>
    /// Объединяет текущую сеть с другой (без дублирования позиций)
    /// </summary>
    public void Merge(PipeNetwork otherNetwork)
    {
        foreach (var pipePos in otherNetwork.Pipes)
        {
            if (!Pipes.Contains(pipePos))
            {
                Pipes.Add(pipePos.Copy());
            }
        }

        foreach (var inserterPos in otherNetwork.Inserters)
        {
            if (!Inserters.Contains(inserterPos))
            {
                Inserters.Add(inserterPos.Copy());
            }
        }
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