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
        dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-part-split"));
        StorageAccess.DescribeCell(inSlot.Itemstack, world, dsc);
    }

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        => EmptyHousingSplit.OnHeld(slot, byEntity, firstEvent, ref handling, true);

    public override bool MatchesForCrafting(ItemStack inputStack, IRecipeBase recipe, IRecipeIngredient ingredient)
    {
        if (!EmptyHousingSplit.CanCraft(inputStack, ingredient, true))
            return false;
        return base.MatchesForCrafting(inputStack, recipe, ingredient);
    }
}
