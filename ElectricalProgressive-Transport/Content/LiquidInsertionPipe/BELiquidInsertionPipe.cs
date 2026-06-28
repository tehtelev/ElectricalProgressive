using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using ElectricalProgressive.Content.NetworkPipe;
using ElectricalProgressive.Content.NormalPipe;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.LiquidInsertionPipe;

/// <summary>
/// Блок-сущность для жидкостной трубы с фильтрацией и управлением порчей предметов.
/// </summary>
public class BELiquidInsertionPipe : BlockEntityPipeBase
{
    #region Поля таймингов и состояния

    private long transferTimer;
    private long filterSnapshotTimer;
    private int transferRate = 100; // Скорость передачи в миллилитрах за тик
    private BlockFacing outputFacing = null;
    private int debugCounter = 0;
    private int sourceCursor = 0;
    private BlockPos? preferredSourcePos = null;
    private ILiquidSource? preferredSource = null;
    private BlockPos? preferredRealSourcePos = null;
    private BlockPos? preferredSourcePipePos = null;
    private ILiquidSink? cachedSink = null;
    private BlockPos? cachedTargetPos = null;
    private BlockPos? cachedRealTargetPos = null;

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

    #region Кэширование температуры

    private float temperatureCached = -1000f;
    private long lastTemperatureUpdate = 0;

    #endregion

    #region Тайминги передачи

    /// <summary>Время последней передачи для каждой позиции источника</summary>
    private Dictionary<BlockPos, long> lastTransferTime = [];
    /// <summary>Минимальный интервал между передачами в миллисекундах</summary>
    private const long MinTransferInterval = 100;
    private const int BaseTransferTickIntervalMs = 200;
    private const int TransferTickIntervalMs = 1000;
    private const int LiquidTransitPacketId = 1006;
    private const int MaxTransitPathPipes = 96;
    private const long VisualUpdateInterval = 1000;
    private const int MaxSourceChecksPerTick = 24;
    private const long FailedTransferBackoffInitialMs = 1000;
    private const long FailedTransferBackoffMaxMs = 8000;
    private const bool EnableTransferProfiler = true;
    private const long TransferProfilerLogIntervalMs = 15000;
    private long lastTransferCleanupTime = 0;
    private long lastVisualUpdateTime = 0;
    private long nextTransferAttemptTime = 0;
    private long currentTransferBackoffMs = 0;
    private int consecutiveFailedTransferTicks = 0;
    private readonly List<BlockPos> expiredTransferKeys = [];

    private static long profilerNextLogTime = 0;
    private static long profilerTickCount = 0;
    private static long profilerAttemptCount = 0;
    private static long profilerSuccessCount = 0;
    private static long profilerBackoffSkipCount = 0;
    private static long profilerSourceChecks = 0;
    private static long profilerSinkCacheHits = 0;
    private static long profilerSinkCacheMisses = 0;
    private static long profilerPreferredSourceHits = 0;
    private static long profilerVisualUpdates = 0;
    private static long profilerVisualSkips = 0;
    private static double profilerTotalTickMs = 0;
    private static double profilerMaxTickMs = 0;

    #endregion

    // Переопределяем свойство Inventory
    public override InventoryBase Inventory => _inventory;
    public override string InventoryClassName => "liquidinsertionpipe";

