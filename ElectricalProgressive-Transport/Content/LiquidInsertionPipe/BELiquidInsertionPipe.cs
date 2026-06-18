using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.LiquidInsertionPipe;

/// <summary>
/// Блок-сущность для жидкостной трубы с фильтрацией и управлением порчей предметов.
/// </summary>
public class BELiquidInsertionPipe : BlockEntityPipeBase
{
    #region Поля таймингов и состояния

    private long transferTimer;
    private long perishTimer;
    private int transferRate = 100; // Скорость передачи в миллилитрах за тик
    private BlockFacing outputFacing = null;
    private int debugCounter = 0;
    private int sourceCursor = 0;
    private BlockPos preferredSourcePos = null;

    #endregion

    #region Инвентарь и GUI

    /// <summary>Собственный инвентарь (фильтры для жидкостей)</summary>
    internal InventoryLiquidInsertionPipe _inventory;
    private GuiDialogLiquidInsertionPipe _clientDialog;

    #endregion

    #region Фильтрация

    /// <summary>Режимы работы фильтра</summary>
    public enum FilterMode
    {
        AllowList = 0, // Разрешить только указанные жидкости
        DenyList = 1   // Запретить указанные жидкости
    }

    private FilterMode currentFilterMode = FilterMode.AllowList;

    #endregion

    #region Настройки остановки порчи

    /// <summary>Включена ли остановка порчи</summary>
    private bool stopPerishEnabled = true;
    /// <summary>Множитель скорости порчи (0 = полностью остановить)</summary>
    private float perishRateMultiplier = 0f;
    /// <summary>Останавливать все переходные состояния</summary>
    private bool stopAllTransitions = false;

    #endregion

    #region Кэширование температуры

    private float temperatureCached = -1000f;
    private long lastTemperatureUpdate = 0;

    #endregion

    #region Тайминги передачи

    /// <summary>Время последней передачи для каждой позиции источника</summary>
    private Dictionary<BlockPos, long> lastTransferTime = [];
    /// <summary>Минимальный интервал между передачами в миллисекундах</summary>
    private const long MinTransferInterval = 100;
    private const int MaxSourceChecksPerTick = 24;
    private long lastTransferCleanupTime = 0;
    private readonly List<BlockPos> expiredTransferKeys = [];

    #endregion

    // Переопределяем свойство Inventory
    public override InventoryBase Inventory => _inventory;
    public override string InventoryClassName => "liquidinsertionpipe";

    /// <summary>Скорость передачи жидкости</summary>
    public int TransferRate => transferRate;
    /// <summary>Текущий режим фильтрации</summary>
    public FilterMode CurrentFilterMode => currentFilterMode;

    // Свойства для остановки порчи
    /// <summary>Включена ли остановка порчи</summary>
    public bool StopPerishEnabled => stopPerishEnabled;
    /// <summary>Множитель скорости порчи</summary>
    public float PerishRateMultiplier => perishRateMultiplier;
    /// <summary>Останавливать все переходные состояния</summary>
    public bool StopAllTransitions => stopAllTransitions;

    public BELiquidInsertionPipe()
    {
        _inventory = new InventoryLiquidInsertionPipe(6, InventoryClassName, null, null, this);
    }

    /// <summary>Инициализация блок-сущности</summary>
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        DetermineOutputDirection();

        if (api.Side == EnumAppSide.Server)
        {
            transferTimer = api.World.RegisterGameTickListener(OnTransferTick, 200);
            // Тик для обработки порчи предметов в инвентаре
            perishTimer = api.World.RegisterGameTickListener(OnPerishTick, 2000);
        }

