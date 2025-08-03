using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ElectricalProgressive.Content.Block.ETermoGenerator;

public class InventoryTermoGenerator : InventoryBase, ISlotProvider
{
    private ItemSlot[] slots;

    public ItemSlot[] Slots => this.slots;

    public InventoryTermoGenerator(string inventoryID, ICoreAPI api)
        : base(inventoryID, api)
    {
        this.slots = this.GenEmptySlots(4);
    }

    public InventoryTermoGenerator(string className, string instanceID, ICoreAPI api)
        : base(className, instanceID, api)
    {
        this.slots = this.GenEmptySlots(1);
    }

    public override int Count => 1;

    public override ItemSlot this[int slotId]
    {
        get => slotId < 0 || slotId >= this.Count ? null! : this.slots[slotId];
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
}

