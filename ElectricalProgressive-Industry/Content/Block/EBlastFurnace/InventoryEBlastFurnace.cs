using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EBlastFurnace;

public class InventoryEBlastFurnace : InventoryGeneric
{
    private BlockEntityEBlastFurnace _entity;
    private int lastSlot0Count = -1;
    private int lastSlot1Count = -1;
    private long lastSlotUpdateTime = 0;
    private const long DelayMs = 2000;

    public InventoryEBlastFurnace(ICoreAPI api) : base(api) { }

    public InventoryEBlastFurnace(int slots, string className, string instanceID, ICoreAPI api, NewSlotDelegate onNewSlot, BlockEntityEBlastFurnace entity)
        : base(slots, className, instanceID, api, onNewSlot)
    {
        _entity = entity;
    }

    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        if (targetSlot is ItemSlotBlastFurnaceMold)
            return BlastFurnaceSlotUtil.IsIngotMold(sourceSlot.Itemstack) ? 5f : 0f;
        if (targetSlot is ItemSlotBlastFurnaceCharge)
            return BlastFurnaceSlotUtil.IsCharge(sourceSlot.Itemstack) ? 4f : 0f;
        return 0f;
    }

    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        if (BlastFurnaceSlotUtil.IsIngotMold(fromSlot.Itemstack) && Count > 5)
            return this[5];
        if (this[0].Empty || GetSuitability(fromSlot, this[0], false) > 0)
            return this[0];
        if (this[1].Empty || GetSuitability(fromSlot, this[1], false) > 0)
            return this[1];
        if (Count > 4 && (this[4].Empty || GetSuitability(fromSlot, this[4], false) > 0))
            return this[4];
        return this[0];
    }

    public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
    {
        var currentSlot0Count = this[0].Itemstack?.StackSize ?? 0;
        var currentSlot1Count = this[1].Itemstack?.StackSize ?? 0;

        if (currentSlot0Count != lastSlot0Count || currentSlot1Count != lastSlot1Count)
        {
            lastSlot0Count = currentSlot0Count;
            lastSlot1Count = currentSlot1Count;
            lastSlotUpdateTime = _entity.Api.World.ElapsedMilliseconds;
        }

        var hasRecipe = BlockEntityEBlastFurnace.FindMatchingRecipe(ref _entity.CurrentRecipe, ref _entity.CurrentRecipeName, this);

        if (!hasRecipe || _entity.CurrentRecipe == null)
        {
            if (_entity.Api.World.ElapsedMilliseconds - lastSlotUpdateTime > DelayMs)
            {
                lastSlotUpdateTime = _entity.Api.World.ElapsedMilliseconds;
                if (!this[0].Empty) return this[0];
                if (!this[1].Empty) return this[1];
            }
        }

        if (!this[2].Empty) return this[2];
        if (!this[3].Empty) return this[3];

        return null!;
    }
}