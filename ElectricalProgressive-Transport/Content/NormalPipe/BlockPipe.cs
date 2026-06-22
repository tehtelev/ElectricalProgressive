using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.NormalPipe;

public class BlockPipe : BlockPipeBase
{
    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is not BEPipe)
            return null;

        var block = world.GetBlock(new AssetLocation("electricalprogressivetransport:pipe-normal"));
        return block == null ? null : new ItemStack(block);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return [OnPickBlock(world, pos)];
    }

    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        var sb = new StringBuilder();
        if (world.BlockAccessor.GetBlockEntity(pos) is BEPipe pipe)
            pipe.GetBlockInfo(forPlayer, sb);

        return sb.ToString();
    }
}