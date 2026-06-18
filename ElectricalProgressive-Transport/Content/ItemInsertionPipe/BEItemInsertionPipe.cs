using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.ItemInsertionPipe;

/// <summary>
/// Фильтрующая труба. Позволяет передавать предметы только из списка разрешенных или запрещенных.
/// Также содержит настройки для замедления порчи предметов в пути.
/// </summary>
public class BEItemInsertionPipe : BlockEntityPipeBase
{
    // === Поля состояния ===

    private long transferTimer;       // Таймер передачи (для сервера)
    private long perishTimer;         // Таймер обработки порчи (для сервера)
    private int transferRate = 1;     // Скорость передачи предметов в секунду
    private BlockFacing outputFacing = null; // Направление вывода предметов
    private int debugCounter = 0;     // Счетчик отладки
    private int sourceCursor = 0;     // Round-robin позиция поиска источников
    private BlockPos preferredSourcePos = null; // Последний источник, из которого успешно забрали предмет

    // === Собственный инвентарь (фильтры) ===
    internal InventoryInsertionPipe _inventory;   // Инвентарь для хранения фильтровых слотов
    private GuiDialogInsertionPipe _clientDialog; // GUI диалог для клиента

    // === Режимы работы фильтра ===
    public enum FilterMode
    {
        AllowList = 0, // Разрешать только указанные предметы (белый список)
        DenyList = 1   // Запрещать указанные предметы (черный список)
    }

    private FilterMode currentFilterMode = FilterMode.AllowList;
    private bool matchMod = false;          // Совпадать по мод-идентификатору (домену)
    private bool matchType = true;          // Совпадать по типу предмета (базовому названию)
    private bool matchAttributes = false;   // Совпадать по атрибутам/вариантам

    // === НАСТРОЙКИ ОСТАНОВКИ ПОРЧИ ===
    private bool stopPerishEnabled = true;  // Останавливать процесс порчи
    private float perishRateMultiplier = 0f; // Множитель скорости порчи (0 = полная остановка)
    private bool stopAllTransitions = false; // Останавливать все типы переходов

    // === Кэширование температуры для оптимизации ===
    private float temperatureCached = -1000f;
    private long lastTemperatureUpdate = 0;

    // === Тайминги для предотвращения спама ===
    private Dictionary<BlockPos, long> lastTransferTime = [];
    private const long MinTransferInterval = 500; // Минимум 500 мс между переносами одного предмета
    private const int MaxSourceChecksPerTick = 16;
    private long lastTransferCleanupTime = 0;
    private readonly List<BlockPos> expiredTransferKeys = [];

    // === Переопределение свойства Inventory ===
    public override InventoryBase Inventory => _inventory;

    // === Геттеры для публичного доступа к настройкам ===
    public int TransferRate => transferRate;
    public FilterMode CurrentFilterMode => currentFilterMode;
    public bool MatchMod => matchMod;
    public bool MatchType => matchType;
    public bool MatchAttributes => matchAttributes;

    // Свойства для остановки порчи
    public bool StopPerishEnabled => stopPerishEnabled;
    public float PerishRateMultiplier => perishRateMultiplier;
    public bool StopAllTransitions => stopAllTransitions;

    // === Конструктор ===
    public BEItemInsertionPipe()
    {
        // 12 слотов для фильтров (6x2 в GUI)
        _inventory = new InventoryInsertionPipe(12, "insertionpipe", null, null, this);
    }

    // === Инициализация ===
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        // Определяем направление вывода (куда будем класть предметы)
        DetermineOutputDirection();

        if (api.Side == EnumAppSide.Server)
        {
            // Регистрируем тик для обработки передачи предметов
            transferTimer = api.World.RegisterGameTickListener(OnTransferTick, 1000);

            // Также регистрируем тик для обработки порчи (каждые 2 секунды)
            perishTimer = api.World.RegisterGameTickListener(OnPerishTick, 2000);
        }

