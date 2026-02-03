using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressiveTransport.LiquidInsertionPipe;

public class BELiquidInsertionPipe : BlockEntityPipeBase
{
    private long transferTimer;
    private int transferRate = 100; // В миллилитрах
    private BlockFacing outputFacing = null;
    private int debugCounter = 0;

    // Собственный инвентарь (фильтры для жидкостей)
    internal InventoryLiquidInsertionPipe _inventory;
    private GuiDialogLiquidInsertionPipe _clientDialog;

    // Режимы работы фильтра
    public enum FilterMode
    {
        AllowList = 0,
        DenyList = 1,
    }

    private FilterMode currentFilterMode = FilterMode.AllowList;

    // НАСТРОЙКИ ОСТАНОВКИ ПОРЧИ
    private bool stopPerishEnabled = true;
    private float perishRateMultiplier = 0f;
    private bool stopAllTransitions = false;

    // Кэширование температуры
    private float temperatureCached = -1000f;
    private long lastTemperatureUpdate = 0;

    // Тайминги
    private Dictionary<BlockPos, long> lastTransferTime = new Dictionary<BlockPos, long>();
    private const long MinTransferInterval = 100;

    // Переопределяем свойство Inventory
    public override InventoryBase Inventory => _inventory;
    public override string InventoryClassName => "liquidinsertionpipe";

    public int TransferRate => transferRate;
    public FilterMode CurrentFilterMode => currentFilterMode;

    // Свойства для остановки порчи
    public bool StopPerishEnabled => stopPerishEnabled;
    public float PerishRateMultiplier => perishRateMultiplier;
    public bool StopAllTransitions => stopAllTransitions;

    public BELiquidInsertionPipe()
    {
        _inventory = new InventoryLiquidInsertionPipe(6, InventoryClassName, null, null, this);
    }

    public override void Initialize(ICoreAPI api)
    {
        // Сначала вызываем базовую инициализацию
        base.Initialize(api);

        api.Logger.Notification($"=== Жидкостная труба Initialize на {Pos} ===");

        DetermineOutputDirection();

        if (api.Side == EnumAppSide.Server)
        {
            transferTimer = api.World.RegisterGameTickListener(OnTransferTick, 200);

            // Также регистрируем тик для обработки порчи
            api.World.RegisterGameTickListener(OnPerishTick, 2000);
        }

        // Подписываемся на события инвентаря для контроля скорости порчи
        _inventory.OnAcquireTransitionSpeed += OnAcquireTransitionSpeed;
    }

