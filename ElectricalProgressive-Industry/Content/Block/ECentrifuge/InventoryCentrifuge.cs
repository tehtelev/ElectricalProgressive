using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ECentrifuge;

public class InventoryCentrifuge : InventoryBase, ISlotProvider
{
    private ItemSlot[] slots;

    public ItemSlot[] Slots => this.slots;

    public InventoryCentrifuge(string inventoryID, ICoreAPI api)
        : base(inventoryID, api)
    {
        this.slots = this.GenEmptySlots(2);
    }

    public InventoryCentrifuge(string className, string instanceID, ICoreAPI api)
        : base(className, instanceID, api)
    {
        this.slots = this.GenEmptySlots(2);
    }

    public override int Count => 2;

    public override ItemSlot this[int slotId]
    {
        get => slotId < 0 || slotId >= this.Count ? (ItemSlot)null : this.slots[slotId];
        set
        {
            if (slotId < 0 || slotId >= this.Count)
                throw new ArgumentOutOfRangeException(nameof(slotId));
            this.slots[slotId] = value != null ? value : throw new ArgumentNullException(nameof(value));
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        this.slots = this.SlotsFromTreeAttributes(tree, this.slots);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        this.SlotsToTreeAttributes(this.slots, tree);
    }

    protected override ItemSlot NewSlot(int i)
    {
        return (ItemSlot)new ItemSlotSurvival((InventoryBase)this);
    }

    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        return targetSlot == this.slots[0] && sourceSlot.Itemstack.Collectible.GrindingProps != null
            ? 4f
            : base.GetSuitability(sourceSlot, targetSlot, isMerge);
    }

    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        return this.slots[0];
    }

    public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
    {
        return this.slots[1];
    }
}

