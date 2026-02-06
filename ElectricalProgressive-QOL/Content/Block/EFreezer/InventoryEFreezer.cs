using Vintagestory.API.Common;
using System;
using System.Collections.Generic;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.Block.EFreezer;

public class InventoryEFreezer : InventoryGeneric
{
    public InventoryEFreezer(int quantitySlots, string inventoryID, ICoreAPI api)
        : base(quantitySlots, inventoryID, api)
    {
        // добавляем флаг слота для рюкзака, чтобы игрок мог перемещать предметы между рюкзаком и морозильником без ограничений
        this.Foreach(slot => slot.StorageType = slot.StorageType | EnumItemStorageFlags.Backpack);
    }

    public override bool CanContain(ItemSlot sinkSlot, ItemSlot sourceSlot)
    {
        if (sourceSlot.Empty) return false;

        // Helper function to identify a carcass 
        bool IsCarcass(ItemStack stack)
        {
            return stack?.Collectible?.Code?.Domain?.Contains("butchering") == true &&
                   stack.Collectible.Code.Path.Contains("dead");
        }

        bool sourceIsCarcass = IsCarcass(sourceSlot.Itemstack);

        // Scan the current inventory state
        bool hasCarcass = false;
        bool hasOther = false;
        foreach (ItemSlot sl in this)
        {
            if (!sl.Empty)
            {
                if (IsCarcass(sl.Itemstack))
                {
                    hasCarcass = true;
                }
                else
                {
                    hasOther = true;
                }
            }
        }

        if (sourceIsCarcass)
        {
            // Check if merging into an existing carcass slot
            if (IsCarcass(sinkSlot.Itemstack))
            {
                // Assuming carcasses do not stack (common for unique dead animal items), base logic will handle max stack.
                // If they stack and you want to strictly limit to 1 total, add: if (sinkSlot.StackSize >= 1) return false;
                return base.CanContain(sinkSlot, sourceSlot);
            }
            else
            {
                // Adding a new carcass: only allow if inventory is completely empty (no other items or carcasses)
                if (hasCarcass || hasOther) return false;
            }
        }
        else
        {
            // Adding a non-carcass: disallow if any carcass is present
            if (hasCarcass) return false;
        }

        // Fall back to base checks (e.g., dimensions if set)
        return base.CanContain(sinkSlot, sourceSlot);
    }
}