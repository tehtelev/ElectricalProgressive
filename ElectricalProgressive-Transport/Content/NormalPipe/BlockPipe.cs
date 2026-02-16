using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.NormalPipe;

public class BlockPipe : BlockPipeBase
{
    // Простая транспортная труба
    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(Lang.Get("electricalprogressivetransport:pipe-normal-info"));
        BEPipe pipe = world.BlockAccessor.GetBlockEntity(pos) as BEPipe;
        if (pipe != null)
        {
            pipe.GetBlockInfo(forPlayer, sb);
        }

        return sb.ToString();
    }
}