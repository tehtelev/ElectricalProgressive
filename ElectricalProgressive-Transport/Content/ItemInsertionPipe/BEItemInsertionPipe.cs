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

public class BEItemInsertionPipe : BlockEntityPipeBase
{
    private long transferTimer;
    private int transferRate = 1;
    private BlockFacing outputFacing = null; // Направление вывода
    private int debugCounter = 0;

    // Собственный инвентарь (фильтры)
    internal InventoryInsertionPipe _inventory;
    private GuiDialogInsertionPipe _clientDialog;

    // Режимы работы фильтра
    public enum FilterMode
    {
        AllowList = 0, // Разрешать только указанные предметы
        DenyList = 1, // Запрещать указанные предметы
    }

    private FilterMode currentFilterMode = FilterMode.AllowList;
    private bool matchMod = false; // Совпадать по мод-идентификатору
    private bool matchType = true; // Совпадать по типу предмета
    private bool matchAttributes = false; // Совпадать по атрибутам

    // НАСТРОЙКИ ОСТАНОВКИ ПОРЧИ
    private bool stopPerishEnabled = true; // Останавливать порчу
    private float perishRateMultiplier = 0f; // Множитель скорости порчи (0 = полная остановка)
    private bool stopAllTransitions = false; // Останавливать все типы переходов

    // Кэширование температуры для оптимизации
    private float temperatureCached = -1000f;
    private long lastTemperatureUpdate = 0;

    // Тайминги для предотвращения спама
    private Dictionary<BlockPos, long> lastTransferTime = new();
    private const long MinTransferInterval = 500; // 500 мс между переносами

    // Переопределяем свойство Inventory для фильтрующей трубы
    public override InventoryBase Inventory => _inventory;

    public int TransferRate => transferRate;
    public FilterMode CurrentFilterMode => currentFilterMode;
    public bool MatchMod => matchMod;
    public bool MatchType => matchType;
    public bool MatchAttributes => matchAttributes;

    // Свойства для остановки порчи
    public bool StopPerishEnabled => stopPerishEnabled;
    public float PerishRateMultiplier => perishRateMultiplier;
    public bool StopAllTransitions => stopAllTransitions;

    public BEItemInsertionPipe()
    {
        // 12 слотов для фильтров (6x2 в GUI)
        _inventory = new InventoryInsertionPipe(12, "insertionpipe", null, null, this);
    }

    public override void Initialize(ICoreAPI api)
    {
        // Сначала вызываем базовую инициализацию
        base.Initialize(api);

        api.Logger.Notification($"=== Фильтрующая труба Initialize на {Pos} ===");

        // Определяем направление вывода
        DetermineOutputDirection();

        if (api.Side == EnumAppSide.Server)
        {
            api.Logger.Notification($"=== Регистрируем таймер на {Pos} ===");
            transferTimer = api.World.RegisterGameTickListener(OnTransferTick, 1000);

            // Также регистрируем тик для обработки порчи
            api.World.RegisterGameTickListener(OnPerishTick, 2000); // Каждые 2 секунды
        }

        // Подписываемся на события инвентаря для контроля скорости порчи
        _inventory.OnAcquireTransitionSpeed += OnAcquireTransitionSpeed;
    }

    #region Методы для остановки порчи

    // Обработчик скорости переходных состояний
    private float OnAcquireTransitionSpeed(EnumTransitionType transType, ItemStack stack, float baseMul)
    {
        // Если отключена остановка порчи или предмет не портится
        if (!stopPerishEnabled || transType != EnumTransitionType.Perish)
        {
            // Проверяем, нужно ли останавливать все переходы
            if (stopAllTransitions && ShouldStopTransition(transType))
            {
                return 0f;
            }

            return baseMul;
        }

        // Применяем множитель скорости порчи
        return baseMul * perishRateMultiplier;
    }

    // Проверяет, нужно ли останавливать этот тип перехода
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

    // Тик для обработки порчи
    private void OnPerishTick(float dt)
    {
        if (Api?.Side != EnumAppSide.Server || !stopPerishEnabled)
            return;

        // Обновляем состояние всех предметов в инвентаре
        foreach (var slot in _inventory)
        {
            if (slot.Itemstack != null)
            {
                var before = slot.Itemstack.Clone();
                slot.Itemstack.Collectible.UpdateAndGetTransitionStates(Api.World, slot);

                // Если предмет изменился, помечаем как грязный
                if (!slot.Itemstack.Equals(Api.World, before))
                {
                    MarkDirty();
                }
            }
        }
    }

