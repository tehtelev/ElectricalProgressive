// InventoryLiquidInsertionPipe.cs
// ================================================
// Инвентарь для фильтрации жидкостей в трубе
// Содержит слоты, принимающие только жидкости

using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.LiquidInsertionPipe;

public class InventoryLiquidInsertionPipe : InventoryGeneric
{
    internal const string FrozenTemperatureAttribute = "epFrozenLiquidFilterTemperature";

    private BELiquidInsertionPipe _entity;
    private readonly ItemStack?[] filterSnapshots;

    /// <summary>
    /// Конструктор инвентаря.
    /// </summary>
    /// <param name="slots">Количество слотов</param>
    /// <param name="className">Класс инвентаря</param>
    /// <param name="instanceID">Идентификатор экземпляра</param>
    /// <param name="api">API сервера</param>
    /// <param name="entity">Ссылка на блок-сущность трубы</param>
    public InventoryLiquidInsertionPipe(int slots, string className, string instanceID, ICoreAPI api, BELiquidInsertionPipe entity)
        : base(slots, className, instanceID, api)
    {
        _entity = entity;
        filterSnapshots = new ItemStack?[slots];
    }

    /// <summary>
    /// Создает слот фильтра жидкости.
    /// </summary>
    private static ItemSlot CreateLiquidFilterSlot(int slotId, InventoryBase inventory)
    {
        return new LiquidFilterSlot(slotId, inventory);
    }

    /// <summary>
    /// Создает новый слот в инвентаре (по умолчанию - слот фильтра).
    /// </summary>
    protected override ItemSlot NewSlot(int i)
    {
        return CreateLiquidFilterSlot(i, this);
    }

    /// <summary>
    /// Фильтр заполняется только кликом игрока: это снимок жидкости, а не реальный инвентарь.
    /// </summary>
    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        return null;
    }

    /// <summary>
    /// Снимки фильтра не должны выкачиваться автоматикой или трубами других модов.
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
}

/// <summary>
/// Слот для фильтрации жидкостей.
/// Принимает только жидкости и блоки с контейнером для жидкости.
/// </summary>
public class LiquidFilterSlot : ItemSlot
{
    private readonly int slotId;

    /// <summary>
    /// Конструктор слота фильтра.
    /// </summary>
    public LiquidFilterSlot(int slotId, InventoryBase inventory)
        : base(inventory)
    {
        this.slotId = slotId;
    }

    private InventoryLiquidInsertionPipe FilterInventory => (InventoryLiquidInsertionPipe)inventory;

    /// <summary>
    /// Максимальный размер стопки в слоте.
    /// В фильтре допускается только 1 предмет на слот.
    /// </summary>
    public override int MaxSlotStackSize => 1; // В фильтре только 1 предмет

    /// <summary>
    /// Проверяет, можно ли положить предмет в этот слот.
    /// Разрешает только жидкости и блоки с контейнером для жидкости.
    /// </summary>
    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (sourceSlot == null || sourceSlot.Empty)
            return true; // Всегда можно очистить пустой слот

        ItemStack sourceStack = sourceSlot.Itemstack;

        // Проверка 1: Прямая проверка через IsLiquid() - самый надежный способ
        if (sourceStack.Collectible != null && sourceStack.Collectible.IsLiquid())
            return true;

        // Проверка 2: Проверяем, является ли это BlockLiquidContainerBase (ведра)
        if (sourceStack.Block is BlockLiquidContainerBase)
            return true;

        // Проверка 3: Проверяем атрибуты контейнера с жидкостью
        if (sourceStack.ItemAttributes != null)
        {
            // Контейнеры с жидкостью имеют contentItemCode
            if (sourceStack.ItemAttributes["contentItemCode"].Exists)
            {
                string contentCode = sourceStack.ItemAttributes["contentItemCode"].AsString();
                if (!string.IsNullOrEmpty(contentCode))
                {
                    // Получаем предмет содержимого
                    AssetLocation contentAsset = AssetLocation.Create(contentCode, sourceStack.Collectible.Code.Domain);
                    Vintagestory.API.Common.Item contentItem = this.inventory.Api.World.GetItem(contentAsset);

                    if (contentItem != null && contentItem.IsLiquid())
                    {
                        return true;
                    }
                }
                // Даже если не смогли проверить - предполагаем что это жидкость
                return true;
            }

            // Или contentItem2BlockCodes (для бутылок)
            if (sourceStack.ItemAttributes["contentItem2BlockCodes"].Exists)
                return true;

            // Проверяем атрибут containerType на "liquid" или "portion"
            if (sourceStack.ItemAttributes["containerType"].Exists)
            {
                string containerType = sourceStack.ItemAttributes["containerType"].AsString();
                if (containerType?.ToLower() == "liquid" || containerType?.ToLower() == "portion")
                    return true;
            }

            // Проверяем атрибут liquidProps
            if (sourceStack.ItemAttributes["liquidProps"].Exists)
                return true;
        }

        // Проверка 4: Проверяем через ItemLadle (черпаки)
        if (sourceStack.Collectible.Code?.Path?.Contains("ladle") == true)
            return true;

        // Проверка 5: Для предметов с атрибутом "content" - предполагаем жидкость
        if (sourceStack.Attributes?.HasAttribute("content") == true)
            return true;

