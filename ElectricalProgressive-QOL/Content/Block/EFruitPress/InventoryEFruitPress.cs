using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EFruitPress;

/// <summary>
/// Специальный слот для жидкости в прессе - нельзя взять кликом, но можно налить/слить
/// </summary>
public class FruitPressLiquidSlot : ItemSlotLiquidOnly
{
    public FruitPressLiquidSlot(InventoryBase inventory, float capacityLitres) 
        : base(inventory, capacityLitres)
    {
    }
    
    // Разрешаем брать только если это автоматическая операция
    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
    {
        // Разрешаем только автоматические операции и операции извлечения жидкости
        if (priority == EnumMergePriority.AutoMerge || priority == EnumMergePriority.DirectMerge)
        {
            return true;
        }
        // Запрещаем ручной клик по слоту
        return false;
    }
    
    // Разрешаем ставить жидкость
    public override bool CanHold(ItemSlot itemstackFromSourceSlot)
    {
        if (itemstackFromSourceSlot.Empty) return true;
        
        var props = BlockLiquidContainerBase.GetContainableProps(itemstackFromSourceSlot.Itemstack);
        return props != null && props.Containable;
    }
    
    // Запрещаем перетаскивание между слотами
    public override bool TryFlipWith(ItemSlot itemSlot)
    {
        return false;
    }
}

/// <summary>
/// Инвентарь пресса для сока
/// </summary>
public class InventoryEFruitPress : InventoryBase, ISlotProvider
{
    private ItemSlot[] slots;
    private BlockPos _pos;
    private ICoreAPI _api;
    
    public ItemSlot[] Slots => this.slots;
    public ItemSlot FruitSlot => this.slots[0];
    public ItemSlot LiquidSlot => this.slots[1];
    public ItemSlot MashSlot => this.slots[2];
    
    public override int Count => 3;
    
    public override ItemSlot this[int slotId]
    {
        get 
        { 
            if (slotId < 0 || slotId >= 3) 
                return null; 
            return slots[slotId]; 
        }
        set
        {
            if (slotId < 0 || slotId >= 3)
                throw new ArgumentOutOfRangeException(nameof(slotId));
            slots[slotId] = value ?? throw new ArgumentNullException(nameof(value));
        }
    }
    
    public InventoryEFruitPress(string inventoryID, ICoreAPI api)
        : base(inventoryID, api)
    {
        _api = api;
        slots = new ItemSlot[3];
        InitializeSlots();
    }
    
    public InventoryEFruitPress() : base(null, null)
    {
        slots = new ItemSlot[3];
        InitializeSlots();
    }
    
    public override void LateInitialize(string inventoryID, ICoreAPI api)
    {
        base.LateInitialize(inventoryID, api);
        _api = api;
        InitializeSlots();
    }
    
    /// <summary>
    /// Установить позицию блока для получения емкости
    /// </summary>
    public void SetBlockPos(BlockPos pos)
    {
        _pos = pos;
        UpdateLiquidSlotCapacity();
    }
    
    /// <summary>
    /// Обновить емкость слота жидкости
    /// </summary>
    public void UpdateLiquidSlotCapacity()
    {
        if (slots[1] is FruitPressLiquidSlot liquidSlot)
        {
            float capacity = GetLiquidCapacityFromBlock();
            
            // Обновляем емкость через рефлексию
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
        for (int i = 0; i < 3; i++)
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
        
        // Пробуем получить емкость через атрибуты
        if (block?.Attributes?["capacityLitres"].Exists == true)
        {
            return block.Attributes["capacityLitres"].AsFloat(100f);
        }
        
        // Проверяем, есть ли у блока свойство CapacityLitres
        var property = block?.GetType().GetProperty("CapacityLitres");
        if (property != null)
        {
            object value = property.GetValue(block);
            if (value is float floatValue)
                return floatValue;
            if (value is int intValue)
                return intValue;
        }
        
        return 100f; // Значение по умолчанию
    }
    
    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        var loadedSlots = this.SlotsFromTreeAttributes(tree, this.slots);
        
        for (int i = 0; i < 3; i++)
        {
            if (i < loadedSlots.Length)
                slots[i] = loadedSlots[i];
        }
        
        // Обновляем емкость после загрузки
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
            case 0: // Слот для фруктов
                return new ItemSlotSurvival(this);
            case 1: // Слот для жидкости (специальный, нельзя брать кликом)
                return new FruitPressLiquidSlot(this, 100f);
            case 2: // Слот для жмыха
                return new ItemSlotSurvival(this);
            default:
                return new ItemSlotSurvival(this);
        }
    }
    
    /// <summary>
    /// Определение приоритета предмета для слота
    /// </summary>
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
                return 4f; // Высокий приоритет для жидкостей
            }
            return 0f;
        }
        
        // Проверка для слота фруктов
        if (targetSlot == FruitSlot)
        {
            // Проверяем, можно ли выжать сок из этого предмета
            var juiceableProps = sourceSlot.Itemstack.ItemAttributes?["juiceableProperties"];
            if (juiceableProps != null && juiceableProps.Exists)
            {
                return 3f; // Средний приоритет для фруктов
            }
            return 0f;
        }
        
        // Проверка для слота жмыха
        if (targetSlot == MashSlot)
        {
            // Жмых обычно нельзя ставить вручную
            return 0f;
        }
        
        return base.GetSuitability(sourceSlot, targetSlot, isMerge);
    }
    
    /// <summary>
    /// Получение слота для автоматического помещения предмета
    /// </summary>
    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        if (fromSlot?.Itemstack == null) 
            return null;
        
        // Проверяем, является ли предмет жидкостью
        var props = BlockLiquidContainerBase.GetContainableProps(fromSlot.Itemstack);
        if (props != null && props.Containable)
            return LiquidSlot; // Жидкости идут в слот жидкости
        
        // Проверяем, является ли предмет фруктом для отжима
        var juiceableProps = fromSlot.Itemstack.ItemAttributes?["juiceableProperties"];
        if (juiceableProps != null && juiceableProps.Exists)
            return FruitSlot; // Фрукты идут в слот фруктов
        
        return null; // Другие предметы не принимаются
    }
}