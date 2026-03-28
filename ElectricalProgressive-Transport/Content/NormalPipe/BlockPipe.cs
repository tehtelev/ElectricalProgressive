using System.Text;
using Vintagestory.API.Common;

using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.NormalPipe;

public class BlockPipe : BlockPipeBase
{

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {

        var be = world.BlockAccessor.GetBlockEntity(pos) as BEPipe;
        if (be == null)
            return null;

        var blockCode = "electricalprogressivetransport:" + be.GetBaseBlockCode() + "-cross";

        var block = world.BlockAccessor.GetBlock(blockCode);

        return new(block);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return [OnPickBlock(world, pos)];
    }

    // Простая транспортная труба
    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        StringBuilder sb = new StringBuilder();

       var pipe = world.BlockAccessor.GetBlockEntity(pos) as BEPipe;
        if (pipe != null)
        {
            pipe.GetBlockInfo(forPlayer, sb);
        }

        return sb.ToString();
    }
}