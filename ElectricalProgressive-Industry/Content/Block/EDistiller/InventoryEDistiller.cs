using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EDistiller;

public class InventoryEDistiller : InventoryBase, ISlotProvider
{
    public const int MashSlotId = 0;
    public const int WaterSlotId = 1;
    public const int ProductSlotId = 2;

    private ItemSlot[] _slots;
    private BlockPos _pos;
    private ICoreAPI _api;

    public ItemSlot[] Slots => _slots;
    public ItemSlot MashSlot => _slots[MashSlotId];
    public ItemSlot WaterSlot => _slots[WaterSlotId];
    public ItemSlot ProductSlot => _slots[ProductSlotId];

    public override int Count => 3;

    public override ItemSlot this[int slotId]
    {
        get
        {
            if (slotId < 0 || slotId >= Count)
                return null!;
            return _slots[slotId];
        }
        set
        {
            if (slotId < 0 || slotId >= Count)
                throw new ArgumentOutOfRangeException(nameof(slotId));
            _slots[slotId] = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    public InventoryEDistiller() : base(null, null)
    {
        _slots = new ItemSlot[Count];
        InitializeSlots();
    }

    public InventoryEDistiller(string inventoryID, ICoreAPI api) : base(inventoryID, api)
    {
        _api = api;
        _slots = new ItemSlot[Count];
        InitializeSlots();
    }

    public override void LateInitialize(string inventoryID, ICoreAPI api)
    {
        base.LateInitialize(inventoryID, api);
        _api = api;
        InitializeSlots();
        UpdateCapacities();
    }

    public void SetBlockPos(BlockPos pos)
    {
        _pos = pos;
        UpdateCapacities();
    }

    private void InitializeSlots()
    {
        for (var i = 0; i < Count; i++)
        {
            if (_slots[i] == null)
                _slots[i] = NewSlot(i);
        }
    }

    public void UpdateCapacities()
    {
        SetCapacity(MashSlot, GetAttr("mashCapacityLitres", 100f));
        SetCapacity(WaterSlot, GetAttr("waterCapacityLitres", 50f));
        SetCapacity(ProductSlot, GetAttr("productCapacityLitres", 50f));
    }

    private float GetAttr(string key, float fallback)
    {
        if (_api == null || _pos == null)
            return fallback;
        var block = _api.World.BlockAccessor.GetBlock(_pos);
        if (block?.Attributes?[key].Exists == true)
            return block.Attributes[key].AsFloat(fallback);
        return fallback;
    }

    private static void SetCapacity(ItemSlot slot, float litres)
    {
        if (slot is not ItemSlotLiquidOnly liquid)
            return;

        var field = typeof(ItemSlotLiquidOnly).GetField("CapacityLitres",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field?.SetValue(liquid, litres);

        if (liquid.Empty || liquid.Itemstack == null)
            return;

        var props = BlockLiquidContainerBase.GetContainableProps(liquid.Itemstack);
        if (props == null)
            return;

        var maxStack = litres * props.ItemsPerLitre;
        if (liquid.StackSize > maxStack)
        {
            liquid.Itemstack.StackSize = (int)maxStack;
            liquid.MarkDirty();
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        var loaded = SlotsFromTreeAttributes(tree, _slots);
        for (var i = 0; i < Count && i < loaded.Length; i++)
            _slots[i] = loaded[i];
        UpdateCapacities();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        SlotsToTreeAttributes(_slots, tree);
    }

    protected override ItemSlot NewSlot(int i)
    {
        return i switch
        {
            MashSlotId => new ItemSlotLiquidOnly(this, 100f),
            WaterSlotId => new ItemSlotLiquidOnly(this, 50f),
            _ => new ItemSlotLiquidOnly(this, 50f)
        };
    }

    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        if (targetSlot == null || sourceSlot?.Itemstack == null)
            return 0f;

        var props = BlockLiquidContainerBase.GetContainableProps(sourceSlot.Itemstack);
        if (props == null || !props.Containable)
            return 0f;

        var path = sourceSlot.Itemstack.Collectible.Code.Path;
        if (targetSlot == WaterSlot)
            return path.Contains("water") ? 4f : 0f;
        if (targetSlot == MashSlot)
            return path.Contains("water") ? 0f : 3f;
        if (targetSlot == ProductSlot)
            return 0f;

        return base.GetSuitability(sourceSlot, targetSlot, isMerge);
    }

    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        if (fromSlot?.Itemstack == null)
            return null!;

        var props = BlockLiquidContainerBase.GetContainableProps(fromSlot.Itemstack);
        if (props == null || !props.Containable)
            return null!;

        var path = fromSlot.Itemstack.Collectible.Code.Path;
        if (path.Contains("water"))
            return WaterSlot;
        return MashSlot;
    }

    public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
    {
        if (!ProductSlot.Empty)
            return ProductSlot;
        return null!;
    }
}
