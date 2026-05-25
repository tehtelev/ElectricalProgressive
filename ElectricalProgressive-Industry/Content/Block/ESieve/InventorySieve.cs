// InventorySieve.cs
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ESieve;

public class InventorySieve : InventoryGeneric
{
    private BlockEntityESieve _entity;
    private int lastSlot0Count = -1;
    private long lastSlot0UpdateTime = 0;
    private const long DelayMs = 2000;

    public InventorySieve(ICoreAPI api)
        : base(api)
    {
    }

    public InventorySieve(int slots, string className, string instanceID, ICoreAPI api, NewSlotDelegate onNewSlot, BlockEntityESieve entity)
        : base(slots, className, instanceID, api)
    {
        _entity = entity;
    }

    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        return base.GetSuitability(sourceSlot, targetSlot, isMerge);
    }

    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        return this.slots[0]; // только входной слот
    }

    public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
    {
        // Проверяем входной слот
        var currentCount = this[0].Itemstack?.StackSize ?? 0;

        if (currentCount != lastSlot0Count)
        {
            lastSlot0Count = currentCount;
            lastSlot0UpdateTime = _entity.Api.World.ElapsedMilliseconds;
        }

        var hasRecipe = !this[0].Empty && BlockEntityESieve.FindMatchingRecipe(ref _entity.CurrentRecipe, ref _entity.CurrentRecipeName, this);

        if (!hasRecipe || _entity.CurrentRecipe == null)
        {
            if (_entity.Api.World.ElapsedMilliseconds - lastSlot0UpdateTime > DelayMs)
            {
                lastSlot0UpdateTime = _entity.Api.World.ElapsedMilliseconds;
                return this[0];
            }
        }

        // Выдаем непустые выходные слоты (1-25)
        for (var i = 1; i < this.Count; i++)
        {
            if (!this[i].Empty)
                return this[i];
        }

        return null!;
    }
}