using Vintagestory.API.Common;

namespace ElectricalProgressive.Content.Block.EBlastFurnace;

internal static class BlastFurnaceSlotUtil
{
    public static bool IsIngotMold(ItemStack? stack)
    {
        return stack?.Collectible?.Code?.Path?.Contains("ingotmold") == true;
    }

    public static bool IsCharge(ItemStack? stack)
    {
        if (stack?.Collectible == null || IsIngotMold(stack))
            return false;
        var path = stack.Collectible.Code.Path;
        if (path.Contains("nugget") || path.Contains("metalbit") || path.Contains("ingot") || path.Contains("coal") || path.Contains("powder"))
            return true;
        return stack.Collectible is Vintagestory.API.Common.Block block &&
               (block.BlockMaterial == EnumBlockMaterial.Ore || block.BlockMaterial == EnumBlockMaterial.Metal);
    }
}

public class ItemSlotBlastFurnaceCharge : ItemSlot
{
    public ItemSlotBlastFurnaceCharge(InventoryBase inventory) : base(inventory)
    {
        BackgroundIcon = BlastFurnaceSlotIcons.Ore;
    }

    public override bool CanHold(ItemSlot sourceSlot) => BlastFurnaceSlotUtil.IsCharge(sourceSlot?.Itemstack);

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        => BlastFurnaceSlotUtil.IsCharge(sourceSlot?.Itemstack) && base.CanTakeFrom(sourceSlot, priority);
}

public class ItemSlotBlastFurnaceChance : ItemSlot
{
    public ItemSlotBlastFurnaceChance(InventoryBase inventory) : base(inventory)
    {
        BackgroundIcon = BlastFurnaceSlotIcons.Chance;
    }
}

public class ItemSlotBlastFurnaceMold : ItemSlot
{
    public ItemSlotBlastFurnaceMold(InventoryBase inventory) : base(inventory)
    {
        BackgroundIcon = BlastFurnaceSlotIcons.Mold;
    }

    public override bool CanHold(ItemSlot sourceSlot) => BlastFurnaceSlotUtil.IsIngotMold(sourceSlot?.Itemstack);

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        => BlastFurnaceSlotUtil.IsIngotMold(sourceSlot?.Itemstack) && base.CanTakeFrom(sourceSlot, priority);
}
