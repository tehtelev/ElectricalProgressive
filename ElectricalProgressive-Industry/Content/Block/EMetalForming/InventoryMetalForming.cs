using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EMetalForming;

public class InventoryMetalForming : InventoryGeneric
{
    private readonly BlockEntityEMetalForming _entity;
    private int _lastSlot0Count = -1;
    private long _lastSlot0UpdateTime;
    private const long DelayMs = 2000;

    public InventoryMetalForming(ICoreAPI api) : base(api)
    {
    }

    public InventoryMetalForming(int slots, string className, string instanceID, ICoreAPI api,
        NewSlotDelegate onNewSlot, BlockEntityEMetalForming entity)
        : base(slots, className, instanceID, api)
    {
        _entity = entity;
    }

    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        if (targetSlot != this[0])
            return 0f;
        if (!BlockEntityEMetalForming.IsWorkableInput(sourceSlot?.Itemstack))
            return 0f;
        if (_entity.SelectedRecipe != null && !_entity.StackMatchesSelectedRecipe(sourceSlot!.Itemstack))
            return 0f;
        return 4f;
    }

    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        return this[0];
    }

    public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
    {
        for (var i = 1; i < Count; i++)
        {
            if (!this[i].Empty)
                return this[i];
        }

        // не забирать вход, пока ждём партию выбранного рецепта
        if (_entity.SelectedRecipe != null && _entity.RecipeMatchesInput())
            return null!;

        var currentCount = this[0].Itemstack?.StackSize ?? 0;
        if (currentCount != _lastSlot0Count)
        {
            _lastSlot0Count = currentCount;
            _lastSlot0UpdateTime = _entity.Api.World.ElapsedMilliseconds;
        }

        if (!this[0].Empty &&
            _entity.Api.World.ElapsedMilliseconds - _lastSlot0UpdateTime > DelayMs)
        {
            _lastSlot0UpdateTime = _entity.Api.World.ElapsedMilliseconds;
            return this[0];
        }

        return null!;
    }

    public ItemSlot InputSlot => this[0];
    public ItemSlot OutputSlot => this[1];
    public ItemSlot BitsSlot => this[2];
}