        // Ни один из проверенных способов не подтвердил что это жидкость - НЕ допускаем
        return false;
    }

    /// <summary>
    /// Проверяет, можно ли взять предмет из слота.
    /// В фильтре предметы можно брать только правым кликом для очистки.
    /// </summary>
    public override bool CanTake()
    {
        // В фильтре предметы можно брать только правым кликом для очистки
        return false;
    }

    /// <summary>
    /// Запрещает вытягивание снимка фильтра стандартной логикой слотов.
    /// </summary>
    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
    {
        return false;
    }

    /// <summary>
    /// Обработка активации слота (левый клик).
    /// Запоминает снимок жидкости без изменения исходного предмета.
    /// </summary>
    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        // Если кликаем пустой рукой - очищаем слот
        if (sourceSlot == null || sourceSlot.Empty)
        {
            if (FilterInventory.IsFilterSet(slotId))
            {
                FilterInventory.ClearFilterSnapshot(slotId);
                op.MovedQuantity = 1;
            }
            return;
        }

        // Сначала проверяем, можно ли вообще положить этот предмет
        if (!CanHold(sourceSlot))
        {
            // Не жидкость - ничего не делаем
            op.MovedQuantity = 0;
            op.RequestedQuantity = 0;
            return;
        }

        ItemStack filterSnapshot = CreateLiquidFilterSnapshot(sourceSlot.Itemstack);
        if (filterSnapshot == null)
        {
            op.MovedQuantity = 0;
            op.RequestedQuantity = 0;
            return;
        }

        FilterInventory.SetFilterSnapshot(slotId, filterSnapshot);

        op.MovedQuantity = 1;
        op.RequestedQuantity = 1;
    }

    private ItemStack? CreateLiquidFilterSnapshot(ItemStack? sourceStack)
    {
        ItemStack? snapshot = null;

        if (sourceStack?.Block is BlockLiquidContainerBase block)
        {
            snapshot = block.GetContent(sourceStack)?.Clone();
        }
        else if (sourceStack?.ItemAttributes?["contentItemCode"].Exists == true)
        {
            string contentCode = sourceStack.ItemAttributes["contentItemCode"].AsString();
            if (!string.IsNullOrEmpty(contentCode) && inventory.Api?.World != null)
            {
                AssetLocation contentAsset = AssetLocation.Create(contentCode, sourceStack.Collectible.Code.Domain);
                Vintagestory.API.Common.Item contentItem = inventory.Api.World.GetItem(contentAsset);
                if (contentItem != null)
                    snapshot = new ItemStack(contentItem);
            }
        }
        else if (sourceStack?.Collectible != null)
        {
            snapshot = sourceStack.Clone();
        }

        if (snapshot == null)
            return null;

        snapshot.StackSize = 1;
        FreezeSnapshotTemperature(snapshot);
        return snapshot;
    }

    private void FreezeSnapshotTemperature(ItemStack snapshot)
    {
        if (inventory.Api?.World == null)
            return;

        InventoryLiquidInsertionPipe.FreezeSnapshotTemperature(inventory.Api.World, snapshot);
    }

    /// <summary>
    /// Обработка правого клика для очистки слота.
    /// Очищает содержимое слота, если клик с пустой рукой.
    /// </summary>
    protected override void ActivateSlotRightClick(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        // Если кликаем правой кнопкой с пустой рукой - очищаем слот
        if (sourceSlot == null || sourceSlot.Empty)
        {
            if (FilterInventory.IsFilterSet(slotId))
            {
                FilterInventory.ClearFilterSnapshot(slotId);
                op.MovedQuantity = 1;
            }
            return;
        }

        // Для предметов с жидкостью используем специальную логику
        if (sourceSlot.Itemstack?.Block is BlockLiquidContainerBase ||
            sourceSlot.Itemstack?.ItemAttributes?["contentItemCode"].Exists == true ||
            sourceSlot.Itemstack?.Collectible.IsLiquid() == true)
        {
            // Используем левый клик для жидкостей
            ActivateSlot(sourceSlot, ref op);
            return;
        }

        // Для не-жидкостей - ничего не делаем
        op.MovedQuantity = 0;
        op.RequestedQuantity = 0;
    }

    /// <summary>
    /// Переопределяем TryFlipWith для снимка фильтра без перемещения исходного предмета.
    /// </summary>
    public override bool TryFlipWith(ItemSlot itemSlot)
    {
        // Сначала проверяем, можно ли вообще поместить этот предмет
        if (!CanHold(itemSlot))
            return false;

        if (itemSlot != null && itemSlot.StackSize > 0)
        {
            ItemStack? filterSnapshot = CreateLiquidFilterSnapshot(itemSlot.Itemstack);
            if (filterSnapshot == null)
                return false;

            FilterInventory.SetFilterSnapshot(slotId, filterSnapshot);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Гарантируем, что в слоте не больше 1 предмета.
    /// </summary>
    public override void OnItemSlotModified(ItemStack? extractedStack = null)
    {
        if (!this.Empty && this.Itemstack.StackSize > 1)
        {
            this.Itemstack.StackSize = 1;
        }
        base.OnItemSlotModified(extractedStack);
    }
}
