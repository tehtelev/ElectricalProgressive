using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EOven;
  public class InventoryEOven : InventoryBase, ISlotProvider
  {
    private ItemSlot[] slots;
    private readonly int cookingSize;


    public InventoryEOven(string inventoryID, int bakeableSlots)
      : base(inventoryID, (ICoreAPI) null)
    {
      this.slots = this.GenEmptySlots(bakeableSlots + 1);
      this.cookingSize = bakeableSlots;
      this.CookingSlots = new ItemSlot[bakeableSlots];


      for (int index = 0; index < bakeableSlots; ++index)
      {
          this.slots[index].MaxSlotStackSize = 1;
          this.CookingSlots[index] = this.slots[index];
      }
    }

    public ItemSlot[] CookingSlots { get; }

    public ItemSlot[] Slots => this.slots;

    public override int Count => this.slots.Length;

    public override ItemSlot this[int slotId]
    {
      get => slotId < 0 || slotId >= this.Count ? null! : this.slots[slotId];
      set
      {
        if (slotId < 0 || slotId >= this.Count)
          throw new ArgumentOutOfRangeException(nameof (slotId));
        ItemSlot[] slots = this.slots;
        int index = slotId;
        slots[index] = value ?? throw new ArgumentNullException(nameof (value));
      }
    }

    /// <summary>
    /// Если слот изменился, то обновляем данные духовки
    /// </summary>
    /// <param name="slot"></param>
    public override void OnItemSlotModified(ItemSlot slot)
    {
        int num = Array.IndexOf(slots, slot);
        if (num >= 0 && slot != null && slot.Itemstack!=null) 
        {
            if (Api?.World.BlockAccessor.GetBlockEntity(Pos) is BlockEntityEOven entity && entity != null)
            {
                entity.bakingData[num]= new OvenItemData(slot.Itemstack);
            }
        }
    }


    public override void FromTreeAttributes(ITreeAttribute tree)
    {
      List<ItemSlot> modifiedSlots = new List<ItemSlot>();
      this.slots = this.SlotsFromTreeAttributes(tree, this.slots, modifiedSlots);
      for (int index = 0; index < modifiedSlots.Count; ++index)
        this.MarkSlotDirty(this.GetSlotId(modifiedSlots[index]));
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
      this.SlotsToTreeAttributes(this.slots, tree);
    }

    protected override ItemSlot NewSlot(int i)
    {
      return (ItemSlot) new ItemSlotSurvival((InventoryBase) this);
    }


   

    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
      CombustibleProperties combustibleProps = sourceSlot.Itemstack.Collectible.CombustibleProps;
      return targetSlot == this.slots[this.cookingSize] && (combustibleProps == null || combustibleProps.BurnTemperature <= 0) ? 0.0f : base.GetSuitability(sourceSlot, targetSlot, isMerge);
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
        for (int i = 0; i < this.cookingSize; i++)
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
        for (int i = 0; i < this.cookingSize; i++)
        {
            if (this[i] != null && !this[i].Empty)
            {
                BakingProperties bakingProperties = BakingProperties.ReadFrom(this[i].Itemstack);
                if (bakingProperties == null || !this[i].Itemstack.Attributes.GetBool("bakeable", true)) //если свойства выпекания не найдены
                    return this[i];
                
                string Code="";
                if (this[i].Itemstack.Item!=null)
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