    /// <summary>
    /// Получает базовый код блока для жидкостной трубы
    /// </summary>
    protected override string GetBaseBlockCode()
    {
        return "electricalprogressivetransport:pipe-liquid-insertion";
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
    private bool ShouldStopTransition(EnumTransitionType transType)
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
            _clientDialog = new GuiDialogLiquidInsertionPipe(
                Lang.Get("electricalprogressivetransport:liquid-filter-pipe-title"),
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

    // Универсальный метод для получения реальной позиции (с учетом мультиблоков)
    private BlockPos GetRealPosition(BlockPos pos)
    {
        Block block = Api.World.BlockAccessor.GetBlock(pos);

        if (block is BlockMultiblock multiblock)
        {
            BlockPos controlPos = multiblock.GetControlBlockPos(pos);

            if (debugCounter % 20 == 0)
            {
                Api.Logger.Notification($"=== Мультиблок {pos} -> контрольная позиция: {controlPos} ===");
            }

            // Рекурсивно (на случай вложенных мультиблоков)
            return GetRealPosition(controlPos);
        }

        return pos;
    }

    // Метод для получения ILiquidSink из позиции (с учетом мультиблоков)
    private ILiquidSink GetLiquidSinkAtPosition(BlockPos pos)
    {
        Block block = Api.World.BlockAccessor.GetBlock(pos);

        // 1. Проверяем сам блок
        if (block is ILiquidSink sink)
        {
            if (debugCounter % 20 == 0)
                Api.Logger.Notification($"=== Блок имеет ILiquidSink: {block.GetType().Name} ===");
            return sink;
        }

        // 2. Если это мультиблок, проверяем контрольный блок
        if (block is BlockMultiblock multiblock)
        {
            BlockPos controlPos = multiblock.GetControlBlockPos(pos);
            Block controlBlock = Api.World.BlockAccessor.GetBlock(controlPos);

            if (controlBlock is ILiquidSink controlSink)
            {
                if (debugCounter % 10 == 0)
                    Api.Logger.Notification(
                        $"=== Мультиблок -> контрольный блок имеет ILiquidSink: {controlBlock.GetType().Name} ===");
                return controlSink;
            }
            else if (debugCounter % 20 == 0)
            {
                Api.Logger.Notification(
                    $"=== Контрольный блок {controlBlock?.GetType().Name} НЕ имеет ILiquidSink ===");
            }
        }

        return null;
    }

    // Метод для получения ILiquidSource из позиции (с учетом мультиблоков)
    private ILiquidSource GetLiquidSourceAtPosition(BlockPos pos)
    {
        Block block = Api.World.BlockAccessor.GetBlock(pos);

        // 1. Проверяем сам блок
        if (block is ILiquidSource source)
        {
            if (debugCounter % 20 == 0)
                Api.Logger.Notification($"=== Блок имеет ILiquidSource: {block.GetType().Name} ===");
            return source;
        }

        // 2. Если это мультиблок, проверяем контрольный блок
        if (block is BlockMultiblock multiblock)
        {
            BlockPos controlPos = multiblock.GetControlBlockPos(pos);
            Block controlBlock = Api.World.BlockAccessor.GetBlock(controlPos);

            if (controlBlock is ILiquidSource controlSource)
            {
                if (debugCounter % 10 == 0)
                    Api.Logger.Notification(
                        $"=== Мультиблок -> контрольный блок имеет ILiquidSource: {controlBlock.GetType().Name} ===");
                return controlSource;
            }
            else if (debugCounter % 20 == 0)
            {
                Api.Logger.Notification(
                    $"=== Контрольный блок {controlBlock?.GetType().Name} НЕ имеет ILiquidSource ===");
            }
        }

        return null;
    }

    private void DetermineOutputDirection()
    {
        if (Api == null) return;

        if (debugCounter % 20 == 0)
            Api.Logger.Notification($"=== DetermineOutputDirection для {Pos} ===");

        for (int i = 0; i < 6; i++)
        {
            BlockFacing facing = BlockFacing.ALLFACES[i];
            BlockPos checkPos = Pos.AddCopy(facing);

            // Пропускаем трубы
            Block checkBlock = Api.World.BlockAccessor.GetBlock(checkPos);
            if (checkBlock is BlockPipeBase)
            {
                continue;
            }

            // Ищем ILiquidSink (с учетом мультиблоков)
            ILiquidSink liquidSink = GetLiquidSinkAtPosition(checkPos);

            if (liquidSink != null)
            {
                outputFacing = facing;

                if (debugCounter % 10 == 0)
                {
                    Api.Logger.Notification($"=== НАЙДЕН ILiquidSink! ===");
                    Api.Logger.Notification($"=== Направление: {facing.Code} -> {checkPos} ===");
                    Api.Logger.Notification($"=== Блок: {checkBlock.GetType().Name} ===");

                    // Показываем реальную позицию для мультиблоков
                    if (checkBlock is BlockMultiblock)
                    {
                        BlockPos realPos = GetRealPosition(checkPos);
                        Api.Logger.Notification($"=== Реальная позиция: {realPos} ===");
                    }
                }

                return;
            }
        }

        outputFacing = null;
        if (debugCounter % 20 == 0)
            Api.Logger.Notification($"=== ILiquidSink не найден в соседних блоках ===");
    }

    private void OnTransferTick(float dt)
    {
        debugCounter++;

        if (Api == null || networkManager == null) return;

        if (debugCounter % 10 == 0)
            Api.Logger.Notification($"=== OnTransferTick #{debugCounter} на позиции {Pos} ===");

        // Обновляем направление вывода
        if (outputFacing == null || debugCounter % 20 == 0)
        {
            DetermineOutputDirection();
        }

        if (outputFacing == null)
        {
            if (debugCounter % 20 == 0)
                Api.Logger.Notification($"=== outputFacing is null, пропускаем тик ===");
            return;
        }

        // Получаем целевой контейнер
        BlockPos targetPos = Pos.AddCopy(outputFacing);

        if (debugCounter % 10 == 0)
            Api.Logger.Notification($"=== Целевая позиция: {targetPos} ===");

        // Ищем ILiquidSink (с учетом мультиблоков)
        ILiquidSink sink = GetLiquidSinkAtPosition(targetPos);

        if (sink != null)
        {
            if (debugCounter % 10 == 0)
            {
                Api.Logger.Notification($"=== Целевой блок реализует ILiquidSink ===");

                // Показываем реальную позицию
                Block targetBlock = Api.World.BlockAccessor.GetBlock(targetPos);
                if (targetBlock is BlockMultiblock)
                {
                    BlockPos realPos = GetRealPosition(targetPos);
                    Api.Logger.Notification($"=== Это мультиблок! Реальная позиция: {realPos} ===");
                }
            }

            // Вызываем передачу
            TryTransferLiquidToSinkDirect(sink, targetPos);
        }
        else
        {
            if (debugCounter % 10 == 0)
            {
                Api.Logger.Notification($"=== Целевой блок НЕ реализует ILiquidSink! ===");
                outputFacing = null; // Сбросим направление
            }

            return;
        }
    }

    private void TryTransferLiquidToSinkDirect(ILiquidSink sink, BlockPos targetPos)
    {
        if (sink == null) return;

        if (debugCounter % 10 == 0)
            Api.Logger.Notification($"=== TryTransferLiquidToSinkDirect: цель на {targetPos} ===");

        // Используем сеть для поиска источников
        var network = networkManager.GetNetwork(Pos);
        if (network == null)
        {
            if (debugCounter % 20 == 0)
                Api.Logger.Notification($"=== СЕТЬ НЕ НАЙДЕНА ===");
            return;
        }

        if (debugCounter % 20 == 0)
            Api.Logger.Notification($"=== Размер сети: {network.Pipes.Count} труб ===");

        // Собираем все позиции для исключения
        var excludePositions = new HashSet<BlockPos>();
        excludePositions.Add(Pos);
        excludePositions.Add(targetPos);

        int sourcesChecked = 0;

        // Ищем источник жидкости в сети
        foreach (var pipePos in network.Pipes)
        {
            if (excludePositions.Contains(pipePos)) continue;

            // Проверяем все стороны трубы
            for (int i = 0; i < 6; i++)
            {
                BlockFacing facing = BlockFacing.ALLFACES[i];
                BlockPos sourcePos = pipePos.AddCopy(facing);

                if (excludePositions.Contains(sourcePos)) continue;

                // Пропускаем другие трубы
                Block sourceBlock = Api.World.BlockAccessor.GetBlock(sourcePos);
                if (sourceBlock is BlockPipeBase)
                    continue;

                sourcesChecked++;

                if (debugCounter % 5 == 0)
                {
                    Api.Logger.Notification($"=== Проверяем источник #{sourcesChecked}: {sourcePos} ===");
                    Api.Logger.Notification($"=== Визуальный блок: {sourceBlock?.GetType().Name} ===");
                }

                // Ищем ILiquidSource (с учетом мультиблоков)
                ILiquidSource liquidSource = GetLiquidSourceAtPosition(sourcePos);

                if (liquidSource != null)
                {
                    if (debugCounter % 5 == 0)
                        Api.Logger.Notification($"=== НАЙДЕН ILiquidSource! ===");

                    // Пытаемся передать
                    if (TryTransferFromBlockSourceToSink(sourcePos, liquidSource, sink, targetPos))
                    {
                        if (debugCounter % 5 == 0)
                            Api.Logger.Notification($"=== Успешно перенесли жидкость из {sourcePos} ===");
                        return;
                    }
                    else if (debugCounter % 10 == 0)
                    {
                        Api.Logger.Notification($"=== Не удалось перенести жидкость из {sourcePos} ===");
                    }
                }
                else if (debugCounter % 20 == 0)
                {
                    Api.Logger.Notification($"=== Блок НЕ реализует ILiquidSource ===");
                }
            }
        }

        if (sourcesChecked == 0 && debugCounter % 10 == 0)
            Api.Logger.Notification($"=== НЕ НАЙДЕНО НИ ОДНОГО ИСТОЧНИКА ===");
    }

    private bool TryTransferFromBlockSourceToSink(BlockPos sourcePos, ILiquidSource source, ILiquidSink sink,
        BlockPos targetPos)
    {
        // Проверяем тайминг
        if (!CanTransferFrom(sourcePos))
        {
            if (debugCounter % 30 == 0)
                Api.Logger.Notification($"=== Тайминг не позволяет передачу из {sourcePos} ===");
            return false;
        }

        if (debugCounter % 5 == 0)
        {
            Api.Logger.Notification($"=== TryTransferFromBlockSourceToSink ===");
            Api.Logger.Notification($"=== Визуальная позиция источника: {sourcePos} ===");
            Api.Logger.Notification($"=== Визуальная позиция цели: {targetPos} ===");
        }

        // Определяем РЕАЛЬНЫЕ позиции для обоих блоков
        BlockPos realSourcePos = GetRealPosition(sourcePos);
        BlockPos realTargetPos = GetRealPosition(targetPos);

        if (debugCounter % 5 == 0 && (!realSourcePos.Equals(sourcePos) || !realTargetPos.Equals(targetPos)))
        {
            Api.Logger.Notification($"=== Реальная позиция источника: {realSourcePos} ===");
            Api.Logger.Notification($"=== Реальная позиция цели: {realTargetPos} ===");
        }

        // Получаем содержимое из РЕАЛЬНОЙ позиции источника
        var contentStack = source.GetContent(realSourcePos);
        if (contentStack == null)
        {
            if (debugCounter % 20 == 0)
                Api.Logger.Notification($"=== Источник пуст (GetContent вернул null) ===");
            return false;
        }

        if (debugCounter % 5 == 0)
        {
            Api.Logger.Notification(
                $"=== Содержимое источника: {contentStack.Collectible?.Code}, StackSize: {contentStack.StackSize} ===");
        }

        // Проверяем фильтр
        if (!CheckItemAgainstLiquidFilter(contentStack))
        {
            if (debugCounter % 10 == 0)
                Api.Logger.Notification($"=== Жидкость не прошла фильтр ===");
            return false;
        }

        // Вычисляем количество для передачи (в литрах)
        float currentLitres = source.GetCurrentLitres(realSourcePos);

        if (debugCounter % 10 == 0)
            Api.Logger.Notification($"=== Текущее количество в источнике: {currentLitres} литров ===");

        float litresToTransfer = Math.Min(transferRate / 1000f, currentLitres);
        if (litresToTransfer <= 0)
        {
            if (debugCounter % 20 == 0)
                Api.Logger.Notification($"=== Недостаточно жидкости для передачи ===");
            return false;
        }

        if (debugCounter % 5 == 0)
            Api.Logger.Notification($"=== Пытаемся передать {litresToTransfer} литров жидкости ===");

        // Пытаемся передать жидкость в РЕАЛЬНУЮ позицию цели
        int movedItems = sink.TryPutLiquid(realTargetPos, contentStack, litresToTransfer);

        if (movedItems > 0)
        {
            // Забираем переданное количество из РЕАЛЬНОЙ позиции источника
            ItemStack takenStack = source.TryTakeContent(realSourcePos, movedItems);

            lastTransferTime[sourcePos] = Api.World.ElapsedMilliseconds;

            // Обновляем РЕАЛЬНЫЕ позиции
            Api.World.BlockAccessor.MarkBlockDirty(realSourcePos);
            Api.World.BlockAccessor.MarkBlockDirty(realTargetPos);

            // Также обновляем визуальные позиции (для мультиблоков)
            if (!realSourcePos.Equals(sourcePos))
                Api.World.BlockAccessor.MarkBlockDirty(sourcePos);
            if (!realTargetPos.Equals(targetPos))
                Api.World.BlockAccessor.MarkBlockDirty(targetPos);

            if (debugCounter % 2 == 0)
                Api.Logger.Notification($"=== УСПЕХ: Передано {movedItems} единиц жидкости ===");
            return true;
        }
        else
        {
            if (debugCounter % 10 == 0)
            {
                Api.Logger.Notification($"=== НЕУДАЧА: TryPutLiquid вернул 0 ===");

                // Диагностика
                Block sourceBlock = Api.World.BlockAccessor.GetBlock(realSourcePos);
                Block targetBlock = Api.World.BlockAccessor.GetBlock(realTargetPos);

                Api.Logger.Notification($"=== Реальный блок источника: {sourceBlock?.GetType().Name} ===");
                Api.Logger.Notification($"=== Реальный блок цели: {targetBlock?.GetType().Name} ===");
                Api.Logger.Notification($"=== Код жидкости: {contentStack.Collectible?.Code} ===");
            }
        }

        return false;
    }

    private bool CanTransferFrom(BlockPos sourcePos)
    {
        if (!lastTransferTime.ContainsKey(sourcePos))
            return true;

        long elapsed = Api.World.ElapsedMilliseconds - lastTransferTime[sourcePos];
        bool canTransfer = elapsed > MinTransferInterval;

        if (!canTransfer && debugCounter % 20 == 0)
            Api.Logger.Notification($"=== Тайминг: {elapsed}мс из {MinTransferInterval}мс ===");

        return canTransfer;
    }

    // Проверяет предмет через фильтр (если фильтры настроены)
    private bool CheckItemAgainstLiquidFilter(ItemStack itemstack)
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
                // Сравниваем коды предметов
                if (itemstack.Collectible.Code.Equals(filterSlot.Itemstack.Collectible.Code))
                {
                    hasMatchingFilter = true;
                    break;
                }
            }
        }

