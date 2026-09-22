using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EAcidAccum;

public class InventoryEAcidAccum : InventoryBase, ISlotProvider
{
    public static readonly AssetLocation SulfuricAcidCode = new("game", "acid-full-sulfuric");

    private ItemSlot[] slots;
    private BlockPos? _pos;
    private ICoreAPI? _api;

    public ItemSlot[] Slots => slots;
    public ItemSlot LiquidSlot => slots[0];
    public override int Count => 1;

    public override ItemSlot this[int slotId]
    {
        get
        {
            if (slotId < 0 || slotId >= 1)
                return null!;
            return slots[slotId];
        }
        set
        {
            if (slotId < 0 || slotId >= 1)
                throw new ArgumentOutOfRangeException(nameof(slotId));
            slots[slotId] = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    public InventoryEAcidAccum(string inventoryID, ICoreAPI api) : base(inventoryID, api)
    {
        _api = api;
        slots = new ItemSlot[1];
        InitializeSlots();
    }

    public InventoryEAcidAccum() : base(null!, null!)
    {
        slots = new ItemSlot[1];
        InitializeSlots();
    }

    public override void LateInitialize(string inventoryID, ICoreAPI api)
    {
        base.LateInitialize(inventoryID, api);
        _api = api;
        InitializeSlots();
        UpdateLiquidSlotCapacity();
    }

    public void SetBlockPos(BlockPos pos)
    {
        _pos = pos;
        UpdateLiquidSlotCapacity();
    }

    public static bool IsSulfuricAcid(ItemStack? stack)
    {
        return stack?.Collectible?.Code != null && stack.Collectible.Code.Equals(SulfuricAcidCode);
    }

    public void UpdateLiquidSlotCapacity()
    {
        if (slots[0] is not ItemSlotLiquidOnly liquidSlot)
            return;

        float capacity = GetLiquidCapacityFromBlock();
        var field = typeof(ItemSlotLiquidOnly).GetField("CapacityLitres",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field?.SetValue(liquidSlot, capacity);

        if (liquidSlot.Empty || liquidSlot.Itemstack == null)
            return;

        var props = BlockLiquidContainerBase.GetContainableProps(liquidSlot.Itemstack);
        if (props == null)
            return;

        float maxStackSize = capacity * props.ItemsPerLitre;
        if (liquidSlot.StackSize > maxStackSize)
        {
            liquidSlot.Itemstack.StackSize = (int)maxStackSize;
            liquidSlot.MarkDirty();
        }
    }

    private void InitializeSlots()
    {
        if (slots[0] == null)
            slots[0] = NewSlot(0);
    }

    private float GetLiquidCapacityFromBlock()
    {
        if (_api == null || _pos == null)
            return 100f;
        var block = _api.World.BlockAccessor.GetBlock(_pos);
        if (block?.Attributes?["capacityLitres"].Exists == true)
            return block.Attributes["capacityLitres"].AsFloat(100f);
        return 100f;
    }

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        var loadedSlots = SlotsFromTreeAttributes(tree, slots);
        if (loadedSlots.Length > 0)
            slots[0] = loadedSlots[0];
        UpdateLiquidSlotCapacity();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        SlotsToTreeAttributes(slots, tree);
    }

    protected override ItemSlot NewSlot(int i)
    {
        return new ItemSlotLiquidOnly(this, 100f);
    }

    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        if (targetSlot == LiquidSlot && IsSulfuricAcid(sourceSlot?.Itemstack))
            return 4f;
        return 0f;
    }

    public override ItemSlot? GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        if (IsSulfuricAcid(fromSlot?.Itemstack))
            return LiquidSlot;
        return null;
    }
}