        _inventory.OnAcquireTransitionSpeed += OnAcquireTransitionSpeed;
    }

    /// <summary>Получает базовый код блока для жидкостной трубы</summary>
    public override string GetBaseBlockCode()
    {
        return "electricalprogressivetransport:pipe-liquid-insertion";
    }

    #region Методы обработки порчи предметов

    /// <summary>
    /// Обработчик скорости переходных состояний для контроля порчи.
    /// </summary>
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

    /// <summary>
    /// Проверяет, нужно ли останавливать этот тип перехода состояния.
    /// </summary>
    private static bool ShouldStopTransition(EnumTransitionType transType)
    {
        switch (transType)
        {
            case EnumTransitionType.Perish:     // Порча
            case EnumTransitionType.Harden:     // Затвердевание
            case EnumTransitionType.Melt:       // Плавление
            case EnumTransitionType.None:       // Нет перехода
            case EnumTransitionType.Burn:       // Сгорание
            case EnumTransitionType.Ripen:      // Созревание
            case EnumTransitionType.Convert:    // Превращение
            case EnumTransitionType.Dry:        // Сушка
            case EnumTransitionType.Cure:       // Выдержка
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Тик для обработки порчи предметов в инвентаре.
    /// </summary>
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

                // Если предмет изменился, помечаем как грязный для сохранения
                if (!slot.Itemstack.Equals(Api.World, before))
                {
                    MarkDirty();
                }
            }
        }
    }

    /// <summary>
    /// Устанавливает настройки обработки порчи.
    /// </summary>
    public void SetPerishSettings(bool enabled, float multiplier = 0f, bool stopAll = false)
    {
        stopPerishEnabled = enabled;
        perishRateMultiplier = GameMath.Clamp(multiplier, 0f, 10f);
        stopAllTransitions = stopAll;
        MarkDirty(true);
    }

    #endregion

    /// <summary>
    /// Обработка клика игрока по блоку. Открывает GUI на клиенте.
    /// </summary>
    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        // Открываем свой GUI только на клиенте
        if (Api.Side == EnumAppSide.Client)
        {
            OpenGui(byPlayer as IClientPlayer);
        }

        return true;
    }

    /// <summary>Открывает или закрывает диалог управления трубой</summary>
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
            _clientDialog?.TryClose();
        }
    }

    /// <summary>
    /// Получает реальную позицию с учётом мультиблоков.
    /// Рекурсивно проходит через вложенные мультиблоки.
    /// </summary>
    private BlockPos GetRealPosition(BlockPos pos)
    {
        var block = Api.World.BlockAccessor.GetBlock(pos);

        if (block is BlockMultiblock multiblock)
        {
            BlockPos controlPos = multiblock.GetControlBlockPos(pos);
            // Рекурсивно обрабатываем вложенные мультиблоки
            return GetRealPosition(controlPos);
        }

        return pos;
    }

    /// <summary>
    /// Получает ILiquidSink из позиции с учётом мультиблоков.
    /// </summary>
    private ILiquidSink GetLiquidSinkAtPosition(BlockPos pos)
    {
        var block = Api.World.BlockAccessor.GetBlock(pos);

        // 1. Проверяем сам блок
        if (block is ILiquidSink sink)
        {
            return sink;
        }

        // 2. Если это мультиблок, проверяем контрольный блок
        if (block is BlockMultiblock multiblock)
        {
            BlockPos controlPos = multiblock.GetControlBlockPos(pos);
            var controlBlock = Api.World.BlockAccessor.GetBlock(controlPos);

            if (controlBlock is ILiquidSink controlSink)
            {
                return controlSink;
            }
        }

        return null;
    }

    /// <summary>
    /// Получает ILiquidSource из позиции с учётом мультиблоков.
    /// </summary>
    private ILiquidSource GetLiquidSourceAtPosition(BlockPos pos)
    {
        var block = Api.World.BlockAccessor.GetBlock(pos);

        // 1. Проверяем сам блок
        if (block is ILiquidSource source)
        {
            return source;
        }

        // 2. Если это мультиблок, проверяем контрольный блок
        if (block is BlockMultiblock multiblock)
        {
            BlockPos controlPos = multiblock.GetControlBlockPos(pos);
            var controlBlock = Api.World.BlockAccessor.GetBlock(controlPos);

            if (controlBlock is ILiquidSource controlSource)
            {
                return controlSource;
            }
        }

        return null;
    }

    /// <summary>Определяет направление вывода жидкости в соседние блоки</summary>
    private void DetermineOutputDirection()
    {
        if (Api == null)
            return;

        for (int i = 0; i < 6; i++)
        {
            BlockFacing facing = BlockFacing.ALLFACES[i];
            BlockPos checkPos = Pos.AddCopy(facing);

            // Пропускаем трубы (не передаём жидкость в другие трубы)
            var checkBlock = Api.World.BlockAccessor.GetBlock(checkPos);
            if (checkBlock is BlockPipeBase)
            {
                continue;
            }

            // Ищем ILiquidSink с учётом мультиблоков
            ILiquidSink liquidSink = GetLiquidSinkAtPosition(checkPos);

            if (liquidSink != null)
            {
                outputFacing = facing;
                return;
            }
        }

        outputFacing = null;
    }

    /// <summary>Основной тик передачи жидкости</summary>
    private void OnTransferTick(float dt)
    {
        debugCounter++;

        if (Api == null || NetworkManager == null)
            return;

        // Обновляем направление вывода периодически или если оно не установлено
        if (outputFacing == null || debugCounter % 20 == 0)
        {
            DetermineOutputDirection();
        }

        if (outputFacing == null)
        {
            return;
        }

        // Получаем целевую позицию
        BlockPos targetPos = Pos.AddCopy(outputFacing);

        // Ищем ILiquidSink с учётом мультиблоков
        ILiquidSink sink = GetLiquidSinkAtPosition(targetPos);

        if (sink != null)
        {
            TryTransferLiquidToSinkDirect(sink, targetPos);
        }
        else
        {
            outputFacing = null; // Сбрасываем направление если цель не найдена
        }
    }

    /// <summary>
    /// Попытка передачи жидкости в sink через поиск источников в сети труб.
    /// </summary>
    private void TryTransferLiquidToSinkDirect(ILiquidSink sink, BlockPos targetPos)
    {
        if (sink == null)
            return;

        // Используем сеть для поиска источников жидкости
        var network = NetworkManager.GetNetwork(Pos);
        if (network == null)
        {
            return;
        }

        int sourceCount = network.LiquidSources.Count;
        if (sourceCount == 0)
            return;

        if (sourceCursor >= sourceCount)
            sourceCursor = 0;

        if (preferredSourcePos != null && !preferredSourcePos.Equals(Pos) && !preferredSourcePos.Equals(targetPos))
        {
            for (int i = 0; i < sourceCount; i++)
            {
                var sourceEndpoint = network.LiquidSources[i];
                if (!sourceEndpoint.EndpointPos.Equals(preferredSourcePos))
                    continue;

                ILiquidSource liquidSource = GetLiquidSourceAtPosition(preferredSourcePos);
                if (liquidSource != null && TryTransferFromBlockSourceToSink(preferredSourcePos, liquidSource, sink, targetPos))
                {
                    sourceCursor = (i + 1) % sourceCount;
                    return;
                }

                preferredSourcePos = null;
                break;
            }
        }

        int checks = Math.Min(MaxSourceChecksPerTick, sourceCount);

        // Ищем источник жидкости по кэшу сети, распределяя большой поиск по нескольким тикам.
        for (int checkedCount = 0; checkedCount < checks; checkedCount++)
        {
            int index = (sourceCursor + checkedCount) % sourceCount;
            var sourceEndpoint = network.LiquidSources[index];
            BlockPos sourcePos = sourceEndpoint.EndpointPos;
            if (sourcePos.Equals(Pos) || sourcePos.Equals(targetPos))
                continue;

            ILiquidSource liquidSource = GetLiquidSourceAtPosition(sourcePos);

            if (liquidSource != null)
            {
                if (TryTransferFromBlockSourceToSink(sourcePos, liquidSource, sink, targetPos))
                {
                    preferredSourcePos = sourcePos.Copy();
                    sourceCursor = (index + 1) % sourceCount;
                    return; // Успешная передача, выходим
                }
            }
        }

        sourceCursor = (sourceCursor + checks) % sourceCount;
    }

    /// <summary>
    /// Попытка передачи жидкости от источника к sink.
    /// </summary>
    private bool TryTransferFromBlockSourceToSink(BlockPos sourcePos, ILiquidSource source, ILiquidSink sink, BlockPos targetPos)
    {
        // Проверяем тайминг (не передаём слишком часто из одного источника)
        if (!CanTransferFrom(sourcePos))
        {
            return false;
        }

        // Определяем РЕАЛЬНЫЕ позиции для обоих блоков (с учётом мультиблоков)
        BlockPos realSourcePos = GetRealPosition(sourcePos);
        BlockPos realTargetPos = GetRealPosition(targetPos);

        // Получаем содержимое из реальной позиции источника
        var contentStack = source.GetContent(realSourcePos);
        if (contentStack == null)
        {
            return false;
        }

        // Проверяем фильтр жидкости
        if (!CheckItemAgainstLiquidFilter(contentStack))
        {
            return false;
        }

        // Вычисляем количество для передачи в литрах
        float currentLitres = source.GetCurrentLitres(realSourcePos);
        float litresToTransfer = Math.Min(transferRate / 1000f, currentLitres);

        if (litresToTransfer <= 0)
        {
            return false;
        }

        // Пытаемся передать жидкость в реальную позицию цели
        int movedItems = sink.TryPutLiquid(realTargetPos, contentStack, litresToTransfer);

        if (movedItems > 0)
        {
            // Забираем переданное количество из реальной позиции источника
            ItemStack takenStack = source.TryTakeContent(realSourcePos, movedItems);

            lastTransferTime[sourcePos] = Api.World.ElapsedMilliseconds;
            CleanupTransferTimers();

            // Обновляем позиции для отрисовки (реальные и визуальные)
            Api.World.BlockAccessor.MarkBlockDirty(realSourcePos);
            Api.World.BlockAccessor.MarkBlockDirty(realTargetPos);

            if (!realSourcePos.Equals(sourcePos))
                Api.World.BlockAccessor.MarkBlockDirty(sourcePos);
            if (!realTargetPos.Equals(targetPos))
                Api.World.BlockAccessor.MarkBlockDirty(targetPos);

            return true;
        }



        return false;
    }

    /// <summary>Проверяет, можно ли передать жидкость из источника (по таймингу)</summary>
    private bool CanTransferFrom(BlockPos sourcePos)
    {
        if (!lastTransferTime.ContainsKey(sourcePos))
            return true;

        long elapsed = Api.World.ElapsedMilliseconds - lastTransferTime[sourcePos];
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
            if (now - entry.Value > MinTransferInterval * 100)
                expiredTransferKeys.Add(entry.Key);
        }

        foreach (var key in expiredTransferKeys)
        {
            lastTransferTime.Remove(key);
        }
    }

    /// <summary>
    /// Проверяет предмет через фильтр жидкостей.
    /// Возвращает true если жидкость проходит фильтрацию или фильтры не настроены.
    /// </summary>
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

        // Возвращаем результат в зависимости от режима фильтрации
        return currentFilterMode == FilterMode.AllowList ? hasMatchingFilter : !hasMatchingFilter;
    }

    /// <summary>Вычисляет направление между двумя позициями</summary>
    private static BlockFacing GetFacingFromTo(BlockPos from, BlockPos to)
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

    /// <summary>Обновляет настройки фильтрации</summary>
    public void UpdateFilterSettings(FilterMode mode)
    {
        this.currentFilterMode = mode;
        MarkDirty();
    }

    /// <summary>Получает информацию о блоке для отображения в GUI игрока</summary>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
    {
        base.GetBlockInfo(forPlayer, sb);

        sb.AppendLine("══════════════════════════════════════════");

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

        sb.AppendLine(Lang.Get("electricalprogressivetransport:active-liquid-filters", activeFilters, _inventory.Count));

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

    /// <summary>Обработка удаления блока</summary>
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        // очищаем словарь
        lastTransferTime?.Clear();

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

    /// <summary>Загрузка атрибутов из дерева</summary>
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

    /// <summary>Сохранение атрибутов в дерево</summary>
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

    /// <summary>Обработка клиентских пакетов</summary>
    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(player, packetid, data);

        if (packetid == 2001) // Закрытие GUI
        {
            if (_clientDialog != null && _clientDialog.IsOpened())
            {
                _clientDialog?.TryClose();
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

                MarkDirty();
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
            }
            catch (Exception ex)
            {
                Api?.Logger?.Error($"Ошибка при обновлении настроек порчи: {ex.Message}");
            }
        }
    }

    /// <summary>Обработка разрушения блока без дропа предметов</summary>
    public override void OnBlockBroken(IPlayer byPlayer = null)
    {
        // Очищаем инвентарь
        if (Inventory != null)
        {
            Inventory?.Clear();
        }

        // Вызываем OnBlockRemoved без дропа предметов
        OnBlockRemoved();
    }

    /// <summary>Обработка выгрузки чанка</summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        if (_clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog?.TryClose();
        }
    }
}