        // Возвращаем результат в зависимости от режима
        return currentFilterMode == FilterMode.AllowList ? hasMatchingFilter : !hasMatchingFilter;
    }

    private BlockFacing GetFacingFromTo(BlockPos from, BlockPos to)
    {
        int dx = to.X - from.X;
        int dy = to.Y - from.Y;
        int dz = to.Z - from.Z;

        if (dx == 1 && dy == 0 && dz == 0) return BlockFacing.EAST;
        if (dx == -1 && dy == 0 && dz == 0) return BlockFacing.WEST;
        if (dx == 0 && dy == 1 && dz == 0) return BlockFacing.UP;
        if (dx == 0 && dy == -1 && dz == 0) return BlockFacing.DOWN;
        if (dx == 0 && dy == 0 && dz == 1) return BlockFacing.SOUTH;
        if (dx == 0 && dy == 0 && dz == -1) return BlockFacing.NORTH;

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

    public void UpdateFilterSettings(FilterMode mode)
    {
        this.currentFilterMode = mode;
        MarkDirty();
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
    {
        sb.AppendLine(Lang.Get("electricalprogressivetransport:pipe-liquid-insertion-info"));

        // Вызываем базовый метод для информации о соединениях
        base.GetBlockInfo(forPlayer, sb);

        string modeText = currentFilterMode switch
        {
            FilterMode.AllowList => Lang.Get("electricalprogressivetransport:filter-mode-allow"),
            FilterMode.DenyList => Lang.Get("electricalprogressivetransport:filter-mode-deny"),
            _ => "Unknown"
        };
        sb.AppendLine(Lang.Get("electricalprogressivetransport:filter-mode", modeText));

        sb.AppendLine(Lang.Get("electricalprogressivetransport:liquid-transfer-rate", transferRate));

        int activeFilters = 0;
        for (int i = 0; i < _inventory.Count; i++)
        {
            if (!_inventory[i].Empty) activeFilters++;
        }

        sb.AppendLine(Lang.Get("electricalprogressivetransport:active-liquid-filters", activeFilters,
            _inventory.Count));

        // Информация о направлении вывода
        if (outputFacing != null)
        {
            sb.AppendLine(Lang.Get("electricalprogressivetransport:output-facing", outputFacing.Code));
        }
        else
        {
            sb.AppendLine(Lang.Get("electricalprogressivetransport:no-output-facing"));
        }

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

        // Затем специфичную логику для жидкостной трубы
        if (Api?.Side == EnumAppSide.Server)
        {
            Api.World.UnregisterGameTickListener(transferTimer);
        }

        if (_clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog.TryClose();
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        transferRate = tree.GetInt("transferRate", 100);
        currentFilterMode = (FilterMode)tree.GetInt("filterMode", 0);
        transferRate = Math.Max(10, Math.Min(transferRate, 1000));

        // Загружаем настройки порчи
        stopPerishEnabled = tree.GetBool("stopPerishEnabled", true);
        perishRateMultiplier = tree.GetFloat("perishRateMultiplier", 0f);
        stopAllTransitions = tree.GetBool("stopAllTransitions", false);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        tree.SetInt("transferRate", transferRate);
        tree.SetInt("filterMode", (int)currentFilterMode);

        // Сохраняем настройки порчи
        tree.SetBool("stopPerishEnabled", stopPerishEnabled);
        tree.SetFloat("perishRateMultiplier", perishRateMultiplier);
        tree.SetBool("stopAllTransitions", stopAllTransitions);
    }

    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(player, packetid, data);

        if (packetid == 2001) // Закрытие GUI
        {
            if (_clientDialog != null && _clientDialog.IsOpened())
            {
                _clientDialog.TryClose();
            }
        }
        else if (packetid == 2002) // Обновление настроек фильтра
        {
            using (var ms = new System.IO.MemoryStream(data))
            using (var br = new System.IO.BinaryReader(ms))
            {
                FilterMode mode = (FilterMode)br.ReadInt32();
                UpdateFilterSettings(mode);
            }
        }
        else if (packetid == 2003) // Обновление скорости передачи
        {
            var tree = new TreeAttribute();
            tree.FromBytes(data);
            int newRate = tree.GetInt("transferRate", 100);
            newRate = Math.Max(10, Math.Min(newRate, 1000));

            if (newRate != transferRate)
            {
                transferRate = newRate;
                MarkDirty();
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
            }
        }
        else if (packetid == 2004) // Обновление настроек порчи
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