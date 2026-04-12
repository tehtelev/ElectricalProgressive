using System.IO;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.ItemInsertionPipe;

/// <summary>
/// Инвентарь для фильтрующей трубы. Обеспечивает фильтрацию предметов
/// при передаче между соседними контейнерами.
/// </summary>
public class InventoryInsertionPipe : InventoryGeneric
{
    private BEItemInsertionPipe _entity;

    /// <summary>
    /// Конструктор инвентаря трубы.
    /// </summary>
    public InventoryInsertionPipe(int slots, string className, string instanceID, ICoreAPI api, BEItemInsertionPipe entity)
        : base(slots, className, instanceID, api)
    {
        _entity = entity;
    }

    // --- Создание слотов фильтра ---

    /// <summary>
    /// Фабричный метод для создания специальных слотов фильтра.
    /// </summary>
    private static ItemSlot CreateFilterSlot(int slotId, InventoryBase inventory)
    {
        return new FilterSlot(inventory);
    }

    /// <summary>
    /// Переопределение метода создания слота для использования фильтрующих слотов.
    /// </summary>
    protected override ItemSlot NewSlot(int i)
    {
        return CreateFilterSlot(i, this);
    }

    // --- Автопередача предметов ---

    /// <summary>
    /// Получение слота для автопуша из соседних контейнеров.
    /// Фильтрующая труба принимает предметы в пустые слоты фильтра.
    /// </summary>
    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        // Ищем первый пустой слот для приема предмета фильтрации
        for (int i = 0; i < Count; i++)
        {
            if (this[i] != null && this[i].Empty)
                return this[i];
        }

        return null;
    }

    /// <summary>
    /// Получение слота для автопулла в соседние контейнеры.
    /// Фильтрующая труба не отдает предметы автоматически (они остаются в фильтре).
    /// </summary>
    public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
    {
        return null;
    }

    // --- Слот фильтра ---

    /// <summary>
    /// Специализированный слот для хранения одного предмета фильтрации.
    /// Предметы в этом слоте удаляются при клике (исчезают).
    /// </summary>
    public class FilterSlot : ItemSlotSurvival
    {
        public FilterSlot(InventoryBase inventory) : base(inventory)
        {
        }

        /// <summary>
        /// Максимальный размер стопки в слоте (всегда 1 предмет).
        /// </summary>
        public override int MaxSlotStackSize => 1;

        /// <summary>
        /// Обработка активации слота (левый клик).
        /// </summary>
        public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
        {
            // --- Клик по заполненному слоту пустой рукой ---
            if (sourceSlot == null || sourceSlot.Empty)
            {
                if (!this.Empty)
                {
                    // Удаление предмета из фильтра (предмет исчезает)
                    this.Itemstack = null;
                    this.MarkDirty();

                    op.MovedQuantity = 1;
                    op.RequestedQuantity = 1;

                    // Воспроизведение звука удаления
                    if (inventory.Api is ICoreClientAPI clientApi)
                    {
                        clientApi.World.PlaySoundAt(new AssetLocation("sounds/player/drop"),
                            clientApi.World.Player.Entity, null, true, 16f);
                    }
                }
                else
                {
                    // Клик по пустому слоту - ничего не происходит
                    op.MovedQuantity = 0;
                    op.RequestedQuantity = 0;
                }

                return;
            }

            ItemStack sourceStack = sourceSlot.Itemstack;

            // --- Заполнение пустого слота предметом из руки ---
            if (this.Empty)
            {
                this.Itemstack = sourceStack.Clone();
                this.Itemstack.StackSize = 1;

                // Предмет остается в руке, не уменьшается
                this.MarkDirty();

                op.MovedQuantity = 1;
                op.RequestedQuantity = 1;
            }
            else
            {
                // --- Замена предмета в заполненном слоте ---
                if (this.CanHold(sourceSlot))
                {
                    // Удаляем старый предмет (исчезает) и кладем копию из руки
                    this.Itemstack = sourceStack.Clone();
                    this.Itemstack.StackSize = 1;

                    this.MarkDirty();

                    op.MovedQuantity = 1;
                    op.RequestedQuantity = 1;
                }
                else
                {
                    // Нельзя заменить - операция отменяется
                    op.MovedQuantity = 0;
                    op.RequestedQuantity = 0;
                }
            }
        }

        /// <summary>
        /// Обработка правого клика (альтернативное удаление).
        /// </summary>
        protected override void ActivateSlotRightClick(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
        {
            // ПКМ работает аналогично ЛКМ пустой рукой
            ActivateSlot(null, ref op);
        }

        /// <summary>
        /// Предотвращение стандартного переворота слотов.
        /// </summary>
        public override bool TryFlipWith(ItemSlot itemSlot)
        {
            if (itemSlot != null && itemSlot.StackSize > 0)
            {
                // Заполнение пустого слота копией предмета
                if (!this.Empty)
                {
                    ItemStack singleStack = itemSlot.Itemstack.Clone();
                    singleStack.StackSize = 1;
                    this.Itemstack = singleStack;

                    this.MarkDirty();
                    return true;
                }
                else
                {
                    // Заполнение пустого слота копией предмета
                    ItemStack singleStack = itemSlot.Itemstack.Clone();
                    singleStack.StackSize = 1;
                    this.Itemstack = singleStack;

                    this.MarkDirty();
                    return true;
                }
            }

            return base.TryFlipWith(itemSlot);
        }

        /// <summary>
        /// Автоматическое ограничение размера стопки до 1 предмета.
        /// </summary>
        public override void OnItemSlotModified(ItemStack extractedStack = null)
        {
            if (!this.Empty && this.Itemstack.StackSize > 1)
                this.Itemstack.StackSize = 1;

            base.OnItemSlotModified(extractedStack);
        }

        /// <summary>
        /// Предотвращение стандартного взятия предметов из слота.
        /// </summary>
        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        {
            return false; // Взятие только через клики
        }

        /// <summary>
        /// Предотвращение стандартного взятия предметов.
        /// </summary>
        public override bool CanTake()
        {
            return false; // Взятие только по клику на слот
        }

        /// <summary>
        /// Проверка возможности размещения предмета в слоте.
        /// Всегда возвращает true - можно положить копию любого предмета.
        /// </summary>
        public override bool CanHold(ItemSlot sourceSlot)
        {
            if (sourceSlot == null || sourceSlot.Empty)
                return true;

            // Можно разместить копию любого предмета
            return true;
        }
    }
}