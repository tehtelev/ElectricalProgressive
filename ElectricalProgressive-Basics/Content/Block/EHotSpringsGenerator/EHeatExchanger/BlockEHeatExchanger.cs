using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EHotSpringsGenerator.EHeatExchanger;

public class BlockEHeatExchanger : Vintagestory.API.Common.Block
{
    /// <summary>
    /// Checks if the block can be placed - must be adjacent to a hot springs generator
    /// </summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack,
       BlockSelection blockSel, ref string failureCode)
    {
        // Check if any adjacent block (including diagonals) is a hot springs generator
        if (!IsAdjacentToHotSpringsGenerator(world, blockSel.Position))
        {
            return false;
        }

        return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }

    

    /// <summary>
    /// Checks if the position is horizontally adjacent to a hot springs generator (including diagonals)
    /// </summary>
    private static bool IsAdjacentToHotSpringsGenerator(IWorldAccessor world, BlockPos pos)
    {
        var accessor = world.BlockAccessor;

        // Check all 8 horizontal directions (N, S, E, W, NE, NW, SE, SW)
        BlockPos[] adjacentPositions =
        [
            pos.NorthCopy(),
            pos.SouthCopy(),
            pos.EastCopy(),
            pos.WestCopy(),
            pos.NorthCopy().EastCopy(),  // NE
            pos.NorthCopy().WestCopy(),  // NW
            pos.SouthCopy().EastCopy(),  // SE
            pos.SouthCopy().WestCopy()   // SW
        ];

        foreach (var adjPos in adjacentPositions)
        {
            var blockEntity = accessor.GetBlockEntity(adjPos);
            if (blockEntity is BlockEntityEHotSpringsGenerator)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// When a neighbor block changes, check if we should break
    /// </summary>
    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);

        // If no longer adjacent to a hot springs generator, break
        if (!IsAdjacentToHotSpringsGenerator(world, pos))
        {
            world.BlockAccessor.BreakBlock(pos, null);
        }
    }

    /// <summary>
    /// Get held item info for inventory
    /// </summary>
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:heatexchanger-placement-hint"));
    }
}
