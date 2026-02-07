using ElectricalProgressiveTransport.ItemInsertionPipe;
using ElectricalProgressiveTransport.NormalPipe;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressiveTransport.NetworkPipe;

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

    public void AddPipe(BlockPos pos, BlockEntity pipe)
    {
        if (!Pipes.Contains(pos))
        {
            Pipes.Add(pos.Copy());

            if (pipe is BEInsertionPipe)
            {
                Inserters.Add(pos.Copy());
            }
        }
    }

    public void RemovePipe(BlockPos pos)
    {
        Pipes.Remove(pos);
        Inserters.Remove(pos);
    }

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
                        Block neighborBlock = world.BlockAccessor.GetBlock(neighborPos);
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