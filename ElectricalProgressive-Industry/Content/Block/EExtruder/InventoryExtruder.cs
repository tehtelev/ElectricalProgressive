using ElectricalProgressive.Content.Block.EHammer;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EExtruder;

public class InventoryExtruder : InventoryGeneric
{
    private BlockEntityEExtruder _entity;      // ссылка на блок-сущность пресса
    private int lastSlot0Count = -1;        // для отслеживания изменений в слоте 0
    private long lastSlot0UpdateTime = 0;   // время последнего изменения в слоте 0
    private const long DelayMs = 2000;      // задержка 2 секунды



    public InventoryExtruder(ICoreAPI api)
        : base(api)
    {

    }

    public InventoryExtruder(int slots, string className, string instanceID, ICoreAPI api, NewSlotDelegate onNewSlot, BlockEntityEExtruder entity)
        : base(slots, className, instanceID, api)
    {
        _entity = entity;
    }


    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        return targetSlot == this[0] && sourceSlot.Itemstack.Collectible.GrindingProps != null
            ? 3f
            : base.GetSuitability(sourceSlot, targetSlot, isMerge);
    }

    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        return this.slots[0];
    }


    /// <summary>
    /// Автопулл из пресса
    /// </summary>
    /// <param name="atBlockFace"></param>
    /// <returns></returns>
    public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
    {
        // Проверяем входной слот
        var currentCount = this[0].Itemstack?.StackSize ?? 0;

        // Если количество изменилось (например, загрузился новый предмет)
        if (currentCount != lastSlot0Count)
        {
            lastSlot0Count = currentCount;
            lastSlot0UpdateTime = _entity.Api.World.ElapsedMilliseconds; // запоминаем время
        }

        // есть рецепт?
        var hasRecipe = !this[0].Empty && BlockEntityEExtruder.FindMatchingRecipe(ref _entity.CurrentRecipe, ref _entity.CurrentRecipeName, this);

        if (!hasRecipe || _entity.CurrentRecipe == null)
        {
            // Выгружаем слот 0 только если прошло время задержки
            if (_entity.Api.World.ElapsedMilliseconds - lastSlot0UpdateTime > DelayMs)
            {
                lastSlot0UpdateTime = _entity.Api.World.ElapsedMilliseconds;
                return this[0];
            }

        }

        // выдаем непустые выходные
        for (var i = 2; i < this.Count; i++)
        {
            if (!this[i].Empty)
                return this[i];
        }

        return null!;
    }
}