using ElectricalProgressive.Content.Block;
using Vintagestory.API.Common;

namespace ElectricalProgressive.Content.Storage.Controller;

/// <summary>
/// Сущность контроллера. Слом блока заново обходит сеть.
/// </summary>
public class BlockEntityEStorageController : BlockEntityEBase
{
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        StorageAccess.DirtyTopology();
    }
}
