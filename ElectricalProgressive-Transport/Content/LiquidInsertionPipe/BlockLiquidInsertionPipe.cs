using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.LiquidInsertionPipe;

public class BlockLiquidInsertionPipe : BlockPipeBase
{
    public override WorldInteraction[] GetPlacedBlockInteractionHelp(
        IWorldAccessor world,
        BlockSelection selection,
        IPlayer forPlayer)
    {
        base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);

        return new WorldInteraction[]
        {
            new WorldInteraction()
            {
                ActionLangCode = Lang.Get("electricalprogressivetransport:blockhelp-liquid-filter-settings"),
                MouseButton = EnumMouseButton.Right,
            },
        }.Append<WorldInteraction>(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is not BELiquidInsertionPipe)
            return null;

        var block = world.GetBlock(new AssetLocation("electricalprogressivetransport:pipe-liquid-insertion"));
        return block == null ? null : new ItemStack(block);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return [OnPickBlock(world, pos)];
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel.Position == null ||
            world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BELiquidInsertionPipe)
        {
            return false;
        }

        base.OnBlockInteractStart(world, byPlayer, blockSel);
        return true;
    }

    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        var sb = new StringBuilder();
        if (world.BlockAccessor.GetBlockEntity(pos) is BELiquidInsertionPipe pipe)
            pipe.GetBlockInfo(forPlayer, sb);

        return sb.ToString();
    }
}