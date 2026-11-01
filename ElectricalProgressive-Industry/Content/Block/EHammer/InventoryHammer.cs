using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EHammer;

public class InventoryHammer : InventoryGeneric
{
    private BlockEntityEHammer _entity;     // ссылка на блок-сущность молота
    private int lastSlot0Count = -1;        // для отслеживания изменений в слоте 0
    private long lastSlot0UpdateTime = 0;   // время последнего изменения в слоте 0
    private const long DelayMs = 2000;      // задержка 2 секунды

    public InventoryHammer(ICoreAPI api)
        : base(api)
    {

    }

    public InventoryHammer(int slots, string className, string instanceID, ICoreAPI api, NewSlotDelegate onNewSlot, BlockEntityEHammer entity)
        : base(slots, className, instanceID, api)
    {
        _entity = entity;
    }


    public override float GetSuitability(ItemSlot sourceSlot, ItemSlot targetSlot, bool isMerge)
    {
        // Слот 0 - только для входных предметов
        if (targetSlot == this[0])
        {
            return sourceSlot.Itemstack.Collectible.GrindingProps != null ? 4f : 0f;
        }
        
        // Слоты 1 и 2 - только для выходных предметов (нельзя вручную класть)
        return 0f;
    }

    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        // Автозаполнение только во входной слот
        return this[0];
    }



    /// <summary>
    /// Автопулл из молота
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
        var hasRecipe = !this[0].Empty && BlockEntityEHammer.FindMatchingRecipe(ref _entity.CurrentRecipe, ref _entity.CurrentRecipeName, this[0]);

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


    // Методы для удобного доступа к слотам
    public ItemSlot InputSlot => this[0];
    public ItemSlot OutputSlot => this[1];
    public ItemSlot SecondaryOutputSlot => this[2];
}