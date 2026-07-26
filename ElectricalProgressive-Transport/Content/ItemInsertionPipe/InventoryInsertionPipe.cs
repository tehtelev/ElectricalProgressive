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
    internal const string FrozenTemperatureAttribute = "epFrozenFilterTemperature";

    private BEItemInsertionPipe _entity;
    private readonly ItemStack?[] filterSnapshots;

    /// <summary>
    /// Конструктор инвентаря трубы.
    /// </summary>
    public InventoryInsertionPipe(int slots, string className, string instanceID, ICoreAPI api, BEItemInsertionPipe entity)
        : base(slots, className, instanceID, api)
    {
        _entity = entity;
        filterSnapshots = new ItemStack?[slots];
    }

    // --- Создание слотов фильтра ---

    /// <summary>
    /// Фабричный метод для создания специальных слотов фильтра.
    /// </summary>
    private static ItemSlot CreateFilterSlot(int slotId, InventoryBase inventory)
    {
        return new FilterSlot(slotId, inventory);
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
        // Фильтр заполняется только кликом игрока: это снимок предмета, а не реальный инвентарь.
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

    internal bool IsFilterSet(int slotId)
    {
        return IsValidSlotId(slotId) && filterSnapshots[slotId]?.Collectible != null;
    }

    internal ItemStack? GetFilterSnapshot(int slotId)
    {
        return IsValidSlotId(slotId) ? filterSnapshots[slotId] : null;
    }

    internal int CountActiveFilterSnapshots()
    {
        int count = 0;
        for (int i = 0; i < filterSnapshots.Length; i++)
        {
            if (filterSnapshots[i]?.Collectible != null)
                count++;
        }

        return count;
    }

    internal void SetFilterSnapshot(int slotId, ItemStack? snapshot)
    {
        if (!IsValidSlotId(slotId))
            return;

        filterSnapshots[slotId] = snapshot?.Clone();
        RestoreFilterSlotSnapshot(slotId);
    }

    internal void ClearFilterSnapshot(int slotId)
    {
        SetFilterSnapshot(slotId, null);
    }

    internal void CaptureFilterSnapshotsFromSlots(IWorldAccessor world)
    {
        for (int i = 0; i < Count && i < filterSnapshots.Length; i++)
        {
            filterSnapshots[i] = this[i].Itemstack?.Clone();
            ResolveFilterSnapshot(filterSnapshots[i], world);
            NormalizeFilterSnapshot(filterSnapshots[i]);
        }

        RestoreFilterSlotSnapshots();
    }

    internal void MaintainFilterSnapshots(IWorldAccessor world)
    {
        for (int i = 0; i < filterSnapshots.Length; i++)
        {
            ResolveFilterSnapshot(filterSnapshots[i]);
            FreezeSnapshotTemperature(world, filterSnapshots[i]);
        }

        RestoreFilterSlotSnapshots();
    }

    internal static void FreezeSnapshotTemperature(IWorldAccessor world, ItemStack? snapshot)
    {
        if (snapshot?.Collectible?.HasTemperature(snapshot) != true)
            return;

        if (!snapshot.Attributes.HasAttribute(FrozenTemperatureAttribute))
        {
            snapshot.Attributes.SetFloat(FrozenTemperatureAttribute, snapshot.Collectible.GetTemperature(world, snapshot));
        }

        float temperature = snapshot.Attributes.GetFloat(FrozenTemperatureAttribute);
        snapshot.Collectible.SetTemperature(world, snapshot, temperature, true);
    }

    private void RestoreFilterSlotSnapshots()
    {
        for (int i = 0; i < filterSnapshots.Length && i < Count; i++)
        {
            RestoreFilterSlotSnapshot(i);
        }
    }

    private void RestoreFilterSlotSnapshot(int slotId)
    {
        if (!IsValidSlotId(slotId))
            return;

        ResolveFilterSnapshot(filterSnapshots[slotId]);

        ItemStack? visibleSnapshot = filterSnapshots[slotId]?.Clone();
        ResolveFilterSnapshot(visibleSnapshot);
        NormalizeFilterSnapshot(visibleSnapshot);

        this[slotId].Itemstack = visibleSnapshot?.Collectible == null ? null : visibleSnapshot;

        // MarkDirty requires inventory.Api; during chunk FromTreeAttributes Api is still null
        // and DidModifyItemSlot throws NRE, which discards the whole block entity.
        if (Api != null)
            this[slotId].MarkDirty();
    }

    private void ResolveFilterSnapshot(ItemStack? snapshot)
    {
        ResolveFilterSnapshot(snapshot, Api?.World);
    }

    private static void ResolveFilterSnapshot(ItemStack? snapshot, IWorldAccessor? world)
    {
        if (snapshot != null && snapshot.Collectible == null && world != null)
            snapshot.ResolveBlockOrItem(world);
    }

    private static void NormalizeFilterSnapshot(ItemStack? snapshot)
    {
        if (snapshot != null)
            snapshot.StackSize = 1;
    }

    private bool IsValidSlotId(int slotId)
    {
        return slotId >= 0 && slotId < filterSnapshots.Length && slotId < Count;
    }

    // --- Слот фильтра ---

    /// <summary>
    /// Специализированный слот для хранения снимка предмета фильтрации.
    /// Предмет в этом слоте не является реальным инвентарным предметом.
    /// </summary>
    public class FilterSlot : ItemSlot
    {
        private readonly int slotId;

        public FilterSlot(int slotId, InventoryBase inventory) : base(inventory)
        {
            this.slotId = slotId;
        }

        private InventoryInsertionPipe FilterInventory => (InventoryInsertionPipe)inventory;

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
                if (FilterInventory.IsFilterSet(slotId))
                {
                    // Удаление предмета из фильтра (предмет исчезает)
                    FilterInventory.ClearFilterSnapshot(slotId);

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
            ItemStack filterSnapshot = CreateFilterSnapshot(sourceStack);

            // --- Заполнение пустого слота предметом из руки ---
            if (this.Empty)
            {
                // Предмет остается в руке, не уменьшается
                FilterInventory.SetFilterSnapshot(slotId, filterSnapshot);

                op.MovedQuantity = 1;
                op.RequestedQuantity = 1;
            }
            else
            {
                // --- Замена предмета в заполненном слоте ---
                if (this.CanHold(sourceSlot))
                {
                    // Удаляем старый предмет (исчезает) и кладем копию из руки
                    FilterInventory.SetFilterSnapshot(slotId, filterSnapshot);

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

        private ItemStack CreateFilterSnapshot(ItemStack sourceStack)
        {
            ItemStack snapshot = sourceStack.Clone();
            snapshot.StackSize = 1;

            if (inventory.Api?.World != null)
            {
                FreezeSnapshotTemperature(inventory.Api.World, snapshot);
            }

            return snapshot;
        }

        /// <summary>
        /// Предотвращение стандартного переворота слотов.
        /// </summary>
        public override bool TryFlipWith(ItemSlot itemSlot)
        {
            if (itemSlot != null && itemSlot.StackSize > 0)
            {
                FilterInventory.SetFilterSnapshot(slotId, CreateFilterSnapshot(itemSlot.Itemstack));
                return true;
            }

            return base.TryFlipWith(itemSlot);
        }

        /// <summary>
        /// Автоматическое ограничение размера стопки до 1 предмета.
        /// </summary>
        public override void OnItemSlotModified(ItemStack? extractedStack = null)
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
