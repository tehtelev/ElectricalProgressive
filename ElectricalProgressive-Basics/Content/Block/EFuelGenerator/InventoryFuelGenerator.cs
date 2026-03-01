﻿using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EFuelGenerator;

/// <summary>
/// Инвентарь генератора на топливе.
/// Управляет слотами для топлива и воды.
/// Реализует ISlotProvider для интеграции с системой слотов.
/// </summary>
public class InventoryFuelGenerator : InventoryBase, ISlotProvider
{
    // === Поля ===
    private ItemSlot[] slots;
    private BlockPos _pos;
    private ICoreAPI _api;
    private LiquidConfig _liquidConfig;
    
    // === Свойства ===
    
    public ItemSlot[] Slots => this.slots;
    public ItemSlot FuelSlot => this.slots[0];
    public ItemSlot WaterSlot => this.slots[1];
    public override int Count => 2;
    
    public override ItemSlot this[int slotId]
    {
        get 
        { 
            if (slotId < 0 || slotId >= 2) 
                return null; 
            return slots[slotId]; 
        }
        set
        {
            if (slotId < 0 || slotId >= 2)
                throw new ArgumentOutOfRangeException(nameof(slotId));
            slots[slotId] = value ?? throw new ArgumentNullException(nameof(value));
        }
    }
    
    // === Конструкторы ===
    
    public InventoryFuelGenerator(string inventoryID, ICoreAPI api)
        : base(inventoryID, api)
    {
        _api = api;
        slots = new ItemSlot[2];
        InitializeSlots();
    }
    
    public InventoryFuelGenerator(string className, string instanceID, ICoreAPI api)
        : base(className, instanceID, api)
    {
        _api = api;
        slots = new ItemSlot[2];
        InitializeSlots();
    }
    
    // Публичные методы
    
    public void SetBlockPos(BlockPos pos)
    {
        _pos = pos;
        UpdateWaterSlotCapacity();
    }
    
    /// <summary>
    /// Установить конфигурацию жидкости
    /// </summary>
    public void SetLiquidConfig(LiquidConfig config)
    {
        _liquidConfig = config;
        UpdateWaterSlotCapacity();
    }
    
    public void UpdateWaterSlotCapacity()
    {
        if (slots[1] is ItemSlotLiquidOnly waterSlot)
        {
            float capacity = GetWaterCapacityFromConfig();
            
            var field = typeof(ItemSlotLiquidOnly).GetField("CapacityLitres",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(waterSlot, capacity);
                
                if (!waterSlot.Empty && waterSlot.Itemstack != null)
                {
                    var props = BlockLiquidContainerBase.GetContainableProps(waterSlot.Itemstack);
                    if (props != null)
                    {
                        float maxStackSize = capacity * props.ItemsPerLitre;
                        if (waterSlot.StackSize > maxStackSize)
                        {
                            waterSlot.Itemstack.StackSize = (int)maxStackSize;
                            waterSlot.MarkDirty();
                        }
                    }
                }
            }
        }
    }
    
    // === Приватные методы ===
    
    private void InitializeSlots()
    {
        for (int i = 0; i < 2; i++)
        {
            slots[i] = NewSlot(i);
        }
    }
    
    private float GetWaterCapacityFromConfig()
    {
        if (_liquidConfig != null)
            return _liquidConfig.CapacityLitres;
            
        if (_api == null || _pos == null) return 100f;
        
        Vintagestory.API.Common.Block block = _api.World.BlockAccessor.GetBlock(_pos);
        
        if (block?.Attributes?["liquidConfig"]?["capacityLitres"].Exists == true)
        {
            return block.Attributes["liquidConfig"]["capacityLitres"].AsFloat(100f);
        }
        
        return 100f;
    }
    
    // === Публичные методы ===
    
    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        var loadedSlots = this.SlotsFromTreeAttributes(tree, this.slots);
        
        for (int i = 0; i < 2; i++)
        {
            if (i < loadedSlots.Length)
                slots[i] = loadedSlots[i];
        }
        
        UpdateWaterSlotCapacity();
    }
    
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        this.SlotsToTreeAttributes(this.slots, tree);
    }
    
    protected override ItemSlot NewSlot(int i)
    {
        if (i == 1)
        {
            float capacity = GetWaterCapacityFromConfig();
            return new ItemSlotLiquidOnly(this, capacity);
        }
        return new ItemSlotSurvival(this);
    }
    
    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        if (targetSlot == null || sourceSlot?.Itemstack == null) 
            return 0f;
        
        if (targetSlot == WaterSlot)
        {
            var props = BlockLiquidContainerBase.GetContainableProps(sourceSlot.Itemstack);
            if (props != null && props.Containable)
            {
                // Проверяем, разрешена ли жидкость
                if (_liquidConfig != null && !_liquidConfig.IsLiquidAllowed(sourceSlot.Itemstack))
                    return 0f;
                    
                return 4f;
            }
            return 0f;
        }
        
        if (targetSlot == FuelSlot && sourceSlot.Itemstack.Collectible.CombustibleProps != null)
            return 4f;
        
        return base.GetSuitability(sourceSlot, targetSlot, isMerge);
    }
    
    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        if (fromSlot?.Itemstack == null) 
            return null;
        
        var props = BlockLiquidContainerBase.GetContainableProps(fromSlot.Itemstack);
        if (props != null && props.Containable)
        {
            // Проверяем, разрешена ли жидкость
            if (_liquidConfig != null && !_liquidConfig.IsLiquidAllowed(fromSlot.Itemstack))
                return null;
                
            return WaterSlot;
        }
        
        if (fromSlot.Itemstack.Collectible.CombustibleProps != null)
            return FuelSlot;
        
        return null;
    }
}