    // Методы для управления настройками порчи
    public void SetPerishSettings(bool enabled, float multiplier = 0f, bool stopAll = false)
    {
        stopPerishEnabled = enabled;
        perishRateMultiplier = GameMath.Clamp(multiplier, 0f, 10f);
        stopAllTransitions = stopAll;
        MarkDirty(true);

        Api?.Logger?.Notification(
            $"=== Настройки порчи обновлены: enabled={enabled}, multiplier={multiplier}, stopAll={stopAll} ===");
    }

    // Расчет скорости порчи
    /*
    public float GetPerishRate()
    {
        if (!stopPerishEnabled || Api == null)
            return 1f;

        // Если множитель установлен в 0 - полная остановка порчи
        if (perishRateMultiplier <= 0.001f)
            return 0f;

        // Используем кэширование температуры для оптимизации
        long currentTime = Api.World.ElapsedMilliseconds;
        if (currentTime - lastTemperatureUpdate > 10000 || temperatureCached < -999f)
        {
            var sealevelpos = Pos.Copy();
            sealevelpos.Y = Api.World.SeaLevel;

            temperatureCached = Api.World.BlockAccessor.GetClimateAt(
                sealevelpos,
                EnumGetClimateMode.ForSuppliedDate_TemperatureOnly,
                Api.World.Calendar.TotalDays
            ).Temperature;

            lastTemperatureUpdate = currentTime;
        }

        // Применяем наш множитель к базовой скорости
        float baseRate = Math.Max(0.1f, Math.Min(2.4f, (float)Math.Pow(3, temperatureCached / 19 - 1.2) - 0.1f));
        return baseRate * perishRateMultiplier;
    }
    */
    #endregion

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            OpenGui(byPlayer as IClientPlayer);
        }

        return true;
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
            _clientDialog.TryClose();
        }
    }

    /// <summary>
    /// Получает базовый код блока для фильтрующей трубы
    /// </summary>
    protected override string GetBaseBlockCode()
    {
        return "electricalprogressivetransport:pipe-item-insertion";
    }

    // Определяем направление, куда будем выводить предметы
    private void DetermineOutputDirection()
    {
        if (Api == null) return;

        if (debugCounter % 10 == 0)
            Api.Logger.Notification($"=== Определяем вывод для {Pos} ===");

        // Ищем контейнер в соседних блоках
        for (int i = 0; i < 6; i++)
        {
            BlockFacing facing = BlockFacing.ALLFACES[i];
            BlockPos checkPos = Pos.AddCopy(facing);

            // Пропускаем позиции с трубами
            Vintagestory.API.Common.Block checkBlock = Api.World.BlockAccessor.GetBlock(checkPos);
            if (checkBlock is BlockPipeBase)
            {
                continue;
            }

            // Используем подход как желоб
            BlockEntityContainer container = checkBlock.GetBlockEntity<BlockEntityContainer>(checkPos);

            if (container != null)
            {
                outputFacing = facing;
                if (debugCounter % 5 == 0)
                {
                    Api.Logger.Notification($"=== НАЙДЕН КОНТЕЙНЕР! Направление: {facing.Code} на {checkPos} ===");
                    Api.Logger.Notification($"=== Тип контейнера: {container.GetType().Name} ===");
                }

                return;
            }
        }

        if (debugCounter % 10 == 0)
            Api.Logger.Notification($"=== Контейнер не найден для {Pos} ===");
        outputFacing = null;
    }

    // Проверяет, проходит ли предмет через фильтр
    public bool CheckItemAgainstFilter(ItemStack itemstack)
    {
        if (itemstack == null || itemstack.Collectible == null)
            return false;

        // Проверяем, есть ли хоть один фильтр
        bool hasAnyFilters = false;
        for (int i = 0; i < _inventory.Count; i++)
        {
            if (!_inventory[i].Empty)
            {
                hasAnyFilters = true;
                break;
            }
        }

        // Если фильтров нет - пропускаем всё
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

        Api?.Logger?.Debug($"Проверка: {itemCode} против {filterCode}");
        Api?.Logger?.Debug($"Настройки: Mod={matchMod}, Type={matchType}, Attrs={matchAttributes}");

        // 1. Проверка по мод-идентификатору (домену)
        if (matchMod)
        {
            if (itemCode?.Domain != filterCode?.Domain)
            {
                Api?.Logger?.Debug($"Не совпадает мод: {itemCode?.Domain} != {filterCode?.Domain}");
                return false;
            }
        }

        // 2. Проверка по типу
        if (matchType)
        {
            // Разбиваем путь на части
            string itemPath = itemCode?.Path ?? "";
            string filterPath = filterCode?.Path ?? "";

            // Сравниваем только первую часть пути
            string[] itemParts = itemPath.Split('-');
            string[] filterParts = filterPath.Split('-');

            if (itemParts.Length == 0 || filterParts.Length == 0)
                return false;

            // Сравниваем базовую часть
            if (itemParts[0] != filterParts[0])
            {
                Api?.Logger?.Debug($"Не совпадает тип: {itemParts[0]} != {filterParts[0]}");
                return false;
            }
        }

        // 3. Проверка по атрибутам/вариантам
        if (matchAttributes)
        {
            try
            {
                // Сравниваем варианты
                if (item.Collectible.Variant != null && filter.Collectible.Variant != null)
                {
                    foreach (var key in filter.Collectible.Variant.Keys)
                    {
                        if (item.Collectible.Variant.ContainsKey(key))
                        {
                            string itemValue = item.Collectible.Variant[key]?.ToString();
                            string filterValue = filter.Collectible.Variant[key]?.ToString();

                            if (itemValue != filterValue)
                            {
                                Api?.Logger?.Debug($"Не совпадает атрибут {key}: {itemValue} != {filterValue}");
                                return false;
                            }
                        }
                        else
                        {
                            // Если в фильтре есть атрибут, которого нет в предмете
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
            // то предмет должен точно совпадать с фильтром
            if (!itemCode?.Equals(filterCode) ?? false)
            {
                Api?.Logger?.Debug($"Нет настроек сравнения, коды не совпадают");
                return false;
            }
        }

        Api?.Logger?.Debug($"Предмет прошел фильтр!");
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

    private void OnTransferTick(float dt)
    {
        debugCounter++;
        if (debugCounter % 10 == 0)
        {
            Api.Logger.Notification($"=== OnTransferTick #{debugCounter} на {Pos} ===");
        }

        if (Api == null || networkManager == null) return;

        // Обновляем направление вывода, если нужно
        if (outputFacing == null || debugCounter % 20 == 0)
        {
            DetermineOutputDirection();
        }

        if (outputFacing == null)
        {
            if (debugCounter % 20 == 0)
                Api.Logger.Notification($"=== Нет вывода на {Pos} ===");
            return;
        }

        // Получаем целевой контейнер
        BlockPos containerPos = Pos.AddCopy(outputFacing);
        Vintagestory.API.Common.Block block = Api.World.BlockAccessor.GetBlock(containerPos);

        // Используем подход как желоб
        BlockEntityContainer targetContainer = block.GetBlockEntity<BlockEntityContainer>(containerPos);

        if (targetContainer == null)
        {
            if (debugCounter % 20 == 0)
                Api.Logger.Error($"=== BlockEntityContainer не найден на {containerPos} ===");
            return;
        }

        if (debugCounter % 5 == 0)
            Api.Logger.Notification($"=== Целевой контейнер: {targetContainer.GetType().Name} на {containerPos} ===");

        // Ищем и переносим предметы
        FindAndTransferItems(targetContainer, containerPos);
    }

    private void FindAndTransferItems(BlockEntityContainer targetContainer, BlockPos targetPos)
    {
        IInventory targetInventory = targetContainer?.Inventory;

        if (targetInventory == null)
        {
            if (debugCounter % 20 == 0)
                Api.Logger.Error($"=== Не удалось получить инвентарь из контейнера на {targetPos} ===");
            return;
        }

        if (debugCounter % 10 == 0)
            Api.Logger.Notification($"=== Получен инвентарь цели. Количество слотов: {targetInventory.Count} ===");

        // Используем сеть для поиска источников
        var network = networkManager.GetNetwork(Pos);
        if (network == null)
        {
            if (debugCounter % 20 == 0)
                Api.Logger.Notification($"=== Нет сети для трубы на {Pos} ===");
            return;
        }

        if (debugCounter % 20 == 0)
            Api.Logger.Notification(
                $"=== Размер сети: {network.Pipes.Count} труб, {network.Inserters.Count} инсертеров ===");

        // Собираем все позиции для исключения
        var excludePositions = new HashSet<BlockPos>();
        excludePositions.Add(Pos);
        excludePositions.Add(targetPos);

        // Исключаем другие фильтрующие трубы и их цели
        foreach (var inserterPos in network.Inserters)
        {
            if (!inserterPos.Equals(Pos))
            {
                excludePositions.Add(inserterPos);
                BEItemInsertionPipe otherPipe = Api.World.BlockAccessor.GetBlockEntity(inserterPos) as BEItemInsertionPipe;
                if (otherPipe != null && otherPipe.outputFacing != null)
                {
                    excludePositions.Add(inserterPos.AddCopy(otherPipe.outputFacing));
                }
            }
        }

        // Ищем источник предметов в сети
        bool foundSource = false;
        foreach (var pipePos in network.Pipes)
        {
            if (excludePositions.Contains(pipePos)) continue;

            // Проверяем все стороны трубы
            for (int i = 0; i < 6; i++)
            {
                BlockFacing facing = BlockFacing.ALLFACES[i];
                BlockPos checkPos = pipePos.AddCopy(facing);

                if (excludePositions.Contains(checkPos)) continue;

                // Проверяем, можно ли взять предмет из этого источника
                if (TryTransferFromSource(checkPos, targetInventory, targetContainer, targetPos))
                {
                    if (debugCounter % 5 == 0)
                        Api.Logger.Notification($"=== Успешно нашли и перенесли предмет из {checkPos} ===");
                    return; // Успешно перенесли предмет
                }
                else
                {
                    foundSource = true; // Нашли источник, но не смогли взять предмет
                }
            }
        }

        if (!foundSource && debugCounter % 10 == 0)
            Api.Logger.Notification($"=== Не найдено подходящих источников в сети ===");
    }

    private bool TryTransferFromSource(BlockPos sourcePos, IInventory targetInventory,
        BlockEntityContainer targetContainer, BlockPos targetPos)
    {
        // Проверяем тайминг
        if (!CanTransferFrom(sourcePos))
        {
            if (debugCounter % 30 == 0)
                Api.Logger.Notification($"=== Слишком рано для переноса из {sourcePos} ===");
            return false;
        }

        // ПОЛУЧАЕМ КОНТЕЙНЕР ИСТОЧНИКА
        Vintagestory.API.Common.Block sourceBlock = Api.World.BlockAccessor.GetBlock(sourcePos);
        BlockEntityContainer sourceContainer = sourceBlock.GetBlockEntity<BlockEntityContainer>(sourcePos);

        if (sourceContainer == null)
        {
            if (debugCounter % 30 == 0)
                Api.Logger.Notification($"=== Источник не найден на {sourcePos} ===");
            return false;
        }

        if (debugCounter % 20 == 0)
            Api.Logger.Notification($"=== Проверяем источник: {sourceContainer.GetType().Name} на {sourcePos} ===");

        // Получаем инвентарь источника
        IInventory sourceInventory = sourceContainer.Inventory;
        if (sourceInventory == null)
        {
            if (debugCounter % 20 == 0)
                Api.Logger.Notification(
                    $"=== Не удалось получить инвентарь из источника {sourceContainer.GetType().Name} ===");
            return false;
        }

        if (debugCounter % 20 == 0)
            Api.Logger.Notification($"=== Инвентарь источника получен. Слотов: {sourceInventory.Count} ===");

        // Определяем направление от источника к трубе
        BlockFacing directionFromSource = GetFacingFromTo(sourcePos, Pos);
        if (directionFromSource == null)
        {
            if (debugCounter % 20 == 0)
                Api.Logger.Notification($"=== Не удалось определить направление от источника ===");
            return false;
        }

        // Ищем подходящий слот
        ItemSlot sourceSlot = FindFirstSuitableSlot(sourceInventory, directionFromSource.Opposite);

        if (sourceSlot == null || sourceSlot.Empty)
        {
            if (debugCounter % 30 == 0)
                Api.Logger.Notification($"=== В источнике нет подходящих предметов ===");
            return false;
        }

        if (sourceSlot.Itemstack.Collectible.IsLiquid())
        {
            if (debugCounter % 10 == 0)
                Api.Logger.Notification($"=== Это жидкость не переносим: {sourceSlot.Itemstack.Collectible.Code} ===");
            return false;
        }

        // Определяем направление к цели
        BlockFacing directionToTarget = GetFacingFromTo(Pos, targetPos);
        if (directionToTarget == null)
        {
            if (debugCounter % 20 == 0)
                Api.Logger.Notification($"=== Не удалось определить направление к цели ===");
            return false;
        }

        // Запрашиваем у цели разрешение на вставку
        ItemSlot targetSlot = null;
        if (targetInventory is InventoryBase targetInventoryBase)
        {
            if (debugCounter % 20 == 0)
                Api.Logger.Notification($"=== Запрашиваем GetAutoPushIntoSlot у цели ===");

            targetSlot = targetInventoryBase.GetAutoPushIntoSlot(directionToTarget.Opposite, sourceSlot);

            if (targetSlot != null)
            {
                if (debugCounter % 10 == 0)
                    Api.Logger.Notification($"=== GetAutoPushIntoSlot вернул целевой слот ===");
            }
        }

        // Если не получили целевой слот через GetAutoPushIntoSlot, ищем подходящий
        if (targetSlot == null)
        {
            if (debugCounter % 20 == 0)
                Api.Logger.Notification($"=== Ищем подходящий слот в цели вручную ===");

            targetSlot = FindSuitableTargetSlot(targetInventory, sourceSlot);

            if (targetSlot == null)
            {
                if (debugCounter % 10 == 0)
                    Api.Logger.Notification($"=== Не найден подходящий слот в цели ===");
                return false;
            }
        }

        // Проверяем, может ли целевой слот принять предмет
        if (!targetSlot.CanHold(sourceSlot))
        {
            if (debugCounter % 10 == 0)
                Api.Logger.Notification($"=== Целевой слот не может принять предмет ===");
            return false;
        }

        // Выполняем перенос
        return ExecuteTransfer(sourceSlot, targetSlot, sourceContainer, targetContainer, sourcePos);
    }

    // Ищет первый подходящий слот в источнике
    private ItemSlot FindFirstSuitableSlot(IInventory inventory, BlockFacing pullDirection)
    {
        // ищем по всем слотам вручную
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
        // 1. Сначала ищем слот с таким же предметом
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

            if (debugCounter % 5 == 0)
                Api.Logger.Notification(
                    $"=== Пытаемся перенести {Math.Min(transferRate, sourceSlot.StackSize)} предметов ===");

            // Используем TryPutInto
            int transferred = sourceSlot.TryPutInto(targetSlot, ref op);

            if (transferred > 0)
            {
                // Успешно перенесли
                lastTransferTime[sourcePos] = Api.World.ElapsedMilliseconds;

                // Помечаем слоты как измененные
                sourceSlot.MarkDirty();
                targetSlot.MarkDirty();

                // Помечаем BlockEntity как измененные
                sourceBe.MarkDirty();
                targetContainer.MarkDirty();

                if (debugCounter % 2 == 0)
                    Api.Logger.Notification($"=== Успешно перенесено {transferred} предметов через TryPutInto ===");

                return true;
            }
            else
            {
                if (debugCounter % 10 == 0)
                    Api.Logger.Notification($"=== TryPutInto не смог перенести предметы (transferred = 0) ===");
            }
        }
        catch (Exception ex)
        {
            Api.Logger.Error($"Ошибка при выполнении переноса: {ex.Message}");
            Api.Logger.Error($"Stack trace: {ex.StackTrace}");
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

        if (debugCounter % 10 == 0)
            Api.Logger.Notification(
                $"=== Не удалось определить направление от {from} к {to} (dx={dx}, dy={dy}, dz={dz}) ===");

        return null;
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
    {
        sb.AppendLine(Lang.Get("electricalprogressivetransport:pipe-insertion-info"));

        // Вызываем базовый метод для информации о соединениях
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
        sb.AppendLine(Lang.Get("Настройки сохранения продуктов:"));

        if (stopPerishEnabled)
        {
            if (perishRateMultiplier <= 0.001f)
            {
                sb.AppendLine(Lang.Get("• Порча: полностью остановлена"));
            }
            else
            {
                sb.AppendLine(Lang.Get("• Порча: замедлена в {0} раз", Math.Round(1f / perishRateMultiplier, 1)));
            }

            if (stopAllTransitions)
            {
                sb.AppendLine(Lang.Get("• Все переходы: остановлены"));
            }
        }
        else
        {
            sb.AppendLine(Lang.Get("• Сохранение продуктов: отключено"));
        }
    }

    public override void OnBlockRemoved()
    {
        // Сначала обрабатываем разрыв соединений через базовый класс
        base.OnBlockRemoved();

        // Затем специфичную логику для фильтрующей трубы
        if (Api?.Side == EnumAppSide.Server)
        {
            Api.World.UnregisterGameTickListener(transferTimer);
        }

        // Закрываем GUI если открыт
        if (_clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog.TryClose();
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
                _clientDialog.TryClose();
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

                    Api.Logger.Notification($"=== Скорость передачи обновлена: {transferRate} на {Pos} ===");

                    // Помечаем как измененное для сохранения
                    MarkDirty();

                    // Отправляем обновление клиентам
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

                // Отправляем обновление другим клиентам
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
        // base.OnBlockBroken(byPlayer);
        
        // Только чистим инвентарь
        if (Inventory != null)
        {
            Inventory.Clear();
        }
        
        // Вызываем свой базовый код без дропа
        OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        if (_clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog.TryClose();
        }
    }
}