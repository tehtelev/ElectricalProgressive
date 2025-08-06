using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EWoodcutter;

public class InventoryEWoodcutter : InventoryGeneric, ISlotProvider
{
    public ItemSlot[] Slots => slots;

    public InventoryEWoodcutter(ICoreAPI api)
        : base(api)
    {
        // Слоты инициализирует InventoryGeneric в методе Init
        slots = Array.Empty<ItemSlot>();
    }

    public override void LateInitialize(string inventoryID, ICoreAPI api)
    {
        var elems = inventoryID.Split("-");

        Init(6, elems[0], elems[1]);

        base.LateInitialize(inventoryID, api);
    }

    public override bool CanContain(ItemSlot sinkSlot, ItemSlot sourceSlot)
    {
        var slotId = GetSlotId(sinkSlot);
        if (slotId == -1)
            return false;

        if (slotId == 0)
        {
            if (sourceSlot.Itemstack.Collectible is not ItemTreeSeed itemTreeSeed)
                return false;

            var type = itemTreeSeed.Variant["type"];
            return type switch
            {
                // Запрещаем секвои, идеале надо смотреть существует ли logsection для такого типа дерева
                //"redwood" => false,

                _ => true,
            };
        }

        var code = sourceSlot.Itemstack.Collectible.Code.Path;
        return code.StartsWith("log") || code.Contains("stick");
    }

    public override ItemSlot? GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot? fromSlot)
    {
        if (fromSlot is null || fromSlot.Empty)
            return null;

        if (fromSlot.Itemstack.Collectible is not ItemTreeSeed)
            return null;

        if (Slots[0].Empty)
            return Slots[0];

        var isCollectableEquals = Slots[0].Itemstack.Collectible.Equals(
            Slots[0].Itemstack,
            fromSlot.Itemstack,
            GlobalConstants.IgnoredStackAttributes);
        if (isCollectableEquals && Slots[0].GetRemainingSlotSpace(fromSlot.Itemstack) > 0)
            return Slots[0];

        return null;
    }

    public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
    {
        for (var i = 1; i < Slots.Length; i++)
        {
            var slot = Slots[i];
            if (!slot.Empty)
                return slot;
        }

        return null!;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        ResolveBlocksOrItems();
    }
}