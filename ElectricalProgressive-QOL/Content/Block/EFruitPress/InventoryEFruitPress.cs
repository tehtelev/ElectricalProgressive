using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EFruitPress;

/// <summary>
/// Простой инвентарь пресса для сока
/// </summary>
public class InventoryEFruitPress : InventoryBase, ISlotProvider
{
    private ItemSlot[] slots;
    
    public ItemSlot[] Slots => slots;
    public ItemSlot FruitSlot => slots[0];
    public ItemSlot LiquidSlot => slots[1];
    public ItemSlot MashSlot => slots[2];
    
    public override int Count => 3;
    
    public override ItemSlot this[int slotId]
    {
        get => slotId >= 0 && slotId < 3 ? slots[slotId] : null;
        set => slots[slotId] = value;
    }
    
    public InventoryEFruitPress(string inventoryID, ICoreAPI api) : base(inventoryID, api)
    {
        slots = new ItemSlot[3];
        for (int i = 0; i < 3; i++)
        {
            slots[i] = NewSlot(i);
        }
    }
    
    public InventoryEFruitPress() : base(null, null)
    {
        slots = new ItemSlot[3];
        for (int i = 0; i < 3; i++)
        {
            slots[i] = NewSlot(i);
        }
    }
    
    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        // Простая загрузка слотов
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] ??= NewSlot(i);
            slots[i].Itemstack = tree.GetItemstack("slot" + i);
        }
    }
    
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        // Простое сохранение слотов
        for (int i = 0; i < slots.Length; i++)
        {
            tree.SetItemstack("slot" + i, slots[i]?.Itemstack);
        }
    }
    
    protected override ItemSlot NewSlot(int i)
    {
        // Для простоты все слоты обычные
        return new ItemSlotSurvival(this);
    }
}