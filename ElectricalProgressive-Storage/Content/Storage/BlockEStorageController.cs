using ElectricalProgressive.Content.Block;
using ElectricalProgressive.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage;

public class BlockEStorageController : BlockEBase
{
    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
    {
        if (!base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack))
            return false;

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityEStorageController entity)
            LoadEProperties.Load(this, entity);

        StorageAccess.DirtyTopology();
        return true;
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        base.OnBlockRemoved(world, pos);
        StorageAccess.DirtyTopology();
    }
}
