using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EStove;
public class InventoryEStove : InventorySmelting
{

    private ItemSlot[] slots;
    private ItemSlot[] cookingSlots;
    
    private int defaultStorageType = 189;



    public InventoryEStove(string inventoryID, ICoreAPI api)
      : base(inventoryID, api)
    {
        this.slots = this.GenEmptySlots(7);
        this.cookingSlots = new ItemSlot[4]
        {
        this.slots[3],
        this.slots[4],
        this.slots[5],
        this.slots[6]
        };
        this.baseWeight = 4f;
    }

    public InventoryEStove(string className, string instanceID, ICoreAPI api)
      : base(className, instanceID, api)
    {
        this.slots = this.GenEmptySlots(7);
        this.cookingSlots = new ItemSlot[4]
        {
        this.slots[3],
        this.slots[4],
        this.slots[5],
        this.slots[6]
        };
        this.baseWeight = 4f;
    }


    /// <summary>
    /// Автозагрузка духовки
    /// </summary>
    /// <param name="atBlockFace"></param>
    /// <param name="fromSlot"></param>
    /// <returns></returns>
    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        if (HaveCookingContainer)
        {
            // если в слоты для готовки есть свободные, то выдаем первый из них
            for (int i = 0; i < CookingSlots.Length; i++)
            {
                if (CookingSlots[i]==null ||
                    CookingSlots[i].Empty) // слот свободен?
                {
                    return CookingSlots[i];
                }

                if (CookingSlots[i].Itemstack != null &&  // слот не пустой
                    CookingSlots[i].Itemstack.StackSize < CookingContainerMaxSlotStackSize && // в нем меньше максимального количества предметов
                    fromSlot.Itemstack!=null && // слот входящий не пустой
                    CookingSlots[i].Itemstack.Collectible.Code== fromSlot.Itemstack.Collectible.Code // предметы одинаковые
                    )
                {
                    return CookingSlots[i];
                }
            }
        }
        else
        {
            return slots[1];
        }
        

        return null!;
    }



    /// <summary>
    /// Автопулл из духовки
    /// </summary>
    /// <param name="atBlockFace"></param>
    /// <returns></returns>
    public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
    {
        return slots[2];
    }

}