        // Подписываемся на события инвентаря для контроля скорости переходных состояний (порчи)
        _inventory.OnAcquireTransitionSpeed += OnAcquireTransitionSpeed;
    }

    // === Методы для остановки порчи ===

    /// <summary>
    /// Обработчик изменения скорости переходных состояний.
    /// Используется для замедления порчи предметов в пути.
    /// </summary>
    private float OnAcquireTransitionSpeed(EnumTransitionType transType, ItemStack stack, float baseMul)
    {
        // Если отключена остановка порчи или предмет не портится (не perish)
        if (!stopPerishEnabled || transType != EnumTransitionType.Perish)
        {
            // Проверяем, нужно ли останавливать все переходы (например, при выдержке)
            if (stopAllTransitions && ShouldStopTransition(transType))
            {
                return 0f;
            }

            return baseMul;
        }

        // Применяем множитель скорости порчи к базе
        return baseMul * perishRateMultiplier;
    }

    /// <summary>
    /// Проверяет, нужно ли останавливать этот тип перехода.
    /// </summary>
    private static bool ShouldStopTransition(EnumTransitionType transType)
    {
        switch (transType)
        {
            case EnumTransitionType.Perish: // Порча
            case EnumTransitionType.Harden:
            case EnumTransitionType.Melt:
            case EnumTransitionType.None:
            case EnumTransitionType.Burn:
            case EnumTransitionType.Ripen: // Созревание
            case EnumTransitionType.Convert: // Превращение
            case EnumTransitionType.Dry: // Сушка
            case EnumTransitionType.Cure: // Выдержка
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Тик для обработки порчи всех предметов в инвентаре.
    /// </summary>
    private void OnPerishTick(float dt)
    {
        if (Api?.Side != EnumAppSide.Server || !stopPerishEnabled)
            return;

        foreach (var slot in _inventory)
        {
            if (slot.Itemstack != null)
            {
                var before = slot.Itemstack.Clone();
                // Обновляем переходные состояния предмета по температуре мира
                slot.Itemstack.Collectible.UpdateAndGetTransitionStates(Api.World, slot);

                // Если предмет изменился (начал портиться), помечаем слот как грязный для отрисовки
                if (!slot.Itemstack.Equals(Api.World, before))
                {
                    MarkDirty();
                }
            }
        }
    }

    /// <summary>
    /// Методы для управления настройками порчи (вызываются из GUI).
    /// </summary>
    public void SetPerishSettings(bool enabled, float multiplier = 0f, bool stopAll = false)
    {
        stopPerishEnabled = enabled;
        perishRateMultiplier = GameMath.Clamp(multiplier, 0f, 10f);
        stopAllTransitions = stopAll;
        MarkDirty(true);

        //Api?.Logger?.Notification($"=== Настройки порчи обновлены: enabled={enabled}, multiplier={multiplier}, stopAll={stopAll} ===");
    }




    // === Взаимодействие с игроком ===
    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            OpenGui(byPlayer as IClientPlayer);
        }

        return true; // возвращаем результат базового метода (true)
    }



    private void OpenGui(IClientPlayer player)
    {
        if (_clientDialog == null || !_clientDialog.IsOpened())
        {
            _clientDialog = new GuiDialogInsertionPipe(
                Lang.Get("electricalprogressivetransport:filter-pipe-title"),
                Inventory,
                Pos,
                Api as ICoreClientAPI,
                this
            );
            _clientDialog.TryOpen();
            player.InventoryManager.OpenInventory(Inventory);
        }
        else
        {
            _clientDialog?.TryClose();
        }
    }

    /// <summary>
    /// Получает базовый код блока для фильтрующей трубы.
    /// </summary>
    public override string GetBaseBlockCode()
    {
        return "electricalprogressivetransport:pipe-item-insertion";
    }

    // Определяем направление, куда будем выводить предметы
    private void DetermineOutputDirection()
    {
        if (Api == null) return;

        // Ищем контейнер в соседних блоках
        for (int i = 0; i < 6; i++)
        {
            BlockFacing facing = BlockFacing.ALLFACES[i];
            BlockPos checkPos = Pos.AddCopy(facing);

            // Пропускаем позиции с трубами (чтобы не выводить в другую трубу)
            var checkBlock = Api.World.BlockAccessor.GetBlock(checkPos);
            if (checkBlock is BlockPipeBase)
            {
                continue;
            }

            // Используем подход как желоб: ищем контейнер на соседнем блоке
            var container = checkBlock.GetBlockEntity<BlockEntityContainer>(checkPos);

            if (container != null)
            {
                outputFacing = facing;
                return;
            }
        }

        outputFacing = null;
    }

    // Проверяет, проходит ли предмет через фильтр
    public bool CheckItemAgainstFilter(ItemStack itemstack)
    {
        if (itemstack == null || itemstack.Collectible == null)
            return false;

        // Проверяем, есть ли хоть один фильтр в слотах
        bool hasAnyFilters = false;
        for (int i = 0; i < _inventory.Count; i++)
        {
            if (!_inventory[i].Empty)
            {
                hasAnyFilters = true;
                break;
            }
        }

        // Если фильтров нет - пропускаем всё (предмет проходит)
        if (!hasAnyFilters)
        {
            return true;
        }

        bool hasMatchingFilter = false;

        // Проверяем все слоты фильтра
        for (int i = 0; i < _inventory.Count; i++)
        {
            ItemSlot filterSlot = _inventory[i];
            if (!filterSlot.Empty)
            {
                bool matches = CheckItemMatchesFilter(itemstack, filterSlot.Itemstack);
                if (matches)
                {
                    hasMatchingFilter = true;
                    break;
                }
            }
        }

        // Возвращаем результат в зависимости от режима
        return currentFilterMode == FilterMode.AllowList ? hasMatchingFilter : !hasMatchingFilter;
    }

    // Проверяет соответствие предмета фильтру
    private bool CheckItemMatchesFilter(ItemStack item, ItemStack filter)
    {
        if (item == null || filter == null || item.Collectible == null || filter.Collectible == null)
            return false;

        AssetLocation itemCode = item.Collectible.Code;
        AssetLocation filterCode = filter.Collectible.Code;

        // 1. Проверка по мод-идентификатору (домену)
        if (matchMod)
        {
            if (itemCode?.Domain != filterCode?.Domain)
                return false;
        }

        // 2. Проверка по типу
        if (matchType)
        {
            string itemPath = itemCode?.Path ?? "";
            string filterPath = filterCode?.Path ?? "";

            // Сравниваем только первую часть пути (базовый тип предмета)
            string[] itemParts = itemPath.Split('-');
            string[] filterParts = filterPath.Split('-');

            if (itemParts.Length == 0 || filterParts.Length == 0)
                return false;

            if (itemParts[0] != filterParts[0])
                return false;
        }

        // 3. Проверка по атрибутам/вариантам
        if (matchAttributes)
        {
            try
            {
                if (item.Collectible.Variant != null && filter.Collectible.Variant != null)
                {
                    foreach (var key in filter.Collectible.Variant.Keys)
                    {
                        if (item.Collectible.Variant.ContainsKey(key))
                        {
                            string itemValue = item.Collectible.Variant[key]?.ToString();
                            string filterValue = filter.Collectible.Variant[key]?.ToString();

                            if (itemValue != filterValue)
                                return false;
                        }
                        else
                        {
                            // Если в фильтре есть атрибут, которого нет в предмете - не подходит
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Api?.Logger?.Error($"Ошибка при проверке атрибутов: {ex.Message}");
                return false;
            }
        }

        // 4. Если НЕ включена ни одна из настроек сравнения
        bool hasComparisonSetting = matchMod || matchType || matchAttributes;

        if (!hasComparisonSetting)
        {
            // Если не включено ни одной настройки сравнения, 
            // то предмет должен точно совпадать с фильтром (по полному коду)
            if (!itemCode?.Equals(filterCode) ?? false)
                return false;
        }

        return true;
    }

    // Метод обновления настроек фильтра (вызывается из GUI)
    public void UpdateFilterSettings(FilterMode mode, bool matchMod, bool matchType, bool matchAttributes)
    {
        this.currentFilterMode = mode;
        this.matchMod = matchMod;
        this.matchType = matchType;
        this.matchAttributes = matchAttributes;

        MarkDirty();
    }

    // === Логика передачи предметов ===

    private void OnTransferTick(float dt)
    {
        debugCounter++;

        if (debugCounter % 10 == 0)
        {
            //Api.Logger.Notification($"=== OnTransferTick #{debugCounter} на {Pos} ===");
        }

        if (Api == null || NetworkManager == null)
            return;

        // Обновляем направление вывода, если нужно
        if (outputFacing == null || debugCounter % 20 == 0)
        {
            DetermineOutputDirection();
        }

        if (outputFacing == null)
        {
            return;
        }

        // Получаем целевой контейнер
        BlockPos containerPos = Pos.AddCopy(outputFacing);
        var block = Api.World.BlockAccessor.GetBlock(containerPos);

        var targetContainer = block.GetBlockEntity<BlockEntityContainer>(containerPos);

        if (targetContainer == null)
        {
            return;
        }

        // Ищем и переносим предметы
        FindAndTransferItems(targetContainer, containerPos);
    }

    private void FindAndTransferItems(BlockEntityContainer targetContainer, BlockPos targetPos)
    {
        IInventory targetInventory = targetContainer?.Inventory;

        if (targetInventory == null)
            return;

        // Используем сеть для поиска источников предметов в других трубах
        var network = NetworkManager.GetNetwork(Pos);
        if (network == null)
            return;

        // Собираем все позиции для исключения (чтобы не брать предметы из себя и цели)
        var excludePositions = new HashSet<BlockPos>();
        excludePositions.Add(Pos);
        excludePositions.Add(targetPos);

        // Исключаем другие фильтрующие трубы и их цели
        foreach (var inserterPos in network.Inserters)
        {
            if (!inserterPos.Equals(Pos))
            {
                excludePositions.Add(inserterPos);
                var otherPipe = Api.World.BlockAccessor.GetBlockEntity(inserterPos) as BEItemInsertionPipe;
                if (otherPipe != null && otherPipe.outputFacing != null)
                {
                    excludePositions.Add(inserterPos.AddCopy(otherPipe.outputFacing));
                }
            }
        }

        int sourceCount = network.ItemSources.Count;
        if (sourceCount == 0)
            return;

        if (sourceCursor >= sourceCount)
            sourceCursor = 0;

        if (preferredSourcePos != null && !excludePositions.Contains(preferredSourcePos))
        {
            for (int i = 0; i < sourceCount; i++)
            {
                var sourceEndpoint = network.ItemSources[i];
                if (!sourceEndpoint.EndpointPos.Equals(preferredSourcePos))
                    continue;

                if (TryTransferFromSource(preferredSourcePos, sourceEndpoint.FacingFromPipe.Opposite, targetInventory, targetContainer, targetPos))
                {
                    sourceCursor = (i + 1) % sourceCount;
                    return;
                }

                preferredSourcePos = null;
                break;
            }
        }

        int checks = Math.Min(MaxSourceChecksPerTick, sourceCount);

        // Ищем источник предметов по кэшу сети, распределяя большой поиск по нескольким тикам.
        for (int checkedCount = 0; checkedCount < checks; checkedCount++)
        {
            int index = (sourceCursor + checkedCount) % sourceCount;
            var sourceEndpoint = network.ItemSources[index];
            BlockPos checkPos = sourceEndpoint.EndpointPos;
            if (excludePositions.Contains(checkPos)) continue;

            if (TryTransferFromSource(checkPos, sourceEndpoint.FacingFromPipe.Opposite, targetInventory, targetContainer, targetPos))
            {
                preferredSourcePos = checkPos.Copy();
                sourceCursor = (index + 1) % sourceCount;
                return; // Успешно перенесли предмет
            }
        }

        sourceCursor = (sourceCursor + checks) % sourceCount;
    }

    private bool TryTransferFromSource(BlockPos sourcePos, BlockFacing directionFromSource, IInventory targetInventory,
        BlockEntityContainer targetContainer, BlockPos targetPos)
    {
        // Проверяем тайминг (чтобы не спамить сетевые пакеты)
        if (!CanTransferFrom(sourcePos))
            return false;

        // ПОЛУЧАЕМ КОНТЕЙНЕР ИСТОЧНИКА
        var sourceBlock = Api.World.BlockAccessor.GetBlock(sourcePos);
        var sourceContainer = sourceBlock.GetBlockEntity<BlockEntityContainer>(sourcePos);

        if (sourceContainer == null)
            return false;

        // Получаем инвентарь источника
        IInventory sourceInventory = sourceContainer.Inventory;
        if (sourceInventory == null)
            return false;

        // Ищем подходящий слот в источнике
        ItemSlot sourceSlot = FindFirstSuitableSlot(sourceInventory, directionFromSource);

        if (sourceSlot == null || sourceSlot.Empty)
            return false;

        // Не переносим жидкости
        if (sourceSlot.Itemstack.Collectible.IsLiquid())
            return false;

        // Определяем направление к цели
        BlockFacing directionToTarget = GetFacingFromTo(Pos, targetPos);
        if (directionToTarget == null)
            return false;

        // Запрашиваем у цели разрешение на вставку
        ItemSlot targetSlot = null;
        if (targetInventory is InventoryBase targetInventoryBase)
        {
            targetSlot = targetInventoryBase.GetAutoPushIntoSlot(directionToTarget.Opposite, sourceSlot);

            if (targetSlot == null)
            {
                // Если не получили целевой слот через GetAutoPushIntoSlot, ищем подходящий вручную
                targetSlot = FindSuitableTargetSlot(targetInventory, sourceSlot);

                if (targetSlot == null)
                    return false;
            }
        }

        // Проверяем, может ли целевой слот принять предмет
        if (!targetSlot.CanHold(sourceSlot))
            return false;

        // Выполняем перенос
        return ExecuteTransfer(sourceSlot, targetSlot, sourceContainer, targetContainer, sourcePos);
    }

    // Ищет первый подходящий слот в источнике
    private ItemSlot FindFirstSuitableSlot(IInventory inventory, BlockFacing pullDirection)
    {
        for (int i = 0; i < inventory.Count; i++)
        {
            ItemSlot slot = inventory[i];
            if (slot != null && !slot.Empty && CheckItemAgainstFilter(slot.Itemstack))
            {
                return slot;
            }
        }

        return null;
    }

    private ItemSlot FindSuitableTargetSlot(IInventory targetInventory, ItemSlot sourceSlot)
    {
        // 1. Сначала ищем слот с таким же предметом (для сохранения количества)
        for (int i = 0; i < targetInventory.Count; i++)
        {
            ItemSlot targetSlot = targetInventory[i];
            if (targetSlot != null &&
                !targetSlot.Empty &&
                targetSlot.CanHold(sourceSlot) &&
                targetSlot.Itemstack.Equals(Api.World, sourceSlot.Itemstack, GlobalConstants.IgnoredStackAttributes))
            {
                // Проверяем, есть ли свободное место
                int freeSpace = targetSlot.Itemstack.Collectible.MaxStackSize - targetSlot.StackSize;
                if (freeSpace > 0)
                {
                    return targetSlot;
                }
            }
        }

        // 2. Ищем пустой слот, который может принять предмет
        for (int i = 0; i < targetInventory.Count; i++)
        {
            ItemSlot targetSlot = targetInventory[i];
            if (targetSlot != null && targetSlot.Empty && targetSlot.CanHold(sourceSlot))
            {
                return targetSlot;
            }
        }

        return null;
    }

    private bool ExecuteTransfer(ItemSlot sourceSlot, ItemSlot targetSlot, BlockEntity sourceBe,
        BlockEntityContainer targetContainer, BlockPos sourcePos)
    {
        try
        {
            // Создаем операцию переноса
            ItemStackMoveOperation op = new ItemStackMoveOperation(
                Api.World,
                EnumMouseButton.Left,
                0,
                EnumMergePriority.DirectMerge,
                Math.Min(transferRate, sourceSlot.StackSize)
            );

            int transferred = sourceSlot.TryPutInto(targetSlot, ref op);

            if (transferred > 0)
            {
                // Успешно перенесли
                lastTransferTime[sourcePos] = Api.World.ElapsedMilliseconds;
                CleanupTransferTimers();

                // Помечаем слоты как измененные
                sourceSlot.MarkDirty();
                targetSlot.MarkDirty();

                // Помечаем BlockEntity как измененные
                sourceBe.MarkDirty();
                targetContainer.MarkDirty();

                return true;
            }
        }
        catch (Exception ex)
        {
            Api.Logger.Error($"Ошибка при выполнении переноса: {ex.Message}");
        }

        return false;
    }

    private bool CanTransferFrom(BlockPos sourcePos)
    {
        if (!lastTransferTime.TryGetValue(sourcePos, out var value))
            return true;

        long elapsed = Api.World.ElapsedMilliseconds - value;
        return elapsed > MinTransferInterval;
    }

    private void CleanupTransferTimers()
    {
        long now = Api.World.ElapsedMilliseconds;
        if (now - lastTransferCleanupTime < 10000)
            return;

        lastTransferCleanupTime = now;
        expiredTransferKeys.Clear();

        foreach (var entry in lastTransferTime)
        {
            if (now - entry.Value > MinTransferInterval * 20)
                expiredTransferKeys.Add(entry.Key);
        }

        foreach (var key in expiredTransferKeys)
        {
            lastTransferTime.Remove(key);
        }
    }

    private BlockFacing GetFacingFromTo(BlockPos from, BlockPos to)
    {
        int dx = to.X - from.X;
        int dy = to.Y - from.Y;
        int dz = to.Z - from.Z;

        // Простая проверка соседних блоков
        if (dx == 1 && dy == 0 && dz == 0) return BlockFacing.EAST;
        if (dx == -1 && dy == 0 && dz == 0) return BlockFacing.WEST;
        if (dx == 0 && dy == 1 && dz == 0) return BlockFacing.UP;
        if (dx == 0 && dy == -1 && dz == 0) return BlockFacing.DOWN;
        if (dx == 0 && dy == 0 && dz == 1) return BlockFacing.SOUTH;
        if (dx == 0 && dy == 0 && dz == -1) return BlockFacing.NORTH;

        // Если блоки не соседние, попробуем определить основное направление
        if (Math.Abs(dx) > Math.Abs(dy) && Math.Abs(dx) > Math.Abs(dz))
        {
            return dx > 0 ? BlockFacing.EAST : BlockFacing.WEST;
        }
        else if (Math.Abs(dy) > Math.Abs(dx) && Math.Abs(dy) > Math.Abs(dz))
        {
            return dy > 0 ? BlockFacing.UP : BlockFacing.DOWN;
        }
        else if (Math.Abs(dz) > Math.Abs(dx) && Math.Abs(dz) > Math.Abs(dy))
        {
            return dz > 0 ? BlockFacing.SOUTH : BlockFacing.NORTH;
        }

        return null;
    }

    // === Информация о блоке для GUI/подсказок ===
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
    {
        base.GetBlockInfo(forPlayer, sb);

        sb.AppendLine("══════════════════════════════════════════");

        // Информация о режиме фильтра
        string modeText = currentFilterMode switch
        {
            FilterMode.AllowList => Lang.Get("electricalprogressivetransport:filter-mode-allow"),
            FilterMode.DenyList => Lang.Get("electricalprogressivetransport:filter-mode-deny"),
            _ => "Unknown"
        };
        sb.AppendLine(Lang.Get("electricalprogressivetransport:filter-mode", modeText));

        // Информация о настройках сравнения
        List<string> filters = [];
        if (matchMod) filters.Add(Lang.Get("electricalprogressivetransport:filter-match-mod"));
        if (matchType) filters.Add(Lang.Get("electricalprogressivetransport:filter-match-type"));
        if (matchAttributes) filters.Add(Lang.Get("electricalprogressivetransport:filter-match-attrs"));

        if (filters.Count > 0)
        {
            sb.AppendLine("└ " + string.Join(", ", filters));
        }

        sb.AppendLine(Lang.Get("electricalprogressivetransport:transfer-rate", transferRate));

        // Информация о фильтрах
        int activeFilters = 0;
        for (int i = 0; i < _inventory.Count; i++)
        {
            if (!_inventory[i].Empty) activeFilters++;
        }

        sb.AppendLine(Lang.Get("electricalprogressivetransport:active-filters", activeFilters, _inventory.Count));

        // Информация о настройках порчи
        sb.AppendLine("══════════════════════════════════════════");
        sb.AppendLine(Lang.Get("electricalprogressivetransport:preservation-settings"));

        if (stopPerishEnabled)
        {
            if (perishRateMultiplier <= 0.001f)
            {
                sb.AppendLine(Lang.Get("electricalprogressivetransport:perish-fully-stopped"));

            }
            else
            {
                sb.AppendLine(Lang.Get("electricalprogressivetransport:perish-slowed-factor", Math.Round(1f / perishRateMultiplier, 1)));
            }

            if (stopAllTransitions)
            {
                sb.AppendLine(Lang.Get("electricalprogressivetransport:all-transitions-stopped"));
            }
        }
        else
        {
            sb.AppendLine(Lang.Get("electricalprogressivetransport:preservation-disabled"));
        }
    }

    // === Жизненный цикл блока ===
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        if (Api?.Side == EnumAppSide.Server)
        {
            Api.World.UnregisterGameTickListener(transferTimer);
            Api.World.UnregisterGameTickListener(perishTimer);
        }

        if (_clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog?.TryClose();
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        // Загружаем настройки фильтра
        transferRate = tree.GetInt("transferRate", 1);
        currentFilterMode = (FilterMode)tree.GetInt("filterMode", 0);
        matchMod = tree.GetBool("matchMod", false);
        matchType = tree.GetBool("matchType", true);
        matchAttributes = tree.GetBool("matchAttributes", false);

        // Загружаем настройки порчи
        stopPerishEnabled = tree.GetBool("stopPerishEnabled", true);
        perishRateMultiplier = tree.GetFloat("perishRateMultiplier", 0f);
        stopAllTransitions = tree.GetBool("stopAllTransitions", false);

        // Ограничиваем значение скорости
        transferRate = System.Math.Max(1, System.Math.Min(transferRate, 64));
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        // Сохраняем настройки фильтра
        tree.SetInt("transferRate", transferRate);
        tree.SetInt("filterMode", (int)currentFilterMode);
        tree.SetBool("matchMod", matchMod);
        tree.SetBool("matchType", matchType);
        tree.SetBool("matchAttributes", matchAttributes);

        // Сохраняем настройки порчи
        tree.SetBool("stopPerishEnabled", stopPerishEnabled);
        tree.SetFloat("perishRateMultiplier", perishRateMultiplier);
        tree.SetBool("stopAllTransitions", stopAllTransitions);
    }

    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(player, packetid, data);

        // Обработка пакетов от GUI
        if (packetid == 1001) // Закрытие GUI
        {
            if (_clientDialog != null && _clientDialog.IsOpened())
            {
                _clientDialog?.TryClose();
            }
        }
        else if (packetid == 1002) // Обновление настроек фильтра
        {
            using var ms = new System.IO.MemoryStream(data);
            using var br = new System.IO.BinaryReader(ms);
            FilterMode mode = (FilterMode)br.ReadInt32();
            bool modMatch = br.ReadBoolean();
            bool typeMatch = br.ReadBoolean();
            bool attrMatch = br.ReadBoolean();

            UpdateFilterSettings(mode, modMatch, typeMatch, attrMatch);
        }
        else if (packetid == 1003) // Обновление скорости передачи
        {
            try
            {
                var tree = new TreeAttribute();
                tree.FromBytes(data);

                int newRate = tree.GetInt("transferRate", 1);

                // Ограничиваем значение
                newRate = System.Math.Max(1, System.Math.Min(newRate, 64));

                if (newRate != transferRate)
                {
                    transferRate = newRate;

                    MarkDirty();
                    Api.World.BlockAccessor.MarkBlockDirty(Pos);
                }
            }
            catch (Exception ex)
            {
                Api?.Logger?.Error($"Ошибка при обновлении скорости передачи: {ex.Message}");
            }
        }
        else if (packetid == 1004) // Обновление настроек порчи
        {
            try
            {
                var tree = new TreeAttribute();
                tree.FromBytes(data);

                bool enabled = tree.GetBool("stopPerishEnabled", true);
                float multiplier = tree.GetFloat("perishRateMultiplier", 0f);
                bool stopAll = tree.GetBool("stopAllTransitions", false);

                SetPerishSettings(enabled, multiplier, stopAll);

                MarkDirty();
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
            }
            catch (Exception ex)
            {
                Api?.Logger?.Error($"Ошибка при обновлении настроек порчи: {ex.Message}");
            }
        }
    }

    public override void OnBlockBroken(IPlayer byPlayer = null)
    {
        // Не вызываем базовый метод, чтобы не дропались предметы
        if (Inventory != null)
        {
            Inventory?.Clear();
        }

        OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        if (_clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog?.TryClose();
        }
    }
}
