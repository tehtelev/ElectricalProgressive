using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressiveTransport.ItemInsertionPipe;

public class InventoryInsertionPipe : InventoryGeneric
{
    private BEItemInsertionPipe _entity;

    public InventoryInsertionPipe(int slots, string className, string instanceID, ICoreAPI api, BEItemInsertionPipe entity)
        : base(slots, className, instanceID, api)
    {
        _entity = entity;
    }

    // Фабричный метод для создания специальных слотов
    private static ItemSlot CreateFilterSlot(int slotId, InventoryBase inventory)
    {
        return new FilterSlot(inventory);
    }

    // Переопределяем метод, чтобы использовать наши слоты
    protected override ItemSlot NewSlot(int i)
    {
        return CreateFilterSlot(i, this);
    }

    // Автопуш из соседних контейнеров в инвентарь трубы
    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        // Фильтрующая труба может принимать предметы для фильтрации
        // Проверяем, есть ли пустые слоты фильтра
        for (int i = 0; i < Count; i++)
        {
            if (this[i] != null && this[i].Empty)
            {
                // Можно принимать предметы для фильтров
                return this[i];
            }
        }

        return null;
    }

    // Автопулл из инвентаря трубы в соседние контейнеры
    public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
    {
        // Фильтрующая труба не отдает предметы автоматически
        // (фильтры должны оставаться в трубе)
        return null;
    }
}

public class FilterSlot : ItemSlotSurvival
{
    public FilterSlot(InventoryBase inventory) : base(inventory)
    {
    }

    public override int MaxSlotStackSize => 1;

    // Основной метод активации слота (левый клик)
    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        // Если кликаем по слоту ПУСТОЙ рукой (sourceSlot пустой)
        if (sourceSlot == null || sourceSlot.Empty)
        {
            // ЛКМ по заполненному слоту фильтра пустой рукой - предмет удаляется
            if (!this.Empty)
            {
                // Удаляем предмет из фильтра (исчезает)
                this.Itemstack = null;
                this.MarkDirty();

                op.MovedQuantity = 1;
                op.RequestedQuantity = 1;

                // Воспроизводим звук удаления
                if (inventory.Api is ICoreClientAPI clientApi)
                {
                    clientApi.World.PlaySoundAt(new AssetLocation("sounds/player/drop"),
                        clientApi.World.Player.Entity, null, true, 16f);
                }
            }
            else
            {
                // ЛКМ по пустому слоту пустой рукой - ничего не делаем
                op.MovedQuantity = 0;
                op.RequestedQuantity = 0;
            }

            return;
        }

        ItemStack sourceStack = sourceSlot.Itemstack;

        // Если кликаем по слоту с предметом в руке
        if (this.Empty)
        {
            // Слот фильтра пустой - кладем КОПИЮ предмета (предмет в руке остается)
            this.Itemstack = sourceStack.Clone();
            this.Itemstack.StackSize = 1;

            // НЕ уменьшаем количество в руке - предмет остается у игрока
            // sourceSlot.Itemstack не изменяется

            this.MarkDirty();

            op.MovedQuantity = 1;
            op.RequestedQuantity = 1;
        }
        else
        {
            // Слот фильтра заполнен - проверяем, можно ли заменить
            if (this.CanHold(sourceSlot))
            {
                // Удаляем старый предмет из фильтра (исчезает)
                // И кладем КОПИЮ предмета из руки
                this.Itemstack = sourceStack.Clone();
                this.Itemstack.StackSize = 1;

                // Старый предмет из фильтра просто исчезает
                // Предмет в руке остается неизменным

                this.MarkDirty();

                op.MovedQuantity = 1;
                op.RequestedQuantity = 1;
            }
            else
            {
                // Нельзя заменить - ничего не делаем
                op.MovedQuantity = 0;
                op.RequestedQuantity = 0;
            }
        }
    }

    // Правый клик - альтернативный способ удаления
    protected override void ActivateSlotRightClick(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        // ПКМ работает так же как ЛКМ пустой рукой
        ActivateSlot(null, ref op);
    }

    // Предотвращаем стандартное поведение TryFlipWith
    public override bool TryFlipWith(ItemSlot itemSlot)
    {
        if (itemSlot != null && itemSlot.StackSize > 0)
        {
            // Если пытаются положить предмет
            if (!this.Empty)
            {
                // Заменяем предмет в фильтре на копию
                ItemStack singleStack = itemSlot.Itemstack.Clone();
                singleStack.StackSize = 1;
                this.Itemstack = singleStack;

                // Предмет в itemSlot не уменьшается
                this.MarkDirty();
                return true;
            }
            else
            {
                // Кладем копию в пустой слот
                ItemStack singleStack = itemSlot.Itemstack.Clone();
                singleStack.StackSize = 1;
                this.Itemstack = singleStack;

                this.MarkDirty();
                return true;
            }
        }

        return base.TryFlipWith(itemSlot);
    }

    // Гарантируем, что в слоте не больше 1 предмета
    public override void OnItemSlotModified(ItemStack extractedStack = null)
    {
        if (!this.Empty && this.Itemstack.StackSize > 1)
        {
            this.Itemstack.StackSize = 1;
        }

        base.OnItemSlotModified(extractedStack);
    }

    // Переопределяем CanTakeFrom - предметы можно брать только правым кликом для удаления
    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
    {
        return false; // Нельзя брать предметы из этого слота стандартным способом
    }

    // Переопределяем CanTake - предметы можно брать только кликом по слоту
    public override bool CanTake()
    {
        return false; // Предотвращаем взятие предмета стандартным способом
    }

    // Переопределяем CanHold - всегда можно положить копию
    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (sourceSlot == null || sourceSlot.Empty)
            return true;

        // Всегда можно положить копию любого предмета
        return true;
    }
}
