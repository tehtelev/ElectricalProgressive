using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressiveTransport
{
    public class BELiquidInsertionPipe : BlockEntityGenericTypedContainer
    {
        private long transferTimer;
        private int transferRate = 100; // В миллилитрах
        private BlockFacing outputFacing = null;
        private int debugCounter = 0;
        
        protected bool[] connectedSides = new bool[6];
        protected BlockPos?[] connectedPipes = new BlockPos?[6];
        protected PipeNetworkManager networkManager;
        
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
        
        // НАСТРОЙКИ ОСТАНОВКИ ПОРЧИ (ДОБАВЛЕНО)
        private bool stopPerishEnabled = true; // Останавливать порчу
        private float perishRateMultiplier = 0f; // Множитель скорости порчи (0 = полная остановка)
        private bool stopAllTransitions = false; // Останавливать все типы переходов
        
        // Кэширование температуры для оптимизации (ДОБАВЛЕНО)
        private float temperatureCached = -1000f;
        private long lastTemperatureUpdate = 0;
        
        // Тайминги
        private Dictionary<BlockPos, long> lastTransferTime = new Dictionary<BlockPos, long>();
        private const long MinTransferInterval = 100;
        
        public override InventoryBase Inventory => _inventory;
        public override string InventoryClassName => "liquidinsertionpipe";
        
        public int TransferRate => transferRate;
        public bool[] ConnectedSides => connectedSides;
        public FilterMode CurrentFilterMode => currentFilterMode;
        
        // Свойства для остановки порчи (ДОБАВЛЕНО)
        public bool StopPerishEnabled => stopPerishEnabled;
        public float PerishRateMultiplier => perishRateMultiplier;
        public bool StopAllTransitions => stopAllTransitions;
        
        public BELiquidInsertionPipe()
        {
            _inventory = new InventoryLiquidInsertionPipe(6, InventoryClassName, null, null, this);
        }
        
        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            
            api.Logger.Notification($"=== Жидкостная труба Initialize на {Pos} ===");
            
            DetermineOutputDirection();
            
            networkManager = ElectricalProgressiveTransport.Instance?.GetNetworkManager();
            networkManager?.AddPipe(Pos, this);
            
            UpdateConnections();
            
            if (api.Side == EnumAppSide.Server)
            {
                transferTimer = api.World.RegisterGameTickListener(OnTransferTick, 200);
                
                // Также регистрируем тик для обработки порчи (ДОБАВЛЕНО)
                api.World.RegisterGameTickListener(OnPerishTick, 2000); // Каждые 2 секунды
            }
            
            // Подписываемся на события инвентаря для контроля скорости порчи (ДОБАВЛЕНО)
            _inventory.OnAcquireTransitionSpeed += OnAcquireTransitionSpeed;
        }

        #region Методы для остановки порчи (ДОБАВЛЕНО)

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
                case EnumTransitionType.Perish:    // Порча
                case EnumTransitionType.Harden:
                case EnumTransitionType.Melt:
                case EnumTransitionType.None:
                case EnumTransitionType.Burn:
                case EnumTransitionType.Ripen:     // Созревание
                case EnumTransitionType.Convert:   // Превращение
                case EnumTransitionType.Dry:       // Сушка
                case EnumTransitionType.Cure:      // Выдержка
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

            Api?.Logger?.Notification($"=== Настройки порчи обновлены: enabled={enabled}, multiplier={multiplier}, stopAll={stopAll} ===");
        }

        // Расчет скорости порчи (аналогично ContainerEFreezer)
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
                        Api.Logger.Notification($"=== Мультиблок -> контрольный блок имеет ILiquidSink: {controlBlock.GetType().Name} ===");
                    return controlSink;
                }
                else if (debugCounter % 20 == 0)
                {
                    Api.Logger.Notification($"=== Контрольный блок {controlBlock?.GetType().Name} НЕ имеет ILiquidSink ===");
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
                        Api.Logger.Notification($"=== Мультиблок -> контрольный блок имеет ILiquidSource: {controlBlock.GetType().Name} ===");
                    return controlSource;
                }
                else if (debugCounter % 20 == 0)
                {
                    Api.Logger.Notification($"=== Контрольный блок {controlBlock?.GetType().Name} НЕ имеет ILiquidSource ===");
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

            if (debugCounter % 5 == 0)
                Api.Logger.Notification($"=== TryPutLiquid вернул: {movedItems} единиц ===");

            if (movedItems > 0)
            {
                // Забираем переданное количество из РЕАЛЬНОЙ позиции источника
                ItemStack takenStack = source.TryTakeContent(realSourcePos, movedItems);

                if (takenStack != null && debugCounter % 5 == 0)
                    Api.Logger.Notification($"=== Изъято из источника: {takenStack.StackSize} единиц ===");

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
        
        public void UpdateConnections()
        {
            for (int i = 0; i < 6; i++)
            {
                connectedSides[i] = false;
                connectedPipes[i] = null;
            }
            
            for (int i = 0; i < 6; i++)
            {
                BlockFacing facing = BlockFacing.ALLFACES[i];
                BlockPos checkPos = Pos.AddCopy(facing);
                
                Block neighborBlock = Api?.World.BlockAccessor.GetBlock(checkPos);
                
                if (neighborBlock is BlockPipeBase)
                {
                    connectedSides[i] = true;
                    connectedPipes[i] = checkPos.Copy();
                    
                    // Обновляем соединение у соседа
                    if (Api.World.BlockAccessor.GetBlockEntity(checkPos) is BEPipe neighborPipe)
                    {
                        neighborPipe.UpdateSingleConnection(facing.Opposite, Pos);
                    }
                    else if (Api.World.BlockAccessor.GetBlockEntity(checkPos) is BEInsertionPipe neighborInserter)
                    {
                        neighborInserter.UpdateSingleConnection(facing.Opposite, Pos);
                    }
                    else if (Api.World.BlockAccessor.GetBlockEntity(checkPos) is BELiquidInsertionPipe neighborLiquidInserter)
                    {
                        neighborLiquidInserter.UpdateSingleConnection(facing.Opposite, Pos);
                    }
                }
            }
            
            MarkDirty();
        }
        
        public void UpdateSingleConnection(BlockFacing side, BlockPos fromPos)
        {
            int index = side.Index;
            connectedSides[index] = true;
            connectedPipes[index] = fromPos.Copy();
            MarkDirty();
        }
        
        public void BreakConnection(BlockFacing side)
        {
            int index = side.Index;
            connectedSides[index] = false;
            connectedPipes[index] = null;
            MarkDirty();
        }
        
        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);
            UpdateConnections();
        }
        
        public override void OnBlockRemoved()
        {
            for (int i = 0; i < 6; i++)
            {
                if (connectedSides[i] && connectedPipes[i] != null)
                {
                    if (Api.World.BlockAccessor.GetBlockEntity(connectedPipes[i]!) is BEPipe neighborPipe)
                    {
                        BlockFacing facing = BlockFacing.ALLFACES[i];
                        neighborPipe.BreakConnection(facing.Opposite);
                    }
                    else if (Api.World.BlockAccessor.GetBlockEntity(connectedPipes[i]!) is BEInsertionPipe neighborInserter)
                    {
                        BlockFacing facing = BlockFacing.ALLFACES[i];
                        neighborInserter.BreakConnection(facing.Opposite);
                    }
                    else if (Api.World.BlockAccessor.GetBlockEntity(connectedPipes[i]!) is BELiquidInsertionPipe neighborLiquidInserter)
                    {
                        BlockFacing facing = BlockFacing.ALLFACES[i];
                        neighborLiquidInserter.BreakConnection(facing.Opposite);
                    }
                }
            }
            
            networkManager?.RemovePipe(Pos);
            
            if (Api?.Side == EnumAppSide.Server)
            {
                Api.World.UnregisterGameTickListener(transferTimer);
            }
            
            if (_clientDialog != null && _clientDialog.IsOpened())
            {
                _clientDialog.TryClose();
            }
            
            base.OnBlockRemoved();
        }
        
        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            base.GetBlockInfo(forPlayer, sb);
            int connections = 0;
            for (int i = 0; i < 6; i++)
            {
                if (connectedSides[i]) connections++;
            }
            
            sb.AppendLine(Lang.Get("electricalprogressivetransport:connections", connections));
            if (networkManager != null)
            {
                var network = networkManager.GetNetwork(Pos);
                if (network != null)
                {
                    sb.AppendLine(Lang.Get("electricalprogressivetransport:network-size", network.Pipes.Count));
                }
            }
            
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
            
            // Информация о направлении вывода
            if (outputFacing != null)
            {
                sb.AppendLine(Lang.Get("electricalprogressivetransport:output-facing", outputFacing.Code));
            }
            else
            {
                sb.AppendLine(Lang.Get("electricalprogressivetransport:no-output-facing"));
            }
            
            // ДОБАВЛЕНО: Информация о настройках порчи
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
        
        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
    
            byte[] connBytes = tree.GetBytes("connections", null);
            if (connBytes != null && connBytes.Length == 6)
            {
                for (int i = 0; i < 6; i++)
                {
                    connectedSides[i] = connBytes[i] == 1;
                }
            }
    
            transferRate = tree.GetInt("transferRate", 100);
            currentFilterMode = (FilterMode)tree.GetInt("filterMode", 0);
            transferRate = Math.Max(10, Math.Min(transferRate, 1000));
            
            // Загружаем настройки порчи (ДОБАВЛЕНО)
            stopPerishEnabled = tree.GetBool("stopPerishEnabled", true);
            perishRateMultiplier = tree.GetFloat("perishRateMultiplier", 0f);
            stopAllTransitions = tree.GetBool("stopAllTransitions", false);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
    
            byte[] connBytes = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                connBytes[i] = (byte)(connectedSides[i] ? 1 : 0);
            }
            tree.SetBytes("connections", connBytes);
    
            tree.SetInt("transferRate", transferRate);
            tree.SetInt("filterMode", (int)currentFilterMode);
            
            // Сохраняем настройки порчи (ДОБАВЛЕНО)
            tree.SetBool("stopPerishEnabled", stopPerishEnabled);
            tree.SetFloat("perishRateMultiplier", perishRateMultiplier);
            tree.SetBool("stopAllTransitions", stopAllTransitions);
        }
        
        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            base.OnReceivedClientPacket(player, packetid, data);
    
            if (packetid == 2001)
            {
                if (_clientDialog != null && _clientDialog.IsOpened())
                {
                    _clientDialog.TryClose();
                }
            }
            else if (packetid == 2002)
            {
                using (var ms = new System.IO.MemoryStream(data))
                using (var br = new System.IO.BinaryReader(ms))
                {
                    FilterMode mode = (FilterMode)br.ReadInt32();
                    UpdateFilterSettings(mode);
                }
            }
            else if (packetid == 2003)
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
            else if (packetid == 2004) // Обновление настроек порчи (НОВЫЙ ПАКЕТ)
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
        
        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            if (_clientDialog != null && _clientDialog.IsOpened())
            {
                _clientDialog.TryClose();
            }
        }
        
        // Тестовый метод для отладки
        public void DebugLiquidPipe()
        {
            Api.Logger.Notification($"=== ДЕБАГ ЖИДКОСТНОЙ ТРУБЫ НА {Pos} ===");
            
            if (outputFacing != null)
            {
                BlockPos targetPos = Pos.AddCopy(outputFacing);
                Api.Logger.Notification($"=== Выходное направление: {outputFacing.Code} -> {targetPos} ===");
                
                Block visualBlock = Api.World.BlockAccessor.GetBlock(targetPos);
                Api.Logger.Notification($"=== Визуальный блок: {visualBlock?.GetType().Name} ===");
                
                BlockPos realPos = GetRealPosition(targetPos);
                Block realBlock = Api.World.BlockAccessor.GetBlock(realPos);
                Api.Logger.Notification($"=== Реальный блок: {realBlock?.GetType().Name} на {realPos} ===");
                
                ILiquidSink sink = GetLiquidSinkAtPosition(targetPos);
                if (sink != null)
                {
                    Api.Logger.Notification($"=== Реализует ILiquidSink ===");
                }
                else
                {
                    Api.Logger.Notification($"=== НЕ реализует ILiquidSink ===");
                }
            }
            else
            {
                Api.Logger.Notification($"=== Выходное направление не определено ===");
            }
        }
    }
    
    public class InventoryLiquidInsertionPipe : InventoryGeneric
    {
        private BELiquidInsertionPipe _entity;
    
        public InventoryLiquidInsertionPipe(int slots, string className, string instanceID, ICoreAPI api, BELiquidInsertionPipe entity)
            : base(slots, className, instanceID, api)
        {
            _entity = entity;
        }
    
        private static ItemSlot CreateLiquidFilterSlot(int slotId, InventoryBase inventory)
        {
            return new LiquidFilterSlot(inventory);
        }
    
        protected override ItemSlot NewSlot(int i)
        {
            return CreateLiquidFilterSlot(i, this);
        }
    }
    
    public class LiquidFilterSlot : ItemSlotWatertight
    {
        public LiquidFilterSlot(InventoryBase inventory) : base(inventory, 1000f) // Большая емкость для фильтра
        {
            // capacityLitres уже установлен в базовом конструкторе
        }
        
        public override int MaxSlotStackSize => 1; // В фильтре только 1 предмет
        
        // Переопределяем CanHold для приема только жидкостей
        public override bool CanHold(ItemSlot sourceSlot)
        {
            if (sourceSlot == null || sourceSlot.Empty)
                return true; // Всегда можно очистить слот
            
            ItemStack sourceStack = sourceSlot.Itemstack;
            if (sourceStack == null || sourceStack.Collectible == null)
                return false;
            
            // 1. Проверяем через IsLiquid() - самый прямой способ
            if (sourceStack.Collectible.IsLiquid())
                return true;
            
            // 2. Проверяем, является ли это BlockLiquidContainerBase (ведра)
            if (sourceStack.Block is BlockLiquidContainerBase)
                return true;
            
            // 3. Проверяем атрибуты контейнера с жидкостью
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
                        Item contentItem = this.inventory.Api.World.GetItem(contentAsset);
                        
                        if (contentItem != null && contentItem.IsLiquid())
                        {
                            return true;
                        }
                    }
                    return true; // Даже если не смогли проверить - предполагаем что это жидкость
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
            
            // 5. Проверяем через ItemLadle (черпаки)
            if (sourceStack.Collectible.Code?.Path?.Contains("ladle") == true)
                return true;
            
            // 6. Для предметов с атрибутом "content" - предполагаем жидкость
            if (sourceStack.Attributes?.HasAttribute("content") == true)
                return true;
            
            // 7. Ни один из проверенных способов не подтвердил что это жидкость - НЕ допускаем
            return false;
        }
        
        // Переопределяем CanTake - в фильтре предметы нельзя брать обычным способом
        public override bool CanTake()
        {
            // В фильтре предметы можно брать только правым кликом для очистки
            return false;
        }
        
        // Переопределяем ActivateSlot для специальной обработки
        public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
        {
            // Если кликаем пустой рукой - очищаем слот
            if (sourceSlot == null || sourceSlot.Empty)
            {
                if (!this.Empty)
                {
                    this.Itemstack = null;
                    this.MarkDirty();
                    op.MovedQuantity = 1;
                }
                return;
            }
            
            // Сначала проверяем, можно ли вообще положить этот предмет
            if (!CanHold(sourceSlot)|| op.ShiftDown || op.Modifiers!= null)
            {
                // Не жидкость - ничего не делаем
                op.MovedQuantity = 0;
                op.RequestedQuantity = 0;
                return;
            }
            
            ItemStack sourceStack = sourceSlot.Itemstack;
            
            // Если пытаемся положить контейнер с жидкостью (ведро)
            if (sourceStack.Block is BlockLiquidContainerBase block)
            {
                HandleLiquidContainer(sourceSlot, block, ref op);
                return;
            }
            
            // Если предмет имеет contentItemCode (бутылки и т.д.)
            if (sourceStack.ItemAttributes?["contentItemCode"].Exists == true)
            {
                HandleContentItem(sourceSlot, ref op);
                return;
            }
            
            // Если это жидкость (portion)
            string itemCode = sourceStack.Collectible.Code?.ToString() ?? "";
            if (itemCode.ToLower().Contains("portion") || sourceStack.Collectible.IsLiquid())
            {
                HandleLiquidItem(sourceSlot, ref op);
                return;
            }
            
            // Для других разрешенных жидкостей - стандартная логика с ограничением количества
            if (this.Empty)
            {
                // Берем только 1 предмет
                this.Itemstack = sourceStack.Clone();
                this.Itemstack.StackSize = 1;
                
                if (sourceSlot.StackSize == 1)
                {
                    sourceSlot.Itemstack = null;
                }
                else
                {
                    sourceSlot.Itemstack.StackSize -= 1;
                }
                
                sourceSlot.MarkDirty();
                this.MarkDirty();
                
                op.MovedQuantity = 1;
                op.RequestedQuantity = 1;
            }
            else
            {
                // Заменяем содержимое слота
                ItemStack temp = this.Itemstack;
                this.Itemstack = sourceStack.Clone();
                this.Itemstack.StackSize = 1;
                
                sourceSlot.Itemstack = temp;
                if (sourceSlot.Itemstack != null)
                {
                    sourceSlot.Itemstack.StackSize = Math.Min(sourceSlot.Itemstack.StackSize, sourceSlot.MaxSlotStackSize);
                }
                
                sourceSlot.MarkDirty();
                this.MarkDirty();
                
                op.MovedQuantity = 1;
                op.RequestedQuantity = 1;
            }
        }
        
        // Обрабатывает контейнер с жидкостью (ведро)
        private void HandleLiquidContainer(ItemSlot containerSlot, BlockLiquidContainerBase block, ref ItemStackMoveOperation op)
        {
            // Если слот фильтра пустой
            if (this.Empty)
            {
                // Создаем копию содержимого ведра
                ItemStack content = block.GetContent(containerSlot.Itemstack);
                if (content != null)
                {
                    // Клонируем содержимое ведра
                    this.Itemstack = content.Clone();
                    this.Itemstack.StackSize = 1;
                    this.MarkDirty();
                    
                    op.MovedQuantity = 1;
                    op.RequestedQuantity = 1;
                }
                else
                {
                    // Ведро пустое - ничего не делаем
                    op.MovedQuantity = 0;
                    op.RequestedQuantity = 0;
                }
            }
            else
            {
                // Если в слоте уже есть что-то, заменяем
                ItemStack temp = this.Itemstack;
                
                ItemStack content = block.GetContent(containerSlot.Itemstack);
                if (content != null)
                {
                    this.Itemstack = content.Clone();
                    this.Itemstack.StackSize = 1;
                }
                else
                {
                    this.Itemstack = null;
                }
                
                this.MarkDirty();
                
                // Возвращаем старую жидкость в контейнер
                if (temp != null)
                {
                    // Пытаемся положить обратно
                    containerSlot.Itemstack = temp;
                    containerSlot.Itemstack.StackSize = 1;
                    containerSlot.MarkDirty();
                }
                
                op.MovedQuantity = 1;
                op.RequestedQuantity = 1;
            }
        }
        
        // Обрабатывает предметы с contentItemCode (бутылки и т.д.)
        private void HandleContentItem(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
        {
            IWorldAccessor world = this.inventory.Api.World;
            
            // Получаем код содержимого из атрибутов
            string contentCode = sourceSlot.Itemstack.ItemAttributes["contentItemCode"].AsString();
            if (contentCode == null)
            {
                op.MovedQuantity = 0;
                op.RequestedQuantity = 0;
                return;
            }
            
            AssetLocation contentAsset = AssetLocation.Create(contentCode, sourceSlot.Itemstack.Collectible.Code.Domain);
            Item contentItem = world.GetItem(contentAsset);
            
            if (contentItem == null)
            {
                op.MovedQuantity = 0;
                op.RequestedQuantity = 0;
                return;
            }
            
            // Создаем стек содержимого
            ItemStack contentStack = new ItemStack(contentItem);
            
            if (this.Empty)
            {
                // Кладем содержимое в слот фильтра
                this.Itemstack = contentStack;
                this.Itemstack.StackSize = 1;
                this.MarkDirty();
                
                // Создаем пустой контейнер
                string emptiedBlockCode = sourceSlot.Itemstack.ItemAttributes["emptiedBlockCode"].AsString();
                if (emptiedBlockCode != null)
                {
                    AssetLocation emptiedAsset = AssetLocation.Create(emptiedBlockCode, sourceSlot.Itemstack.Collectible.Code.Domain);
                    Block emptiedBlock = world.GetBlock(emptiedAsset);
                    
                    if (emptiedBlock != null)
                    {
                        ItemStack emptiedStack = new ItemStack(emptiedBlock);
                        
                        if (sourceSlot.StackSize == 1)
                        {
                            sourceSlot.Itemstack = emptiedStack;
                        }
                        else
                        {
                            sourceSlot.Itemstack.StackSize -= 1;
                            if (!op.ActingPlayer.InventoryManager.TryGiveItemstack(emptiedStack))
                            {
                                world.SpawnItemEntity(emptiedStack, op.ActingPlayer.Entity.Pos.XYZ);
                            }
                        }
                        sourceSlot.MarkDirty();
                    }
                }
                
                op.MovedQuantity = 1;
                op.RequestedQuantity = 1;
            }
            else
            {
                // Заменяем содержимое слота
                ItemStack temp = this.Itemstack;
                this.Itemstack = contentStack;
                this.Itemstack.StackSize = 1;
                this.MarkDirty();
                
                // Возвращаем старую жидкость
                if (temp != null)
                {
                    sourceSlot.Itemstack = temp;
                    sourceSlot.Itemstack.StackSize = 1;
                    sourceSlot.MarkDirty();
                }
                
                op.MovedQuantity = 1;
                op.RequestedQuantity = 1;
            }
        }
        
        // Обрабатывает предметы жидкости (waterportion и т.д.)
        private void HandleLiquidItem(ItemSlot liquidSlot, ref ItemStackMoveOperation op)
        {
            if (this.Empty)
            {
                // Берем 1 предмет жидкости
                this.Itemstack = liquidSlot.Itemstack.Clone();
                this.Itemstack.StackSize = 1;
                
                if (liquidSlot.StackSize == 1)
                {
                    liquidSlot.Itemstack = null;
                }
                else
                {
                    liquidSlot.Itemstack.StackSize -= 1;
                }
                
                liquidSlot.MarkDirty();
                this.MarkDirty();
                
                op.MovedQuantity = 1;
                op.RequestedQuantity = 1;
            }
            else if (this.Itemstack != null && liquidSlot.Itemstack != null)
            {
                // Проверяем, та же ли это жидкость
                if (AreLiquidsEqual(this.Itemstack, liquidSlot.Itemstack))
                {
                    // Та же жидкость - ничего не делаем
                    op.MovedQuantity = 0;
                    op.RequestedQuantity = 0;
                }
                else
                {
                    // Разная жидкость - заменяем
                    ItemStack temp = this.Itemstack;
                    this.Itemstack = liquidSlot.Itemstack.Clone();
                    this.Itemstack.StackSize = 1;
                    
                    liquidSlot.Itemstack = temp;
                    if (liquidSlot.Itemstack != null)
                    {
                        liquidSlot.Itemstack.StackSize = 1;
                    }
                    
                    liquidSlot.MarkDirty();
                    this.MarkDirty();
                    
                    op.MovedQuantity = 1;
                    op.RequestedQuantity = 1;
                }
            }
        }
        
        // Проверяет, одинаковые ли жидкости
        private bool AreLiquidsEqual(ItemStack stack1, ItemStack stack2)
        {
            if (stack1 == null || stack2 == null)
                return false;
            
            // Сравниваем коды предметов
            if (!stack1.Collectible.Code.Equals(stack2.Collectible.Code))
                return false;
            
            // Для контейнеров с жидкостью сравниваем содержимое
            if (stack1.Attributes.HasAttribute("content") && stack2.Attributes.HasAttribute("content"))
            {
                string content1 = stack1.Attributes.GetString("content", "");
                string content2 = stack2.Attributes.GetString("content", "");
                return content1 == content2;
            }
            
            return true;
        }
        
        // Обработка правого клика для очистки слота
        protected override void ActivateSlotRightClick(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
        {
            // Если кликаем правой кнопкой с пустой рукой - очищаем слот
            if (sourceSlot == null || sourceSlot.Empty)
            {
                if (!this.Empty)
                {
                    this.Itemstack = null;
                    this.MarkDirty();
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
        
        // Переопределяем TryFlipWith для ограничения количества
        public override bool TryFlipWith(ItemSlot itemSlot)
        {
            // Сначала проверяем, можно ли вообще поместить этот предмет
            if (!CanHold(itemSlot))
                return false;
                
            if (itemSlot != null && itemSlot.StackSize > 1)
            {
                // Если пытаются положить больше 1 предмета
                if (!this.Empty)
                {
                    // Если слот не пуст, нельзя обменять
                    return false;
                }
                
                // Берем только 1 предмет
                ItemStack singleStack = itemSlot.Itemstack.Clone();
                singleStack.StackSize = 1;
                this.Itemstack = singleStack;
                
                itemSlot.Itemstack.StackSize -= 1;
                if (itemSlot.Itemstack.StackSize <= 0)
                    itemSlot.Itemstack = null;
                    
                itemSlot.MarkDirty();
                this.MarkDirty();
                return true;
            }
            
            // Для 1 предмета - обычный обмен
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
    }
}