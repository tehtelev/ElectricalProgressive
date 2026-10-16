using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ElectricalProgressive.Content.Storage;

public class ItemEStorageDisk : Vintagestory.API.Common.Item
{
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (inSlot.Itemstack == null)
            return;

        dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-disk-hint"));
        StorageAccess.DescribeDisk(inSlot.Itemstack, world, dsc);
    }
}
