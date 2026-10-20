using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ElectricalProgressive.Content.Storage.Drive;

/// <summary>
/// Диск предметов. До 63 типов.
/// </summary>
public class ItemEStorageDisk : Vintagestory.API.Common.Item
{
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (inSlot.Itemstack == null)
            return;

        dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-disk-hint"));
        dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-part-split"));
        StorageAccess.DescribeDisk(inSlot.Itemstack, world, dsc);
    }

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        => EmptyHousingSplit.OnHeld(slot, byEntity, firstEvent, ref handling, false);

    public override bool MatchesForCrafting(ItemStack inputStack, IRecipeBase recipe, IRecipeIngredient ingredient)
    {
        if (!EmptyHousingSplit.CanCraft(inputStack, ingredient, false))
            return false;
        return base.MatchesForCrafting(inputStack, recipe, ingredient);
    }
}
