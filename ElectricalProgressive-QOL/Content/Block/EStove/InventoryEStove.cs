using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EStove;
public class InventoryEStove : InventorySmelting
{


    public InventoryEStove(string inventoryID, ICoreAPI api)
      : base(inventoryID, api)
    {
      
    }

    public InventoryEStove(string className, string instanceID, ICoreAPI api)
      : base(className, instanceID, api)
    {
       
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
            for (var i = 0; i < CookingSlots.Length; i++)
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
            return this[1];
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
        return this[2];
    }

}

