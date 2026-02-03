using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressiveTransport.ItemInsertionPipe;

public class BlockInsertionPipe : BlockPipeBase
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
                ActionLangCode = Lang.Get("electricalprogressivetransport:blockhelp-filter-settings"),
                MouseButton = EnumMouseButton.Right,
                HotKeyCode = "sneak" // Добавляем информацию о горячей клавише
            }
        }.Append<WorldInteraction>(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }

    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(Lang.Get("electricalprogressivetransport:pipe-insertion-info"));
        BEInsertionPipe pipe = world.BlockAccessor.GetBlockEntity(pos) as BEInsertionPipe;
        if (pipe != null)
        {
            pipe.GetBlockInfo(forPlayer, sb);
        }

        return sb.ToString();
    }
}