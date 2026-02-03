using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressiveTransport.LiquidInsertionPipe;

public class BlockLiquidInsertionPipe : BlockPipeBase
{
    public override WorldInteraction[] GetPlacedBlockInteractionHelp(
        IWorldAccessor world,
        BlockSelection selection,
        IPlayer forPlayer)
    {
        base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        return new WorldInteraction[1]
        {
            new WorldInteraction()
            {
                ActionLangCode = Lang.Get("electricalprogressivetransport:blockhelp-liquid-filter-settings"),
                MouseButton = EnumMouseButton.Right,
                HotKeyCode = "sneak"
            }
        }.Append<WorldInteraction>(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }

    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(Lang.Get("electricalprogressivetransport:pipe-liquid-insertion-info"));
        BELiquidInsertionPipe pipe = world.BlockAccessor.GetBlockEntity(pos) as BELiquidInsertionPipe;
        if (pipe != null)
        {
            pipe.GetBlockInfo(forPlayer, sb);
        }

        return sb.ToString();
    }
}