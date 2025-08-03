using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EStove;
public class InventoryEStove : InventoryBase, ISlotProvider
{

    private ItemSlot[] slots;
    private ItemSlot[] cookingSlots;
    public BlockPos? pos;
    private int defaultStorageType = 189;

    public ItemSlot[] CookingSlots
    {
        get => !this.HaveCookingContainer ? new ItemSlot[0] : this.cookingSlots;
    }

    public ItemSlot[] Slots => this.cookingSlots;

    public override Size3f MaxContentDimensions
    {
        get => this.slots[1].Itemstack?.ItemAttributes?["maxContentDimensions"].AsObject<Size3f>()!;
        set
        {
        }
    }

    public bool HaveCookingContainer
    {
        get
        {
            ItemStack itemstack = this.slots[1].Itemstack;
            return itemstack != null && (itemstack.ItemAttributes?.KeyExists("cookingContainerSlots") ?? false);
        }
    }

    public float CookingSlotCapacityLitres
    {
        get
        {
            return (this.slots?[1]?.Itemstack?.ItemAttributes?["cookingSlotCapacityLitres"].AsFloat(6f) ?? 6f);
        }
    }

    public int CookingContainerMaxSlotStackSize
    {
        get
        {
            return !this.HaveCookingContainer ? 0 : this.slots[1].Itemstack.ItemAttributes["maxContainerSlotStackSize"].AsInt(999);
        }
    }

    public override bool CanContain(ItemSlot sinkSlot, ItemSlot sourceSlot)
    {
        return this.GetSlotId(sinkSlot) < 3 || base.CanContain(sinkSlot, sourceSlot);
    }

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

    public override void LateInitialize(string inventoryID, ICoreAPI api)
    {
        base.LateInitialize(inventoryID, api);
        for (int index = 0; index < this.cookingSlots.Length; ++index)
            this.cookingSlots[index].MaxSlotStackSize = this.CookingContainerMaxSlotStackSize;
        this.updateStorageTypeFromContainer(this.slots[1].Itemstack);
    }

    public override int Count => this.slots.Length;

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

    public override void DidModifyItemSlot(ItemSlot slot, ItemStack extractedStack = null!)
    {
        base.DidModifyItemSlot(slot, extractedStack);
        if (this.slots[1] != slot)
            return;
        if ((slot != null ? ((!slot.Itemstack?.ItemAttributes?["storageType"].Exists ?? false) ? 1 : 0) : 1) != 0)
            this.discardCookingSlots();
        else
            this.updateStorageTypeFromContainer(slot!.Itemstack!);
    }

    private void updateStorageTypeFromContainer(ItemStack stack)
    {
        int num = this.defaultStorageType;
        if (stack?.ItemAttributes?["storageType"] != null)
            num = stack.ItemAttributes["storageType"].AsInt(this.defaultStorageType);
        for (int index = 0; index < this.cookingSlots.Length; ++index)
        {
            this.cookingSlots[index].StorageType = (EnumItemStorageFlags)num;
            this.cookingSlots[index].MaxSlotStackSize = this.CookingContainerMaxSlotStackSize;
            (this.cookingSlots[index] as ItemSlotWatertight)!.capacityLitres = this.CookingSlotCapacityLitres;
        }
    }

    public override float GetTransitionSpeedMul(EnumTransitionType transType, ItemStack stack)
    {
        return base.GetTransitionSpeedMul(transType, stack);
    }

