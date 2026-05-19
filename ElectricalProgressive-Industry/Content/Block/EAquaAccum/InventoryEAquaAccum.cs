using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.EAquaAccum;

public class InventoryEAquaAccum : InventoryBase, ISlotProvider
{
    private ItemSlot[] slots;
    private BlockPos _pos;
    private ICoreAPI _api;

    public ItemSlot[] Slots => this.slots;
    public ItemSlot LiquidSlot => this.slots[0];

    public override int Count => 1;

    public override ItemSlot this[int slotId]
    {
        get
        {
            if (slotId < 0 || slotId >= 1)
                return null;
            return slots[slotId];
        }
        set
        {
            if (slotId < 0 || slotId >= 1)
                throw new ArgumentOutOfRangeException(nameof(slotId));
            slots[slotId] = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    public InventoryEAquaAccum(string inventoryID, ICoreAPI api)
        : base(inventoryID, api)
    {
        _api = api;
        slots = new ItemSlot[1];
        InitializeSlots();
    }

    public InventoryEAquaAccum() : base(null, null)
    {
        slots = new ItemSlot[1];
        InitializeSlots();
    }

    public override void LateInitialize(string inventoryID, ICoreAPI api)
    {
        base.LateInitialize(inventoryID, api);
        _api = api;
        InitializeSlots();
    }

    public void SetBlockPos(BlockPos pos)
    {
        _pos = pos;
        UpdateLiquidSlotCapacity();
    }

    public void UpdateLiquidSlotCapacity()
    {
        if (slots[0] is ItemSlotLiquidOnly liquidSlot)
        {
            float capacity = GetLiquidCapacityFromBlock();

            var field = typeof(ItemSlotLiquidOnly).GetField("CapacityLitres",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(liquidSlot, capacity);
            }
        }
    }

    private void InitializeSlots()
    {
        for (int i = 0; i < 1; i++)
        {
            if (slots[i] == null)
            {
                slots[i] = NewSlot(i);
            }
        }
    }

    private float GetLiquidCapacityFromBlock()
    {
        if (_api == null || _pos == null) return 100f;

        var block = _api.World.BlockAccessor.GetBlock(_pos);

        if (block?.Attributes?["capacityLitres"].Exists == true)
        {
            return block.Attributes["capacityLitres"].AsFloat(100f);
        }

        var property = block?.GetType().GetProperty("CapacityLitres");
        if (property != null)
        {
            object value = property.GetValue(block);
            if (value is float floatValue)
                return floatValue;
            if (value is int intValue)
                return intValue;
        }

        return 100f;
    }

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        var loadedSlots = this.SlotsFromTreeAttributes(tree, this.slots);

        for (int i = 0; i < 1; i++)
        {
            if (i < loadedSlots.Length)
                slots[i] = loadedSlots[i];
        }

        UpdateLiquidSlotCapacity();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        this.SlotsToTreeAttributes(this.slots, tree);
    }

    protected override ItemSlot NewSlot(int i)
    {
        switch (i)
        {
            case 0: // Слот для воды
                return new ItemSlotLiquidOnly(this, 100f);
            default:
                return new ItemSlotSurvival(this);
        }
    }

    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        if (targetSlot == null || sourceSlot?.Itemstack == null)
            return 0f;

        // Проверка для слота жидкости
        if (targetSlot == LiquidSlot)
        {
            var props = BlockLiquidContainerBase.GetContainableProps(sourceSlot.Itemstack);
            if (props != null && props.Containable)
            {
                // Проверяем, является ли жидкость водой
                if (sourceSlot.Itemstack.ItemAttributes?["waterTightContainerProps"] != null)
                {
                    // Можно принимать любую жидкость, но вода предпочтительнее
                    if (sourceSlot.Itemstack.Collectible.Code.Path.Contains("water"))
                        return 4f;
                    return 2f;
                }
            }
            return 0f;
        }

        return base.GetSuitability(sourceSlot, targetSlot, isMerge);
    }

    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        if (fromSlot?.Itemstack == null)
            return null;

        // Проверяем, является ли предмет жидкостью
        var props = BlockLiquidContainerBase.GetContainableProps(fromSlot.Itemstack);
        if (props != null && props.Containable)
            return LiquidSlot;

        return null;
    }
}