using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EOven;
public class InventoryEOven : InventoryOven
{


    public InventoryEOven(string inventoryID, int bakeableSlots)
      : base(inventoryID, bakeableSlots)
    {


    }



    /// <summary>
    /// Если слот изменился, то обновляем данные духовки
    /// </summary>
    /// <param name="slot"></param>
    public override void OnItemSlotModified(ItemSlot slot)
    {
        int num = Array.IndexOf(Slots, slot);
        if (num >= 0 && slot != null && slot.Itemstack != null)
        {
            if (Api?.World.BlockAccessor.GetBlockEntity(Pos) is BlockEntityEOven entity && entity != null)
            {
                entity.bakingData[num] = new OvenItemData(slot.Itemstack);
            }
        }
    }





    /// <summary>
    /// Автозагрузка духовки
    /// </summary>
    /// <param name="atBlockFace"></param>
    /// <param name="fromSlot"></param>
    /// <returns></returns>
    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        // если поместить в слот для готовки нельзя, то выдаем null
        if (!BlockEntityEOven.IsValidInput(fromSlot, this))
            return null!;

        // если в слоты для готовки есть свободные, то выдаем первый из них
        for (int i = 0; i < this.CookingSlots.Length; i++)
        {
            if (this[i] == null || this[i].Empty)
            {
                if (i == 0) // если первый пустой, то выдаем так как есть
                {
                    return this[i];
                }
                else // если не первый, то проверяем, что духовка в режиме "квадраты"
                {
                    if (Api?.World.BlockAccessor.GetBlockEntity(Pos) is BlockEntityEOven entity && entity != null &&
                        entity.OvenContentMode == EnumOvenContentMode.Quadrants)
                        return this[i];
                }
            }
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
        for (int i = 0; i < this.CookingSlots.Length; i++)
        {
            if (this[i] != null && !this[i].Empty)
            {
                BakingProperties bakingProperties = BakingProperties.ReadFrom(this[i].Itemstack);
                if (bakingProperties == null || !this[i].Itemstack.Attributes.GetBool("bakeable", true)) //если свойства выпекания не найдены
                    return this[i];

                string Code = "";
                if (this[i].Itemstack.Item != null)
                {
                    Code = this[i].Itemstack.Item.Code.ToString();
                }
                else if (this[i].Itemstack.Block != null)
                {
                    Code = this[i].Itemstack.Block.Code.ToString();
                }

                if (Code.Contains("perfect") ||
                    Code.Contains("charred") ||
                    Code.Contains("rot") ||
                    Code.Contains("bake1") ||
                    Code.Contains("bake2") ||
                    Code.Contains("cooked") ||
                    Code.Contains("dry"))
                {
                    return this[i];
                }
            }
        }

        return null!;
    }

}
