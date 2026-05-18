using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ERecycler;

public class InventoryRecycler : InventoryGeneric
{
    private BlockEntityERecycler _entity; // ссылка на блок-сущность центрифуги
    private int lastSlot0Count = -1;        // для отслеживания изменений в слоте 0
    private long lastSlot0UpdateTime = 0;   // время последнего изменения в слоте 0
    private const long DelayMs = 2000;      // задержка 2 секунды



    public InventoryRecycler(ICoreAPI api)
        : base(api)
    {

    }

    public InventoryRecycler(int slots, string className, string instanceID, ICoreAPI api, NewSlotDelegate onNewSlot, BlockEntityERecycler entity)
        : base(slots, className, instanceID, api)
    {
        _entity = entity;
    }


    /// <summary>
    /// Автозагрузка центрифуги
    /// </summary>
    /// <param name="atBlockFace"></param>
    /// <param name="fromSlot"></param>
    /// <returns></returns>
    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        
        return this[0];
    }


    /// <summary>
    /// Автопулл из центрифуги
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
        var hasRecipe = !this[0].Empty
                        && (BlockEntityERecycler.FindMatchingRecipe(ref _entity.CurrentRecipe, ref _entity.CurrentRecipeName, this[0])
                            || BlockEntityERecycler.FindPerishProperties(ref _entity.CurrentRecipe, ref _entity.CurrentRecipeName, this[0]));

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
        for (var i = 1; i < this.Count; i++)
        {
            if (!this[i].Empty)
                return this[i];
        }

        return null!;
    }

}

