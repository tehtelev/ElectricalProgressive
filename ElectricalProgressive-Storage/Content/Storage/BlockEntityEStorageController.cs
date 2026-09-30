using ElectricalProgressive.Content.Block;
using Vintagestory.API.Common;

namespace ElectricalProgressive.Content.Storage;

public class BlockEntityEStorageController : BlockEntityEBase
{
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        StorageAccess.DirtyTopology();
    }
}
