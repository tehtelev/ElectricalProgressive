using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ElectricalProgressive.Content.Storage.Drive;

/// <summary>
/// Ячейка жидкости. До четырёх типов на общий объём.
/// </summary>
public class ItemEStorageCell : Vintagestory.API.Common.Item
{
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (inSlot.Itemstack == null)
            return;

        dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-cell-hint"));
        StorageAccess.DescribeCell(inSlot.Itemstack, world, dsc);
    }
}