    /// <summary>Скорость передачи жидкости</summary>
    public int TransferRate => transferRate;
    /// <summary>Текущий режим фильтрации</summary>
    public FilterMode CurrentFilterMode => currentFilterMode;

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
            transferTimer = api.World.RegisterGameTickListener(OnTransferTick, TransferTickIntervalMs);
        }

        filterSnapshotTimer = api.World.RegisterGameTickListener(OnFilterSnapshotTick, 1000);
        _inventory.OnAcquireTransitionSpeed -= FreezeFilterTransitionSpeed;
        _inventory.OnAcquireTransitionSpeed += FreezeFilterTransitionSpeed;
    }

    /// <summary>Получает базовый код блока для жидкостной трубы</summary>
    public override string GetBaseBlockCode()
    {
        return "electricalprogressivetransport:pipe-liquid-insertion";
    }

    public override void UpdateConnections(bool updateNeighbors = true)
    {
        base.UpdateConnections(updateNeighbors);
        ResetTransferBackoff();
        outputFacing = null;
        cachedSink = null;
        cachedTargetPos = null;
        cachedRealTargetPos = null;
    }

    private static float FreezeFilterTransitionSpeed(EnumTransitionType transType, ItemStack stack, float baseMul)
    {
        return 0f;
    }

    private void OnFilterSnapshotTick(float dt)
    {
        if (Api?.World == null)
            return;

        _inventory.MaintainFilterSnapshots(Api.World);
    }

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
    private ILiquidSink? GetLiquidSinkAtPosition(BlockPos pos)
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

    private bool TryGetCachedSink(
        [NotNullWhen(true)] out ILiquidSink? sink,
        [NotNullWhen(true)] out BlockPos? targetPos,
        [NotNullWhen(true)] out BlockPos? realTargetPos)
    {
        if (outputFacing == null)
        {
            sink = null;
            targetPos = null;
            realTargetPos = null;
            return false;
        }

        targetPos = Pos.AddCopy(outputFacing);
        if (cachedSink != null && cachedTargetPos != null && cachedRealTargetPos != null && cachedTargetPos.Equals(targetPos))
        {
            RecordSinkCacheHit();
            sink = cachedSink;
            realTargetPos = cachedRealTargetPos;
            return true;
        }

        sink = GetLiquidSinkAtPosition(targetPos);
        if (sink == null)
        {
            RecordSinkCacheMiss();
            cachedSink = null;
            cachedTargetPos = null;
            cachedRealTargetPos = null;
            realTargetPos = null;
            return false;
        }

        cachedSink = sink;
        cachedTargetPos = targetPos.Copy();
        cachedRealTargetPos = GetRealPosition(targetPos);
        realTargetPos = cachedRealTargetPos;
        RecordSinkCacheMiss();
        return true;
    }

    /// <summary>
    /// Получает ILiquidSource из позиции с учётом мультиблоков.
    /// </summary>
    private ILiquidSource? GetLiquidSourceAtPosition(BlockPos pos)
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

        cachedSink = null;
        cachedTargetPos = null;
        cachedRealTargetPos = null;

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
            ILiquidSink? liquidSink = GetLiquidSinkAtPosition(checkPos);

            if (liquidSink != null)
            {
                outputFacing = facing;
                return;
            }
        }

        outputFacing = null;
        preferredSourcePos = null;
        preferredSource = null;
        preferredRealSourcePos = null;
        preferredSourcePipePos = null;
    }

    /// <summary>Основной тик передачи жидкости</summary>
    private void OnTransferTick(float dt)
    {
        debugCounter++;
        long profileStart = StartTransferProfile();
        bool attemptedTransfer = false;
        bool transferred = false;
        bool skippedByBackoff = false;

        try
        {
            if (Api == null || NetworkManager == null)
                return;

            long now = Api.World.ElapsedMilliseconds;
            if (nextTransferAttemptTime > now)
            {
                skippedByBackoff = true;
                return;
            }

            // Обновляем направление вывода периодически или если оно не установлено
            if (outputFacing == null || debugCounter % 20 == 0)
            {
                DetermineOutputDirection();
            }

            if (outputFacing == null)
            {
                attemptedTransfer = true;
                ApplyTransferBackoff(now);
                return;
            }

            attemptedTransfer = true;
            if (TryGetCachedSink(out var sink, out var targetPos, out var realTargetPos))
            {
                transferred = TryTransferLiquidToSinkDirect(sink, targetPos, realTargetPos);
            }
            else
            {
                outputFacing = null; // Сбрасываем направление если цель не найдена
                preferredSourcePos = null;
                preferredSource = null;
                preferredRealSourcePos = null;
                preferredSourcePipePos = null;
            }

            if (transferred)
                ResetTransferBackoff();
            else
                ApplyTransferBackoff(now);
        }
        finally
        {
            FinishTransferProfile(profileStart, attemptedTransfer, transferred, skippedByBackoff);
        }
    }

    /// <summary>
    /// Попытка передачи жидкости в sink через поиск источников в сети труб.
    /// </summary>
    private bool TryTransferLiquidToSinkDirect(ILiquidSink sink, BlockPos targetPos, BlockPos realTargetPos)
    {
        if (sink == null)
            return false;

        // Используем сеть для поиска источников жидкости
        var network = NetworkManager.GetNetwork(Pos);
        if (network == null)
        {
            return false;
        }

        var excludedSourcePositions = BuildExcludedLiquidSourcePositions(network, targetPos);

        if (preferredSource != null && preferredSourcePos != null && preferredRealSourcePos != null &&
            !excludedSourcePositions.Contains(preferredSourcePos))
        {
            if (TryTransferFromBlockSourceToSink(
                    preferredSourcePos,
                    preferredSourcePipePos ?? Pos,
                    preferredRealSourcePos,
                    preferredSource,
                    sink,
                    targetPos,
                    realTargetPos))
            {
                RecordPreferredSourceHit();
                return true;
            }

            preferredSourcePos = null;
            preferredSource = null;
            preferredRealSourcePos = null;
            preferredSourcePipePos = null;
        }

        int sourceCount = network.LiquidSources.Count;
        if (sourceCount == 0)
            return false;

        if (sourceCursor >= sourceCount)
            sourceCursor = 0;

        int checks = Math.Min(MaxSourceChecksPerTick, sourceCount);

        // Ищем источник жидкости по кэшу сети, распределяя большой поиск по нескольким тикам.
        for (int checkedCount = 0; checkedCount < checks; checkedCount++)
        {
            RecordSourceCheck();
            int index = (sourceCursor + checkedCount) % sourceCount;
            var sourceEndpoint = network.LiquidSources[index];
            BlockPos sourcePos = sourceEndpoint.EndpointPos;
            if (excludedSourcePositions.Contains(sourcePos))
                continue;

            ILiquidSource liquidSource = GetLiquidSourceAtPosition(sourcePos);

            if (liquidSource != null)
            {
                BlockPos realSourcePos = GetRealPosition(sourcePos);
                if (TryTransferFromBlockSourceToSink(sourcePos, sourceEndpoint.PipePos, realSourcePos, liquidSource, sink, targetPos, realTargetPos))
                {
                    preferredSourcePos = sourcePos.Copy();
                    preferredSource = liquidSource;
                    preferredRealSourcePos = realSourcePos.Copy();
                    preferredSourcePipePos = sourceEndpoint.PipePos.Copy();
                    sourceCursor = (index + 1) % sourceCount;
                    return true; // Успешная передача, выходим
                }
            }
        }

        sourceCursor = (sourceCursor + checks) % sourceCount;
        return false;
    }

    private HashSet<BlockPos> BuildExcludedLiquidSourcePositions(PipeNetwork network, BlockPos targetPos)
    {
        var excluded = new HashSet<BlockPos>
        {
            Pos.Copy(),
            targetPos.Copy()
        };

        foreach (var inserterPos in network.Inserters)
        {
            excluded.Add(inserterPos.Copy());

            if (Api.World.BlockAccessor.GetBlockEntity(inserterPos) is BELiquidInsertionPipe otherPipe
                && otherPipe.outputFacing != null)
            {
                excluded.Add(inserterPos.AddCopy(otherPipe.outputFacing));
            }
        }

        return excluded;
    }

    /// <summary>
    /// Попытка передачи жидкости от источника к sink.
    /// </summary>
    private bool TryTransferFromBlockSourceToSink(
        BlockPos sourcePos,
        BlockPos sourcePipePos,
        BlockPos realSourcePos,
        ILiquidSource source,
        ILiquidSink sink,
        BlockPos targetPos,
        BlockPos realTargetPos)
    {
        if (realSourcePos.Equals(realTargetPos))
            return false;

        if (!IsLivePipePathConnected(sourcePipePos, Pos))
            return false;

        // Проверяем тайминг (не передаём слишком часто из одного источника)
        if (!CanTransferFrom(sourcePos))
        {
            return false;
        }

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
        float litresToTransfer = Math.Min(GetBatchedTransferLitres(), currentLitres);

        if (litresToTransfer <= 0)
        {
            return false;
        }

        // Пытаемся передать жидкость в реальную позицию цели
        int movedItems = sink.TryPutLiquid(realTargetPos, contentStack, litresToTransfer);

        if (movedItems > 0)
        {
            ItemStack renderStack = contentStack.Clone();
            renderStack.StackSize = 1;

            // Забираем переданное количество из реальной позиции источника
            ItemStack takenStack = source.TryTakeContent(realSourcePos, movedItems);

            lastTransferTime[sourcePos] = Api.World.ElapsedMilliseconds;
            CleanupTransferTimers();

            bool sourceEmptied = currentLitres <= litresToTransfer + 0.0001f;
            MarkLiquidEndpointsDirty(realSourcePos, realTargetPos, sourcePos, targetPos, sourceEmptied);
            BroadcastLiquidTransit(renderStack, sourcePos, sourcePipePos, targetPos, litresToTransfer);

            return true;
        }



        return false;
    }

    private void BroadcastLiquidTransit(ItemStack renderStack, BlockPos sourcePos, BlockPos sourcePipePos, BlockPos targetPos, float litres)
    {
        if (Api is not ICoreServerAPI sapi)
            return;

        try
        {
            List<BlockPos> pipePath = FindPipePath(sourcePipePos, Pos);
            byte[] data = SerializeLiquidTransitPacket(renderStack, litres, sourcePos, pipePath, targetPos);
            sapi.Network.BroadcastBlockEntityPacket(Pos, LiquidTransitPacketId, data);
        }
        catch (Exception ex)
        {
            Api.Logger.Error($"Ошибка при отправке визуализации жидкости в трубе: {ex.Message}");
        }
    }

    private List<BlockPos> FindPipePath(BlockPos startPipePos, BlockPos endPipePos)
    {
        var network = NetworkManager?.GetNetwork(Pos);
        if (network == null || startPipePos == null || endPipePos == null)
            return [Pos.Copy()];

        if (startPipePos.Equals(endPipePos))
            return [endPipePos.Copy()];

        var queue = new Queue<BlockPos>();
        var visited = new HashSet<BlockPos>();
        var previous = new Dictionary<BlockPos, BlockPos>();

        BlockPos start = startPipePos.Copy();
        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0 && visited.Count <= MaxTransitPathPipes)
        {
            BlockPos current = queue.Dequeue();
            foreach (BlockPos neighbor in GetConnectedPipeNeighbors(current, network))
            {
                if (!visited.Add(neighbor))
                    continue;

                previous[neighbor] = current;
                if (neighbor.Equals(endPipePos))
                    return ReconstructPath(start, neighbor, previous);

                queue.Enqueue(neighbor);
            }
        }

        return [startPipePos.Copy(), endPipePos.Copy()];
    }

    private IEnumerable<BlockPos> GetConnectedPipeNeighbors(BlockPos pipePos, PipeNetwork network)
    {
        bool[] connectedSides = null;
        bool[] connectedToInventory = null;
        var entity = Api.World.BlockAccessor.GetBlockEntity(pipePos);

        if (entity is BEPipe pipe)
        {
            connectedSides = pipe.ConnectedSides;
            connectedToInventory = pipe.ConnectedToInventory;
        }
        else if (entity is BlockEntityPipeBase pipeBase)
        {
            connectedSides = pipeBase.ConnectedSides;
            connectedToInventory = pipeBase.ConnectedToInventory;
        }

        if (connectedSides == null)
            yield break;

        for (int i = 0; i < 6; i++)
        {
            if (!connectedSides[i] || connectedToInventory?[i] == true)
                continue;

            BlockPos neighborPos = pipePos.AddCopy(BlockFacing.ALLFACES[i]);
            if (network.Pipes.Contains(neighborPos))
                yield return neighborPos;
        }
    }

    private bool IsLivePipePathConnected(BlockPos startPipePos, BlockPos endPipePos)
    {
        if (startPipePos == null || endPipePos == null)
            return false;

        if (!IsLivePipeAt(startPipePos) || !IsLivePipeAt(endPipePos))
            return false;

        if (startPipePos.Equals(endPipePos))
            return true;

        var queue = new Queue<BlockPos>();
        var visited = new HashSet<BlockPos>();

        BlockPos start = startPipePos.Copy();
        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0 && visited.Count <= MaxTransitPathPipes)
        {
            BlockPos current = queue.Dequeue();
            foreach (BlockPos neighbor in GetLiveConnectedPipeNeighbors(current))
            {
                if (!visited.Add(neighbor))
                    continue;

                if (neighbor.Equals(endPipePos))
                    return true;

                queue.Enqueue(neighbor);
            }
        }

        return false;
    }

    private IEnumerable<BlockPos> GetLiveConnectedPipeNeighbors(BlockPos pipePos)
    {
        bool[] connectedSides = null;
        bool[] connectedToInventory = null;
        var entity = Api.World.BlockAccessor.GetBlockEntity(pipePos);

        if (entity is BEPipe pipe)
        {
            connectedSides = pipe.ConnectedSides;
            connectedToInventory = pipe.ConnectedToInventory;
        }
        else if (entity is BlockEntityPipeBase pipeBase)
        {
            connectedSides = pipeBase.ConnectedSides;
            connectedToInventory = pipeBase.ConnectedToInventory;
        }

        if (connectedSides == null)
            yield break;

        for (int i = 0; i < 6; i++)
        {
            if (!connectedSides[i] || connectedToInventory?[i] == true)
                continue;

            BlockFacing facing = BlockFacing.ALLFACES[i];
            BlockPos neighborPos = pipePos.AddCopy(facing);
            if (!IsLivePipeAt(neighborPos) || !IsLivePipeConnectedBack(neighborPos, facing.Opposite))
                continue;

            yield return neighborPos;
        }
    }

    private bool IsLivePipeAt(BlockPos pipePos)
    {
        return Api.World.BlockAccessor.GetBlock(pipePos) is BlockPipeBase
            && Api.World.BlockAccessor.GetBlockEntity(pipePos) is BEPipe or BlockEntityPipeBase;
    }

    private bool IsLivePipeConnectedBack(BlockPos pipePos, BlockFacing side)
    {
        bool[] connectedSides = null;
        bool[] connectedToInventory = null;
        var entity = Api.World.BlockAccessor.GetBlockEntity(pipePos);

        if (entity is BEPipe pipe)
        {
            connectedSides = pipe.ConnectedSides;
            connectedToInventory = pipe.ConnectedToInventory;
        }
        else if (entity is BlockEntityPipeBase pipeBase)
        {
            connectedSides = pipeBase.ConnectedSides;
            connectedToInventory = pipeBase.ConnectedToInventory;
        }

        return connectedSides?[side.Index] == true && connectedToInventory?[side.Index] != true;
    }

    private static List<BlockPos> ReconstructPath(BlockPos start, BlockPos end, Dictionary<BlockPos, BlockPos> previous)
    {
        var path = new List<BlockPos>();
        BlockPos current = end;

        while (current != null)
        {
            path.Add(current.Copy());
            if (current.Equals(start))
                break;

            current = previous.TryGetValue(current, out BlockPos prev) ? prev : null;
        }

        path.Reverse();
        return path;
    }

    private static byte[] SerializeLiquidTransitPacket(ItemStack stack, float litres, BlockPos sourcePos, List<BlockPos> pipePath, BlockPos targetPos)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        stack.ToBytes(writer);
        writer.Write(litres);

        int pointCount = 2 + (pipePath?.Count ?? 0);
        writer.Write(pointCount);
        WriteBlockPos(writer, sourcePos);

        if (pipePath != null)
        {
            foreach (BlockPos pipePos in pipePath)
                WriteBlockPos(writer, pipePos);
        }

        WriteBlockPos(writer, targetPos);
        return ms.ToArray();
    }

    private static void WriteBlockPos(BinaryWriter writer, BlockPos pos)
    {
        writer.Write(pos.X);
        writer.Write(pos.Y);
        writer.Write(pos.Z);
    }

    private void ResetTransferBackoff()
    {
        nextTransferAttemptTime = 0;
        currentTransferBackoffMs = 0;
        consecutiveFailedTransferTicks = 0;
    }

    private void ApplyTransferBackoff(long now)
    {
        consecutiveFailedTransferTicks++;
        currentTransferBackoffMs = currentTransferBackoffMs <= 0
            ? FailedTransferBackoffInitialMs
            : Math.Min(currentTransferBackoffMs * 2, FailedTransferBackoffMaxMs);
        nextTransferAttemptTime = now + currentTransferBackoffMs;
    }

    private static long StartTransferProfile()
    {
        return EnableTransferProfiler ? Stopwatch.GetTimestamp() : 0;
    }

    private void FinishTransferProfile(long startTimestamp, bool attemptedTransfer, bool transferred, bool skippedByBackoff)
    {
        if (!EnableTransferProfiler || Api == null)
            return;

        double elapsedMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;

        profilerTickCount++;
        if (attemptedTransfer)
            profilerAttemptCount++;
        if (transferred)
            profilerSuccessCount++;
        if (skippedByBackoff)
            profilerBackoffSkipCount++;

        profilerTotalTickMs += elapsedMs;
        if (elapsedMs > profilerMaxTickMs)
            profilerMaxTickMs = elapsedMs;

        long now = Api.World.ElapsedMilliseconds;
        if (profilerNextLogTime == 0)
            profilerNextLogTime = now + TransferProfilerLogIntervalMs;

        if (now < profilerNextLogTime)
            return;

        if (profilerAttemptCount > 0 || profilerBackoffSkipCount > 0)
        {
            double avgMs = profilerTickCount > 0 ? profilerTotalTickMs / profilerTickCount : 0;
            Api.Logger.Notification(
                $"[EP Transport] Liquid pipes profile: ticks={profilerTickCount}, attempts={profilerAttemptCount}, success={profilerSuccessCount}, " +
                $"backoffSkips={profilerBackoffSkipCount}, sourceChecks={profilerSourceChecks}, sinkCache={profilerSinkCacheHits}/{profilerSinkCacheMisses}, " +
                $"preferredHits={profilerPreferredSourceHits}, visual={profilerVisualUpdates}/{profilerVisualSkips}, avgMs={avgMs:F3}, maxMs={profilerMaxTickMs:F3}");
        }

        profilerNextLogTime = now + TransferProfilerLogIntervalMs;
        profilerTickCount = 0;
        profilerAttemptCount = 0;
        profilerSuccessCount = 0;
        profilerBackoffSkipCount = 0;
        profilerSourceChecks = 0;
        profilerSinkCacheHits = 0;
        profilerSinkCacheMisses = 0;
        profilerPreferredSourceHits = 0;
        profilerVisualUpdates = 0;
        profilerVisualSkips = 0;
        profilerTotalTickMs = 0;
        profilerMaxTickMs = 0;
    }

    private static void RecordSourceCheck()
    {
        if (EnableTransferProfiler)
            profilerSourceChecks++;
    }

    private static void RecordSinkCacheHit()
    {
        if (EnableTransferProfiler)
            profilerSinkCacheHits++;
    }

    private static void RecordSinkCacheMiss()
    {
        if (EnableTransferProfiler)
            profilerSinkCacheMisses++;
    }

    private static void RecordPreferredSourceHit()
    {
        if (EnableTransferProfiler)
            profilerPreferredSourceHits++;
    }

    private static void RecordVisualUpdate()
    {
        if (EnableTransferProfiler)
            profilerVisualUpdates++;
    }

    private static void RecordVisualSkip()
    {
        if (EnableTransferProfiler)
            profilerVisualSkips++;
    }

    private float GetBatchedTransferLitres()
    {
        return transferRate * (TransferTickIntervalMs / (float)BaseTransferTickIntervalMs) / 1000f;
    }

    private void MarkLiquidEndpointsDirty(BlockPos realSourcePos, BlockPos realTargetPos, BlockPos sourcePos, BlockPos targetPos, bool forceVisualUpdate)
    {
        var blockAccessor = Api.World.BlockAccessor;

        blockAccessor.GetBlockEntity(realSourcePos)?.MarkDirty();
        if (!realTargetPos.Equals(realSourcePos))
            blockAccessor.GetBlockEntity(realTargetPos)?.MarkDirty();

        long now = Api.World.ElapsedMilliseconds;
        if (!forceVisualUpdate && now - lastVisualUpdateTime < VisualUpdateInterval)
        {
            RecordVisualSkip();
            return;
        }

        lastVisualUpdateTime = now;
        RecordVisualUpdate();

        blockAccessor.MarkBlockDirty(realSourcePos);
        if (!realTargetPos.Equals(realSourcePos))
            blockAccessor.MarkBlockDirty(realTargetPos);

        if (!realSourcePos.Equals(sourcePos))
            blockAccessor.MarkBlockDirty(sourcePos);
        if (!realTargetPos.Equals(targetPos))
            blockAccessor.MarkBlockDirty(targetPos);
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
            if (_inventory.IsFilterSet(i))
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
            ItemStack filterStack = _inventory.GetFilterSnapshot(i);
            if (filterStack?.Collectible != null)
            {
                // Сравниваем коды предметов
                if (itemstack.Collectible.Code.Equals(filterStack.Collectible.Code))
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
            if (_inventory.IsFilterSet(i)) activeFilters++;
        }

        sb.AppendLine(Lang.Get("electricalprogressivetransport:active-liquid-filters", activeFilters, _inventory.Count));
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
        }

        _inventory.OnAcquireTransitionSpeed -= FreezeFilterTransitionSpeed;
        Api?.World.UnregisterGameTickListener(filterSnapshotTimer);

        if (_clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog?.TryClose();
        }
    }

    /// <summary>Загрузка атрибутов из дерева</summary>
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        _inventory.CaptureFilterSnapshotsFromSlots(worldAccessForResolve);

        transferRate = tree.GetInt("transferRate", 100);
        currentFilterMode = (FilterMode)tree.GetInt("filterMode", 0);
        transferRate = Math.Max(10, Math.Min(transferRate, 1000));
    }

    /// <summary>Сохранение атрибутов в дерево</summary>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        if (Api?.World != null)
            _inventory.MaintainFilterSnapshots(Api.World);

        base.ToTreeAttributes(tree);

        tree.SetInt("transferRate", transferRate);
        tree.SetInt("filterMode", (int)currentFilterMode);
    }

    public override void OnReceivedServerPacket(int packetid, byte[] data)
    {
        base.OnReceivedServerPacket(packetid, data);

        if (packetid != LiquidTransitPacketId || Api?.Side != EnumAppSide.Client || data == null)
            return;

        try
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            var stack = new ItemStack();
            stack.FromBytes(reader);
            stack.ResolveBlockOrItem(Api.World);

            float litres = reader.ReadSingle();
            int pointCount = reader.ReadInt32();
            var points = new List<Vec3d>(pointCount);
            for (int i = 0; i < pointCount; i++)
            {
                int x = reader.ReadInt32();
                int y = reader.ReadInt32();
                int z = reader.ReadInt32();
                points.Add(new Vec3d(x + 0.5, y + 0.5, z + 0.5));
            }

            PipeLiquidTransitRenderer.Instance?.AddTransit(stack, points, litres);
        }
        catch (Exception ex)
        {
            Api?.Logger?.Error($"Ошибка при получении визуализации жидкости в трубе: {ex.Message}");
        }
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
        else if (packetid == 2004) // Старый пакет настроек порчи: больше не нужен для пассивных фильтр-снимков.
        {
            MarkDirty();
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
        _inventory.OnAcquireTransitionSpeed -= FreezeFilterTransitionSpeed;
        Api?.World.UnregisterGameTickListener(filterSnapshotTimer);

        if (_clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog?.TryClose();
        }
    }
}
