using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ElectricalProgressive.Content;
using ElectricalProgressive.Content.NetworkPipe;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
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
    private long filterSnapshotTimer; // Таймер поддержания пассивных снимков фильтра
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

    // === Кэширование температуры для оптимизации ===
    private float temperatureCached = -1000f;
    private long lastTemperatureUpdate = 0;

    // === Тайминги для предотвращения спама ===
    private Dictionary<BlockPos, long> lastTransferTime = [];
    private const long MinTransferInterval = 500; // Минимум 500 мс между переносами одного предмета
    private const int MaxSourceChecksPerTick = 16;
    private const int ItemTransitPacketId = 1005;
    private const int MaxTransitPathPipes = 96;
    private long lastTransferCleanupTime = 0;
    private readonly List<BlockPos> expiredTransferKeys = [];

    public override InventoryBase Inventory => _inventory;
    public override string InventoryClassName => "insertionpipe";

    // === Геттеры для публичного доступа к настройкам ===
    public int TransferRate => transferRate;
    public FilterMode CurrentFilterMode => currentFilterMode;
    public bool MatchMod => matchMod;
    public bool MatchType => matchType;
    public bool MatchAttributes => matchAttributes;

    public BEItemInsertionPipe()
    {
        _inventory = new InventoryInsertionPipe(18, "insertionpipe", null, null, this);
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        // Output facing resolved lazily on first transfer tick (cheaper place).
        if (api.Side == EnumAppSide.Server)
        {
            transferTimer = api.World.RegisterGameTickListener(OnTransferTick, 1000);
            filterSnapshotTimer = api.World.RegisterGameTickListener(OnFilterSnapshotTick, 1000);
        }

        _inventory.OnAcquireTransitionSpeed -= FreezeFilterTransitionSpeed;
        _inventory.OnAcquireTransitionSpeed += FreezeFilterTransitionSpeed;
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

    // === Взаимодействие с игроком ===
    public bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
            OpenGui(byPlayer as IClientPlayer);

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
            if (_inventory.IsFilterSet(i))
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
            ItemStack filterStack = _inventory.GetFilterSnapshot(i);
            if (filterStack?.Collectible != null)
            {
                bool matches = CheckItemMatchesFilter(itemstack, filterStack);
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
        if (mode != FilterMode.AllowList && mode != FilterMode.DenyList)
            return;

        this.currentFilterMode = mode;
        this.matchMod = matchMod;
        this.matchType = matchType;
        this.matchAttributes = matchAttributes;

        MarkDirty();
    }

    /// <summary>Apply settings after wrench transform without a full FromTreeAttributes.</summary>
    public void ApplyWrenchTransformSettings(int rate, FilterMode mode, bool matchMod, bool matchType, bool matchAttributes)
    {
        transferRate = PipeSecurity.ClampItemTransferRate(rate);
        if (mode == FilterMode.AllowList || mode == FilterMode.DenyList)
            currentFilterMode = mode;
        this.matchMod = matchMod;
        this.matchType = matchType;
        this.matchAttributes = matchAttributes;
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
        FindAndTransferItems(targetContainer, containerPos, block);
    }

    private void FindAndTransferItems(BlockEntityContainer targetContainer, BlockPos targetPos, Vintagestory.API.Common.Block targetBlock)
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

                if (TryTransferFromSource(preferredSourcePos, sourceEndpoint.PipePos, sourceEndpoint.FacingFromPipe.Opposite, targetInventory, targetContainer, targetPos, targetBlock))
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

            if (TryTransferFromSource(checkPos, sourceEndpoint.PipePos, sourceEndpoint.FacingFromPipe.Opposite, targetInventory, targetContainer, targetPos, targetBlock))
            {
                preferredSourcePos = checkPos.Copy();
                sourceCursor = (index + 1) % sourceCount;
                return; // Успешно перенесли предмет
            }
        }

        sourceCursor = (sourceCursor + checks) % sourceCount;
    }

    private bool TryTransferFromSource(BlockPos sourcePos, BlockPos sourcePipePos, BlockFacing directionFromSource, IInventory targetInventory,
        BlockEntityContainer targetContainer, BlockPos targetPos, Vintagestory.API.Common.Block targetBlock)
    {
        // Проверяем тайминг (чтобы не спамить сетевые пакеты)
        if (!CanTransferFrom(sourcePos))
            return false;

        // ПОЛУЧАЕМ КОНТЕЙНЕР ИСТОЧНИКА
        var sourceBlock = Api.World.BlockAccessor.GetBlock(sourcePos);
        var sourceContainer = sourceBlock.GetBlockEntity<BlockEntityContainer>(sourcePos);

        if (sourceContainer == null)
            return false;

        // Claim-aware: only move between inventories the pipe owner may use.
        if (!PipeSecurity.MayAutomatedTransfer(Api.World, OwnerUid, sourcePos, targetPos))
            return false;

        // Получаем инвентарь источника
        IInventory sourceInventory = sourceContainer.Inventory;
        if (sourceInventory == null)
            return false;

        // Определяем направление к цели
        BlockFacing directionToTarget = GetFacingFromTo(Pos, targetPos);
        if (directionToTarget == null)
            return false;

        for (int i = 0; i < sourceInventory.Count; i++)
        {
            ItemSlot sourceSlot = sourceInventory[i];
            if (sourceSlot == null || sourceSlot.Empty || !CheckItemAgainstFilter(sourceSlot.Itemstack))
                continue;

            // Не переносим жидкости
            if (sourceSlot.Itemstack.Collectible.IsLiquid())
                continue;

            ItemSlot targetSlot = FindTargetSlotForAutoPush(
                targetInventory,
                targetContainer,
                targetBlock,
                directionToTarget.Opposite,
                sourceSlot);

            if (targetSlot == null)
                continue;

            if (ExecuteTransfer(sourceSlot, targetSlot, sourceContainer, targetContainer, sourcePos, sourcePipePos, targetPos))
                return true;
        }

        return false;
    }

    private ItemSlot FindTargetSlotForAutoPush(IInventory targetInventory, BlockEntityContainer targetContainer,
        Vintagestory.API.Common.Block targetBlock, BlockFacing pushFromFace, ItemSlot sourceSlot)
    {
        if (!CanInsertIntoSingleTypeContainer(targetContainer, targetInventory, targetBlock, sourceSlot))
            return null;

        ItemSlot autoSlot = null;
        if (targetInventory is InventoryBase targetInventoryBase)
            autoSlot = targetInventoryBase.GetAutoPushIntoSlot(pushFromFace, sourceSlot);

        if (CanUseTargetSlot(autoSlot, sourceSlot))
            return autoSlot;

        return null;
    }

    private bool CanUseTargetSlot(ItemSlot targetSlot, ItemSlot sourceSlot)
    {
        if (targetSlot == null || !targetSlot.CanHold(sourceSlot))
            return false;

        if (targetSlot.Empty)
            return true;

        if (!targetSlot.Itemstack.Equals(Api.World, sourceSlot.Itemstack, GlobalConstants.IgnoredStackAttributes))
            return false;

        int maxStackSize = Math.Min(targetSlot.MaxSlotStackSize, targetSlot.Itemstack.Collectible.MaxStackSize);
        return targetSlot.StackSize < maxStackSize;
    }

    private bool CanInsertIntoSingleTypeContainer(BlockEntityContainer targetContainer, IInventory targetInventory,
        Vintagestory.API.Common.Block targetBlock, ItemSlot sourceSlot)
    {
        if (!IsSingleTypeContainer(targetContainer, targetBlock))
            return true;

        for (int i = 0; i < targetInventory.Count; i++)
        {
            ItemSlot targetSlot = targetInventory[i];
            if (targetSlot == null || targetSlot.Empty)
                continue;

            return targetSlot.Itemstack.Equals(Api.World, sourceSlot.Itemstack, GlobalConstants.IgnoredStackAttributes);
        }

        return true;
    }

    private static bool IsSingleTypeContainer(BlockEntityContainer targetContainer, Vintagestory.API.Common.Block targetBlock)
    {
        string blockPath = targetBlock?.Code?.Path ?? "";
        string entityType = targetContainer?.GetType().Name ?? "";

        return blockPath.Equals("crate", StringComparison.OrdinalIgnoreCase)
            || entityType.Contains("Crate", StringComparison.OrdinalIgnoreCase);
    }

    private bool ExecuteTransfer(ItemSlot sourceSlot, ItemSlot targetSlot, BlockEntity sourceBe,
        BlockEntityContainer targetContainer, BlockPos sourcePos, BlockPos sourcePipePos, BlockPos targetPos)
    {
        try
        {
            ItemStack renderStack = sourceSlot.Itemstack?.Clone();
            if (renderStack != null)
                renderStack.StackSize = 1;

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

                if (renderStack != null)
                    BroadcastItemTransit(renderStack, sourcePos, sourcePipePos, targetPos);

                return true;
            }
        }
        catch (Exception ex)
        {
            Api.Logger.Error($"Ошибка при выполнении переноса: {ex.Message}");
        }

        return false;
    }

    private void BroadcastItemTransit(ItemStack renderStack, BlockPos sourcePos, BlockPos sourcePipePos, BlockPos targetPos)
    {
        if (Api is not ICoreServerAPI sapi)
            return;

        try
        {
            List<BlockPos> pipePath = FindPipePath(sourcePipePos, Pos);
            byte[] data = SerializeTransitPacket(renderStack, sourcePos, pipePath, targetPos);
            sapi.Network.BroadcastBlockEntityPacket(Pos, ItemTransitPacketId, data);
        }
        catch (Exception ex)
        {
            Api.Logger.Error($"Ошибка при отправке визуализации предмета в трубе: {ex.Message}");
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

    private static byte[] SerializeTransitPacket(ItemStack stack, BlockPos sourcePos, List<BlockPos> pipePath, BlockPos targetPos)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        stack.ToBytes(writer);

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
            if (_inventory.IsFilterSet(i)) activeFilters++;
        }

        sb.AppendLine(Lang.Get("electricalprogressivetransport:active-filters", activeFilters, _inventory.Count));

    }

    // === Жизненный цикл блока ===
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        if (Api?.Side == EnumAppSide.Server)
        {
            Api.World.UnregisterGameTickListener(transferTimer);
        }

        Api?.World.UnregisterGameTickListener(filterSnapshotTimer);

        if (_clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog?.TryClose();
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        _inventory?.CaptureFilterSnapshotsFromSlots(worldAccessForResolve);

        if (tree == null)
            return;

        transferRate = PipeSecurity.ClampItemTransferRate(tree.GetInt("transferRate", 1));
        currentFilterMode = (FilterMode)tree.GetInt("filterMode", 0);
        matchMod = tree.GetBool("matchMod", false);
        matchType = tree.GetBool("matchType", true);
        matchAttributes = tree.GetBool("matchAttributes", false);

        if (!PipeSecurity.IsValidFilterMode((int)currentFilterMode))
            currentFilterMode = FilterMode.AllowList;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        tree.SetInt("transferRate", transferRate);
        tree.SetInt("filterMode", (int)currentFilterMode);
        tree.SetBool("matchMod", matchMod);
        tree.SetBool("matchType", matchType);
        tree.SetBool("matchAttributes", matchAttributes);
    }

    protected override bool IsFilterInventoryEmpty()
    {
        if (_inventory == null)
            return true;
        return _inventory.CountActiveFilterSnapshots() == 0;
    }

    public override void OnReceivedServerPacket(int packetid, byte[] data)
    {
        base.OnReceivedServerPacket(packetid, data);

        if (packetid != ItemTransitPacketId || Api?.Side != EnumAppSide.Client || data == null)
            return;

        try
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            var stack = new ItemStack();
            stack.FromBytes(reader);
            stack.ResolveBlockOrItem(Api.World);

            int pointCount = reader.ReadInt32();
            if (pointCount < 0 || pointCount > MaxTransitPathPipes + 2)
                return;

            var points = new List<Vec3d>(pointCount);
            for (int i = 0; i < pointCount; i++)
            {
                int x = reader.ReadInt32();
                int y = reader.ReadInt32();
                int z = reader.ReadInt32();
                points.Add(new Vec3d(x + 0.5, y + 0.5, z + 0.5));
            }

            PipeItemTransitRenderer.Instance?.AddTransit(stack, points);
        }
        catch (Exception ex)
        {
            Api?.Logger?.Error($"Ошибка при получении визуализации предмета в трубе: {ex.Message}");
        }
    }

    public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(player, packetid, data);

        // Only authenticated, in-range players with claim Use may configure this pipe.
        if (packetid is 1001 or 1002 or 1003 or 1004 or GuiDialogInsertionPipe.SetFilterStackPacketId)
        {
            if (Api?.Side != EnumAppSide.Server)
                return;
            if (!PipeSecurity.CanPlayerConfigure(Api.World, player, Pos))
                return;

            SetOwnerIfEmpty(player);
        }

        try
        {
            // Обработка пакетов от GUI
            if (packetid == 1001) // Закрытие GUI
            {
                if (_clientDialog != null && _clientDialog.IsOpened())
                    _clientDialog?.TryClose();
            }
            else if (packetid == 1002) // Обновление настроек фильтра
            {
                if (!PipeSecurity.IsPacketSizeOk(data) || data.Length < 7)
                    return;

                using var ms = new MemoryStream(data);
                using var br = new BinaryReader(ms);
                int modeInt = br.ReadInt32();
                if (!PipeSecurity.IsValidFilterMode(modeInt))
                    return;

                bool modMatch = br.ReadBoolean();
                bool typeMatch = br.ReadBoolean();
                bool attrMatch = br.ReadBoolean();
                UpdateFilterSettings((FilterMode)modeInt, modMatch, typeMatch, attrMatch);
            }
            else if (packetid == 1003) // Обновление скорости передачи
            {
                if (!PipeSecurity.IsPacketSizeOk(data))
                    return;

                var tree = new TreeAttribute();
                tree.FromBytes(data);
                int newRate = PipeSecurity.ClampItemTransferRate(tree.GetInt("transferRate", 1));

                if (newRate != transferRate)
                {
                    transferRate = newRate;
                    MarkDirty();
                    Api.World.BlockAccessor.MarkBlockDirty(Pos);
                }
            }
            else if (packetid == 1004) // legacy no-op
            {
                // ignored
            }
            else if (packetid == GuiDialogInsertionPipe.SetFilterStackPacketId)
            {
                ApplyFilterStackPacket(data);
            }
        }
        catch (Exception ex)
        {
            Api?.Logger?.Warning("[ElectricalProgressive Transport] Item pipe packet {0} rejected: {1}", packetid, ex.Message);
        }
    }

    private void ApplyFilterStackPacket(byte[] data)
    {
        if (!PipeSecurity.IsPacketSizeOk(data) || _inventory == null || Api?.World == null)
            return;

        var tree = new TreeAttribute();
        tree.FromBytes(data);

        int slotId = tree.GetInt("slotId", -1);
        if (slotId < 0 || slotId >= _inventory.Count)
            return;

        ItemStack? stack = tree.GetItemstack("stack");
        if (stack == null)
        {
            _inventory.ClearFilterSnapshot(slotId);
        }
        else
        {
            ItemStack? clean = PipeSecurity.SanitizeFilterSnapshot(Api.World, stack, liquidsOnly: false);
            if (clean == null)
                return;

            _inventory.SetFilterSnapshot(slotId, clean);
        }

        MarkDirty(true);
        Api.World.BlockAccessor.MarkBlockDirty(Pos);
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
        Api?.World.UnregisterGameTickListener(filterSnapshotTimer);

        if (_clientDialog != null && _clientDialog.IsOpened())
        {
            _clientDialog?.TryClose();
        }
    }
}
