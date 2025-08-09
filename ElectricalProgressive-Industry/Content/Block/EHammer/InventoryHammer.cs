using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ECentrifuge;

public class InventoryHammer : InventoryBase, ISlotProvider
{
    private ItemSlot[] slots;

    public ItemSlot[] Slots => this.slots;

    public InventoryHammer(string inventoryID, ICoreAPI api)
        : base(inventoryID, api)
    {
        this.slots = this.GenEmptySlots(3); // Теперь 3 слота
    }

    public InventoryHammer(string className, string instanceID, ICoreAPI api)
        : base(className, instanceID, api)
    {
        this.slots = this.GenEmptySlots(3); // Теперь 3 слота
    }

    public override int Count => 3; // Теперь 3 слота

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
        // Слот 0 - только для входных предметов
        if (targetSlot == this.slots[0])
        {
            return sourceSlot.Itemstack.Collectible.GrindingProps != null ? 4f : 0f;
        }
        
        // Слоты 1 и 2 - только для выходных предметов (нельзя вручную класть)
        return 0f;
    }

    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        // Автозаполнение только в входной слот
        return this.slots[0];
    }

    public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
    {
        // Автовывод сначала из основного выхода (слот 1), затем из дополнительного (слот 2)
        for (var i = 1; i < this.slots.Length; i++)
        {
            var slot = this.slots[i];
            if (!slot.Empty)
                return slot;
        }

        return null;
    }

    // Методы для удобного доступа к слотам
    public ItemSlot InputSlot => this.slots[0];
    public ItemSlot OutputSlot => this.slots[1];
    public ItemSlot SecondaryOutputSlot => this.slots[2];
}