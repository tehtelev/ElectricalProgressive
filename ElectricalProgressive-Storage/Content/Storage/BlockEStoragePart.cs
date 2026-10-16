using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage;

/// <summary>
/// База блоков сети. Установка и слом заново обходят сеть.
/// </summary>
public class BlockEStoragePart : Vintagestory.API.Common.Block
{
    public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(world, blockPos, byItemStack);
        StorageAccess.DirtyTopology();
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        base.OnBlockRemoved(world, pos);
        StorageAccess.DirtyTopology();
    }
}