    public void discardCookingSlots()
    {
        Vec3d position = this.pos!.ToVec3d().Add(0.5, 0.5, 0.5);
        for (int index = 0; index < this.cookingSlots.Length; ++index)
        {
            if (this.cookingSlots[index] != null)
            {
                this.Api.World.SpawnItemEntity(this.cookingSlots[index].Itemstack, position);
                this.cookingSlots[index].Itemstack = null!;
            }
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        List<ItemSlot> modifiedSlots = new List<ItemSlot>();
        this.slots = this.SlotsFromTreeAttributes(tree, this.slots, modifiedSlots);
        for (int index = 0; index < modifiedSlots.Count; ++index)
            this.DidModifyItemSlot(modifiedSlots[index], (ItemStack)null!);
        if (this.Api == null)
            return;
        for (int index = 0; index < this.cookingSlots.Length; ++index)
            this.cookingSlots[index].MaxSlotStackSize = this.CookingContainerMaxSlotStackSize;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        this.SlotsToTreeAttributes(this.slots, tree);
    }

    public override void OnItemSlotModified(ItemSlot slot) => base.OnItemSlotModified(slot);

    protected override ItemSlot NewSlot(int i)
    {
        switch (i)
        {
            case 0:
                return (ItemSlot)new ItemSlotSurvival((InventoryBase)this);
            case 1:
                return (ItemSlot)new ItemSlotInput((InventoryBase)this, 2);
            case 2:
                return (ItemSlot)new ItemSlotOutput((InventoryBase)this);
            default:
                return (ItemSlot)new ItemSlotWatertight((InventoryBase)this, this.CookingSlotCapacityLitres);
        }
    }

    public override WeightedSlot GetBestSuitedSlot(
      ItemSlot sourceSlot,
      ItemStackMoveOperation op,
      List<ItemSlot> skipSlots = null!)
    {
        if (!this.HaveCookingContainer)
        {
            if (skipSlots == null)
                skipSlots = new List<ItemSlot>();
            skipSlots.Add(this.slots[2]);
            skipSlots.Add(this.slots[3]);
            skipSlots.Add(this.slots[4]);
            skipSlots.Add(this.slots[5]);
            skipSlots.Add(this.slots[6]);
        }
        return base.GetBestSuitedSlot(sourceSlot, op, skipSlots);
    }

    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        ItemStack itemstack = sourceSlot.Itemstack;
        if (targetSlot == this.slots[1] && (itemstack.Collectible is BlockSmeltingContainer || itemstack.Collectible is BlockCookingContainer))
            return 2.2f;
        if (targetSlot == this.slots[0] && (itemstack.Collectible.CombustibleProps == null || itemstack.Collectible.CombustibleProps.BurnTemperature <= 0))
            return 0.0f;
        return targetSlot == this.slots[1] && (itemstack.Collectible.CombustibleProps == null || itemstack.Collectible.CombustibleProps.SmeltedStack == null) ? 0.5f : base.GetSuitability(sourceSlot, targetSlot, isMerge);
    }

    public string GetOutputText()
    {
        ItemStack itemstack = this.slots[1].Itemstack;
        if (itemstack == null)
            return null!;

        if (itemstack.Collectible is BlockSmeltingContainer)
            return ((BlockSmeltingContainer)itemstack.Collectible).GetOutputText(this.Api.World, (ISlotProvider)this, this.slots[1]);
        if (itemstack.Collectible is BlockCookingContainer)
            return ((BlockCookingContainer)itemstack.Collectible).GetOutputText(this.Api.World, (ISlotProvider)this, this.slots[1])!;
        ItemStack resolvedItemstack = itemstack.Collectible.CombustibleProps?.SmeltedStack?.ResolvedItemstack!;
        if (resolvedItemstack == null)
            return (string)null!;
        if (itemstack?.Collectible?.CombustibleProps?.SmeltingType == EnumSmeltType.Fire)
            return Lang.Get("Can't smelt, requires a kiln");
        if (itemstack?.Collectible?.CombustibleProps?.RequiresContainer ?? false)
            return Lang.Get("Can't smelt, requires smelting container (i.e. Crucible)");
        return Lang.Get("firepit-gui-willcreate", (object)(itemstack!.StackSize / itemstack!.Collectible!.CombustibleProps!.SmeltedRatio), (object)resolvedItemstack.GetName());
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
