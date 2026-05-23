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
        : base(slots, className, instanceID, api)
    {
        _entity = entity;
    }

    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        if (targetSlot == this[0] || targetSlot == this[1])
        {
            var block = sourceSlot.Itemstack?.Collectible as Vintagestory.API.Common.Block;
            if (block != null && (block.BlockMaterial == EnumBlockMaterial.Ore || 
                                  block.BlockMaterial == EnumBlockMaterial.Metal ||
                                  sourceSlot.Itemstack?.Collectible.Code.Path.Contains("coal") == true))
                return 4f;
            return 0f;
        }
        
        return 0f;
    }

    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        if (this[0].Empty || GetSuitability(fromSlot, this[0], false) > 0)
            return this[0];
        if (this[1].Empty || GetSuitability(fromSlot, this[1], false) > 0)
            return this[1];
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

        var hasRecipe = !this[0].Empty && !this[1].Empty && 
                        BlockEntityEBlastFurnace.FindMatchingRecipe(ref _entity.CurrentRecipe, ref _entity.CurrentRecipeName, this);

        if (!hasRecipe || _entity.CurrentRecipe == null)
        {
            if (_entity.Api.World.ElapsedMilliseconds - lastSlotUpdateTime > DelayMs)
            {
                lastSlotUpdateTime = _entity.Api.World.ElapsedMilliseconds;
                if (!this[0].Empty) return this[0];
                if (!this[1].Empty) return this[1];
            }
        }

        for (var i = 2; i < this.Count; i++)
        {
            if (!this[i].Empty) return this[i];
        }

        return null!;
    }
}