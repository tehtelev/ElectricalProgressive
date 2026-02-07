using ElectricalProgressiveTransport.NetworkPipe;
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
    /// <summary>
    /// Базовый класс для всех типов труб (наследуется от контейнера)
    /// </summary>
    public class BlockEntityPipeBase : BlockEntityGenericTypedContainer
    {
        // Основные данные о соединениях
        protected bool[] connectedSides = new bool[6];
        protected BlockPos?[] connectedPipes = new BlockPos?[6];
        protected bool[] connectedToInventory = new bool[6];

        // Публичные свойства для доступа
        public bool[] ConnectedSides => connectedSides;
        public bool[] ConnectedToInventory => connectedToInventory;
        public BlockPos?[] ConnectedPipes => connectedPipes;

        // Менеджер сети
        protected PipeNetworkManager networkManager;

        // Для моделей труб
        private string currentPipeType = "cross"; // По умолчанию

        // Событие для обновления модели
        public event Action<string> OnPipeTypeChanged;

        /// <summary>
        /// Базовый метод инициализации
        /// </summary>
        public override void Initialize(ICoreAPI api)
        {
            // Сначала вызываем базовую инициализацию контейнера
            base.Initialize(api);

            // Регистрируем трубу в сети
            networkManager = ElectricalProgressiveTransport.Instance?.GetNetworkManager();
            networkManager?.AddPipe(Pos, this);

            // Обновляем соединения
            UpdateConnections();
        }

        /// <summary>
        /// Основной метод обновления соединений
        /// </summary>
        public virtual void UpdateConnections()
        {
            if (Api?.Side == EnumAppSide.Server)
            {
                Api.Logger.Notification($"=== UpdateConnections для {GetType().Name} на {Pos} ===");
            }

            // Сбрасываем все соединения
            for (int i = 0; i < 6; i++)
            {
                connectedSides[i] = false;
                connectedPipes[i] = null;
                connectedToInventory[i] = false;
            }

            int connectionsFound = 0;
            int inventoryConnectionsFound = 0;

            // Проверяем все 6 сторон на наличие труб и блоков с инвентарем
            for (int i = 0; i < 6; i++)
            {
                BlockFacing facing = BlockFacing.ALLFACES[i];
                BlockPos checkPos = Pos.AddCopy(facing);

                // Получаем блок соседа
                Block neighborBlock = Api?.World.BlockAccessor.GetBlock(checkPos);

                if (Api?.Side == EnumAppSide.Server)
                {
                    Api.Logger.Notification($"Проверяем сторону {facing.Code}: блок {neighborBlock?.Code}");
                }

                bool isConnected = false;
                bool isInventoryConnection = false;
                BlockPos? connectedPos = null;

                // 1. Проверка на трубу
                if (IsPipeBlock(neighborBlock))
                {
                    isConnected = true;
                    connectedPos = checkPos.Copy();
                    isInventoryConnection = false;
                }
                // 2. Проверка на блок с инвентарем
                else if (HasValidInventoryBlock(checkPos))
                {
                    isConnected = true;
                    connectedPos = checkPos.Copy();
                    isInventoryConnection = true;
                    inventoryConnectionsFound++;
                }

                if (isConnected)
                {
                    connectionsFound++;
                    connectedSides[i] = true;
                    connectedPipes[i] = connectedPos;
                    connectedToInventory[i] = isInventoryConnection;

                    if (Api?.Side == EnumAppSide.Server)
                    {
                        if (isInventoryConnection)
                        {
                            Api.Logger.Notification($"Найдено соединение с инвентарем на {checkPos}");
                        }
                        else
                        {
                            Api.Logger.Notification($"Найдено соединение с трубой на {checkPos}");
                        }
                    }

                    // Обновляем соединение у соседа (только если это труба)
                    if (!isInventoryConnection)
                    {
                        UpdateNeighborConnection(checkPos, facing.Opposite);
                    }
                }
            }

            if (Api?.Side == EnumAppSide.Server)
            {
                Api.Logger.Notification($"Всего соединений: {connectionsFound}, из них с инвентарями: {inventoryConnectionsFound}");
            }

            // Обновляем модель после изменения соединений
            UpdateBlockModel();

            MarkDirty();
        }

        /// <summary>
        /// Проверяет, является ли блок трубой
        /// </summary>
        protected virtual bool IsPipeBlock(Block block)
        {
            if (block == null) return false;

            // Простая проверка по коду блока
            string code = block.Code?.ToString() ?? "";
            return code.Contains("pipe") || block is BlockPipeBase;
        }

        /// <summary>
        /// Проверяет, есть ли в позиции блок с инвентарем
        /// </summary>
        protected virtual bool HasValidInventoryBlock(BlockPos pos)
        {
            if (Api == null) return false;

            try
            {
                // Получаем блок
                Block block = Api.World.BlockAccessor.GetBlock(pos);
                if (block == null) return false;

                // ПЕРВЫЙ СПОСОБ: проверка на BlockEntityContainer
                BlockEntityContainer container = block.GetBlockEntity<BlockEntityContainer>(pos);
                if (container != null)
                {
                    // Проверяем, что у контейнера есть инвентарь
                    if (container.Inventory != null && container.Inventory.Count > 0)
                    {
                        return true;
                    }
                }

                // ВТОРОЙ СПОСОБ: Получаем BlockEntity и проверяем несколько интерфейсов
                var blockEntity = Api.World.BlockAccessor.GetBlockEntity(pos);

                if (blockEntity != null)
                {
                    // 1. Проверка на BlockEntityContainer
                    if (blockEntity is BlockEntityContainer bec)
                    {
                        return bec.Inventory != null && bec.Inventory.Count > 0;
                    }

                    // 2. Проверка на IBlockEntityContainer
                    if (blockEntity is IBlockEntityContainer ibec)
                    {
                        return ibec.Inventory != null && ibec.Inventory.Count > 0;
                    }

                    // 3. Проверка на IInventory
                    if (blockEntity is IInventory inventory)
                    {
                        return inventory.Count > 0;
                    }

                    // 4. Рефлексия для поиска Inventory свойства
                    try
                    {
                        var prop = blockEntity.GetType().GetProperty("Inventory");
                        if (prop != null)
                        {
                            var invValue = prop.GetValue(blockEntity) as IInventory;
                            return invValue != null && invValue.Count > 0;
                        }
                    }
                    catch { }
                }

                // ТРЕТИЙ СПОСОБ: Проверка по коду блока
                string code = block.Code?.ToString() ?? "";

                // Список блоков с инвентарем
                string[] inventoryKeywords =
                [
                    "chest", "crate", "box", "barrel", "shelf",
                    "hopper", "funnel", "container", "storage",
                    "cabinet", "drawer", "bin", "basket", "bag",
                    "vessel", "pot", "jar", "tub", "tank",
                    "mill", "quern", "press", "forge", "crucible",
                    "machine", "machinebase", "generator", "machinerack"
                ];

                foreach (var keyword in inventoryKeywords)
                {
                    if (code.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Api?.Logger?.Error($"Ошибка при проверке инвентаря в позиции {pos}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Универсальный метод для получения инвентаря из BlockEntity
        /// </summary>
        public static IInventory GetInventoryFromBlockEntity(BlockEntity be)
        {
            if (be == null) return null;

            // 1. BlockEntityContainer
            if (be is BlockEntityContainer container)
            {
                return container.Inventory;
            }

            // 2. IBlockEntityContainer
            if (be is IBlockEntityContainer icon)
            {
                return icon.Inventory;
            }

            // 3. IInventory
            if (be is IInventory inventory)
            {
                return inventory;
            }

            // 4. Рефлексия
            try
            {
                var prop = be.GetType().GetProperty("Inventory");
                if (prop != null)
                {
                    return prop.GetValue(be) as IInventory;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Получение инвентаря из позиции
        /// </summary>
        public IInventory GetInventoryAtPosition(BlockPos pos)
        {
            if (Api == null) return null;

            // Сначала пытаемся получить BlockEntityContainer
            Block block = Api.World.BlockAccessor.GetBlock(pos);
            BlockEntityContainer container = block?.GetBlockEntity<BlockEntityContainer>(pos);

            if (container != null)
            {
                return container.Inventory;
            }

            // Затем общий подход через GetInventoryFromBlockEntity
            var blockEntity = Api.World.BlockAccessor.GetBlockEntity(pos);
            return GetInventoryFromBlockEntity(blockEntity);
        }

        private void UpdateNeighborConnection(BlockPos neighborPos, BlockFacing fromDirection)
        {
            if (Api.World.BlockAccessor.GetBlockEntity(neighborPos) is BlockEntityPipeBase neighborPipe)
            {
                neighborPipe.UpdateSingleConnection(fromDirection, Pos, false);
            }
        }

        public void UpdateSingleConnection(BlockFacing side, BlockPos fromPos, bool fromInventory = false)
        {
            int index = side.Index;
            connectedSides[index] = true;
            connectedPipes[index] = fromPos.Copy();
            connectedToInventory[index] = fromInventory;

            // Обновляем модель
            UpdateBlockModel();

            MarkDirty();
        }

        /// <summary>
        /// Автоматически выбирает и устанавливает правильную модель трубы
        /// </summary>
        public virtual void UpdateBlockModel()
        {
            if (Api == null || Api.Side != EnumAppSide.Server) return;

            // Получаем список подключенных сторон
            List<BlockFacing> connectedFacings = [];
            for (int i = 0; i < 6; i++)
            {
                if (connectedSides[i])
                {
                    connectedFacings.Add(BlockFacing.ALLFACES[i]);
                }
            }

            // Определяем тип модели
            string newPipeType = DeterminePipeType(connectedFacings);

            // Если тип изменился
            if (newPipeType != currentPipeType)
            {
                currentPipeType = newPipeType;

                // Вызываем событие изменения типа
                OnPipeTypeChanged?.Invoke(newPipeType);

                // Обновляем визуальное представление
                UpdateVisualBlockType(newPipeType);
            }
        }

        /// <summary>
        /// Обновляет визуальный тип блока
        /// </summary>
        protected virtual void UpdateVisualBlockType(string pipeType)
        {
            if (Api == null || Api.Side != EnumAppSide.Server) return;

            // Получаем текущий блок
            Block currentBlock = Api.World.BlockAccessor.GetBlock(Pos);
            if (currentBlock == null) return;

            // Определяем базовый код блока (без типа)
            string baseBlockCode = GetBaseBlockCode();
            if (string.IsNullOrEmpty(baseBlockCode)) return;

            // Создаем правильный код блока
            string newBlockCodeString = $"{baseBlockCode}-{pipeType}";
            AssetLocation newBlockCode = new AssetLocation(newBlockCodeString);

            Block newBlock = Api.World.GetBlock(newBlockCode);

            if (newBlock == null)
            {
                Api.Logger.Error($"Блок не найден: {newBlockCode}");
                return;
            }

            if (newBlock.Id != currentBlock.Id)
            {
                // Сохраняем текущие данные
                ITreeAttribute tree = new TreeAttribute();
                this.ToTreeAttributes(tree);

                // Меняем блок
                Api.World.BlockAccessor.ExchangeBlock(newBlock.BlockId, Pos);

                // Восстанавливаем данные
                BlockEntity newEntity = Api.World.BlockAccessor.GetBlockEntity(Pos);
                if (newEntity is BlockEntityPipeBase newPipe)
                {
                    newPipe.FromTreeAttributes(tree, Api.World);
                    newPipe.MarkDirty();
                }

                Api.World.BlockAccessor.MarkBlockDirty(Pos);
                Api.Logger.Notification($"Блок изменен: {currentBlock.Code} -> {newBlock.Code}");
            }
        }

        /// <summary>
        /// Получает базовый код блока (без суффикса типа)
        /// </summary>
        protected virtual string GetBaseBlockCode()
        {
            var currentBlock = Api?.World.BlockAccessor?.GetBlock(Pos);
            if (currentBlock == null) return null;

            string code = currentBlock.Code.ToString();

            // Убираем суффикс типа (последнюю часть после последнего дефиса)
            int lastDash = code.LastIndexOf('-');
            if (lastDash > 0)
            {
                return code.Substring(0, lastDash);
            }

            return code;
        }

        /// <summary>
        /// Определяет тип трубы на основе соединений
        /// </summary>
        private static string DeterminePipeType(List<BlockFacing> facings)
        {
            int count = facings.Count;

            return count switch
            {
                0 => "straight-ns",
                1 => DetermineSingleConnectionType(facings[0]),
                2 => DetermineTwoConnectionType(facings[0], facings[1]),
                3 => DetermineThreeConnectionType(facings),
                4 => DetermineFourConnectionType(facings),
                5 => DetermineFiveConnectionType(facings),
                _ => "cross"
            };
        }

        /// <summary>
        /// Определяет тип для одного соединения (конец трубы)
        /// </summary>
        private static string DetermineSingleConnectionType(BlockFacing facing)
        {
            // Для одного соединения используем прямую трубу
            // Определяем ориентацию на основе направления соединения
            return facing.Axis switch
            {
                EnumAxis.X => "straight-ew",  // Восток или Запад
                EnumAxis.Z => "straight-ns",  // Север или Юг
                EnumAxis.Y => "straight-ud",  // Вверх или Вниз
                _ => "cross"
            };
        }

        /// <summary>
        /// Определяет тип для двух соединений
        /// </summary>
        private static string DetermineTwoConnectionType(BlockFacing f1, BlockFacing f2)
        {
            // Сортируем для единообразия
            List<BlockFacing> sorted = [f1, f2];
            sorted.Sort((a, b) => a.Index.CompareTo(b.Index));
            f1 = sorted[0];
            f2 = sorted[1];

            // Прямая труба (противоположные стороны)
            if (f1.Opposite == f2)
            {
                if (f1.Axis == EnumAxis.Z) return "straight-ns";  // Север-Юг
                if (f1.Axis == EnumAxis.X) return "straight-ew";  // Восток-Запад
                if (f1.Axis == EnumAxis.Y) return "straight-ud";  // Вверх-Вниз
            }

            // Угловая труба
            return (f1.Code, f2.Code) switch
            {
                ("north", "east") => "corner-ne",   // Север-Восток
                ("east", "south") => "corner-se",   // Восток-Юг
                ("south", "west") => "corner-sw",   // Юг-Запад
                ("north", "west") => "corner-nw",   // Север-Запад

                ("north", "up") => "corner-nu",     // Север-Вверх
                ("south", "up") => "corner-su",     // Юг-Вверх
                ("east", "up") => "corner-eu",      // Восток-Вверх
                ("west", "up") => "corner-wu",      // Запад-Вверх

                ("north", "down") => "corner-nd",   // Север-Вниз
                ("south", "down") => "corner-sd",   // Юг-Вниз
                ("east", "down") => "corner-ed",    // Восток-Вниз
                ("west", "down") => "corner-wd",    // Запад-Вниз

                _ => "cross"
            };
        }

        /// <summary>
        /// Определяет тип для трех соединений
        /// </summary>
        private static string DetermineThreeConnectionType(List<BlockFacing> facings)
        {
            // Сначала проверяем, является ли это тройным углом (3 стороны не в одной плоскости)
            if (IsTripleCorner(facings))
            {
                return DetermineTripleCornerType(facings);
            }

            // Затем проверяем Т-образное соединение
            if (IsTeeConnection(facings))
            {
                return DetermineTeeType(facings);
            }

            // Если не подошли под вышеперечисленные категории,
            // проверяем является ли это "Т-образное с вертикальной ножкой" (3 в плоскости + 1 вертикальное)
            return DetermineThreePlusVerticalType(facings);
        }

        /// <summary>
        /// Проверяет, является ли соединение тройным углом (все три стороны не в одной плоскости)
        /// </summary>
        private static bool IsTripleCorner(List<BlockFacing> facings)
        {
            // Для тройного угла каждая сторона должна быть перпендикулярна к двум другим
            // и ни одна не должна быть противоположной другой
            if (facings.Count != 3)
                return false;

            // Проверяем наличие противоположных сторон
            foreach (BlockFacing facing in facings)
            {
                if (facings.Contains(facing.Opposite))
                {
                    return false; // Есть противоположные стороны - это не тройной угол
                }
            }

            // Проверяем, что все три стороны разные оси
            var axes = facings.Select(f => f.Axis).Distinct().ToList();
            return axes.Count == 3;
        }

        /// <summary>
        /// Определяет тип тройного угла
        /// </summary>
        private static string DetermineTripleCornerType(List<BlockFacing> facings)
        {
            facings.Sort((a, b) => a.Index.CompareTo(b.Index));

            string[] codes = facings.Select(f => f.Code).ToArray();

            // Определяем тип на основе комбинации сторон
            if (codes.Contains("north") && codes.Contains("east") && codes.Contains("up"))
            {
                return "triple-neu";  // Север-Восток-Вверх
            }
            else if (codes.Contains("north") && codes.Contains("east") && codes.Contains("down"))
            {
                return "triple-ned";  // Север-Восток-Вниз
            }
            else if (codes.Contains("east") && codes.Contains("south") && codes.Contains("up"))
            {
                return "triple-seu";  // Восток-Юг-Вверх
            }
            else if (codes.Contains("east") && codes.Contains("south") && codes.Contains("down"))
            {
                return "triple-sed";  // Восток-Юг-Вниз
            }
            else if (codes.Contains("south") && codes.Contains("west") && codes.Contains("up"))
            {
                return "triple-swu";  // Юг-Запад-Вверх
            }
            else if (codes.Contains("south") && codes.Contains("west") && codes.Contains("down"))
            {
                return "triple-swd";  // Юг-Запад-Вниз
            }
            else if (codes.Contains("west") && codes.Contains("north") && codes.Contains("up"))
            {
                return "triple-nwu";  // Запад-Север-Вверх
            }
            else if (codes.Contains("west") && codes.Contains("north") && codes.Contains("down"))
            {
                return "triple-nwd";  // Запад-Север-Вниз
            }

            return "cross";  // Запасной вариант
        }

        /// <summary>
        /// Проверяет, является ли соединение Т-образным (все три стороны в одной плоскости)
        /// </summary>
        private static bool IsTeeConnection(List<BlockFacing> facings)
        {
            if (facings.Count != 3) return false;

            // Для Т-образного соединения две стороны должны быть противоположными
            // (образуют прямую линию), а третья - перпендикулярна к ним
            foreach (BlockFacing facing in facings)
            {
                if (facings.Contains(facing.Opposite))
                {
                    return true;  // Нашли противоположные стороны - это Т-образное соединение
                }
            }

            return false;
        }

        /// <summary>
        /// Определяет тип Т-образного соединения (8 типов)
        /// </summary>
        private static string DetermineTeeType(List<BlockFacing> facings)
        {
            if (facings.Count != 3) return "cross";

            // Сортируем стороны для единообразия
            List<string> sortedCodes = facings
                .Select(f => f.Code)
                .OrderBy(code => code)
                .ToList();

            string side1 = sortedCodes[0];
            string side2 = sortedCodes[1];
            string side3 = sortedCodes[2];
            string key = $"{side1}-{side2}-{side3}";

            // Все возможные комбинации 3 сторон
            switch (key)
            {
                // 1. ГОРИЗОНТАЛЬНЫЕ Т (4 типа) - ножка горизонтальная
                case "east-north-south": return "tee-w";      // ножка: восток (линия север-юг)
                case "north-south-west": return "tee-e";      // ножка: запад (линия север-юг)
                case "east-north-west": return "tee-n";      // ножка: север (линия восток-запад)
                case "east-south-west": return "tee-s";      // ножка: юг (линия восток-запад)

                // 2. ВЕРТИКАЛЬНЫЕ Т с горизонтальной линией (4 типа) - ножка вертикальная
                case "north-south-up": return "tee-un";     // ножка: вверх (линия север-юг)
                case "down-north-south": return "tee-dn";     // ножка: вниз (линия север-юг)
                case "east-up-west": return "tee-uw";     // ножка: вверх (линия восток-запад)
                case "down-east-west": return "tee-dw";     // ножка: вниз (линия восток-запад)

                // 3. ГОРИЗОНТАЛЬНЫЕ Т с вертикальной линией (4 типа) - ножка горизонтальная, линия вертикальная
                case "down-east-up": return "tee-eh";     // ножка: восток (линия верх-низ)
                case "down-north-up": return "tee-nh";     // ножка: север (линия верх-низ)
                case "down-south-up": return "tee-sh";     // ножка: юг (линия верх-низ)
                case "down-up-west": return "tee-wh";     // ножка: запад (линия верх-низ)
                default: return "cross";
            }
        }

        /// <summary>
        /// Определяет тип для случая "3 горизонтальных + 1 вертикальное" соединение
        /// </summary>
        private static string DetermineThreePlusVerticalType(List<BlockFacing> facings)
        {
            // Разделяем горизонтальные и вертикальные стороны
            List<BlockFacing> horizontalSides = [];
            List<BlockFacing> verticalSides = [];

            foreach (var facing in facings)
            {
                if (facing.Axis == EnumAxis.Y)
                    verticalSides.Add(facing);
                else
                    horizontalSides.Add(facing);
            }

            // Если есть 3 горизонтальных и 1 вертикальная сторона
            if (horizontalSides.Count == 3 && verticalSides.Count == 1)
            {
                // Определяем недостающую горизонтальную сторону
                List<string> allHorizontalCodes = ["north", "east", "south", "west"];
                List<string> presentHorizontalCodes = horizontalSides.Select(f => f.Code).ToList();

                string missingHorizontal = allHorizontalCodes.FirstOrDefault(code => !presentHorizontalCodes.Contains(code));

                // Определяем вертикальную сторону
                string verticalCode = verticalSides[0].Code;

                switch (missingHorizontal)
                {
                    // Определяем тип на основе комбинации
                    case "north":
                        return verticalCode == "up" ? "four-d" : "four-u"; // Отсутствует север
                    case "east":
                        return verticalCode == "up" ? "four-d" : "four-u"; // Отсутствует восток
                    case "south":
                        return verticalCode == "up" ? "four-d" : "four-u"; // Отсутствует юг
                    case "west":
                        return verticalCode == "up" ? "four-d" : "four-u"; // Отсутствует запад
                }
            }

            // По умолчанию используем существующую модель "four-n"
            return "four-n";
        }

        /// <summary>
        /// Определяет тип для четырех соединений
        /// </summary>
        private static string DetermineFourConnectionType(List<BlockFacing> facings)
        {
            if (facings.Count != 4)
                return "four-n";

            // Находим отсутствующие стороны
            List<BlockFacing> missingSides = [];
            for (int i = 0; i < 6; i++)
            {
                BlockFacing facing = BlockFacing.ALLFACES[i];
                if (!facings.Contains(facing))
                {
                    missingSides.Add(facing);
                }
            }

            if (missingSides.Count != 2)
                return "four-n";

            BlockFacing missing1 = missingSides[0];
            BlockFacing missing2 = missingSides[1];

            // Сортируем для единообразия
            missingSides.Sort((a, b) => a.Index.CompareTo(b.Index));
            missing1 = missingSides[0];
            missing2 = missingSides[1];

            // Проверяем различные комбинации отсутствующих сторон

            // 1. Обе отсутствующие стороны горизонтальные
            if (missing1.Axis != EnumAxis.Y && missing2.Axis != EnumAxis.Y)
            {
                // Определяем, смежные ли они
                if (AreAdjacentHorizontal(missing1, missing2))
                {
                    // Отсутствуют две смежные горизонтальные стороны
                    return (missing1.Code, missing2.Code) switch
                    {
                        ("north", "west") => "four-nw",
                        ("north", "east") => "four-ne",
                        ("south", "west") => "four-sw",
                        ("east", "south") => "four-se",
                        _ => "four-nw"
                    };
                }
                else
                {
                    // Отсутствуют противоположные горизонтальные стороны
                    // Проверяем, какие вертикальные стороны присутствуют
                    bool hasUp = facings.Contains(BlockFacing.UP);
                    bool hasDown = facings.Contains(BlockFacing.DOWN);

                    if (hasUp && hasDown)
                    {
                        // Есть обе вертикальные стороны
                        if (missing1.Axis == EnumAxis.X) // Отсутствуют восток и запад
                        {
                            return "four-u"; // Север-Юг-Вверх-Вниз
                        }
                        else // Отсутствуют север и юг
                        {
                            return "four-v"; // Восток-Запад-Вверх-Вниз (повернутая модель)
                        }
                    }
                    else if (hasUp)
                    {
                        // Есть только верх
                        return "four-u";
                    }
                    else if (hasDown)
                    {
                        // Есть только низ
                        return "four-d";
                    }
                    else
                    {
                        // Нет вертикальных сторон
                        return "cross"; // Должно быть 4 горизонтальных, но у нас их только 2
                    }
                }
            }

            // 2. Одна горизонтальная, одна вертикальная
            if ((missing1.Axis != EnumAxis.Y && missing2.Axis == EnumAxis.Y) ||
                (missing1.Axis == EnumAxis.Y && missing2.Axis != EnumAxis.Y))
            {
                BlockFacing horizontalMissing = missing1.Axis != EnumAxis.Y ? missing1 : missing2;
                BlockFacing verticalMissing = missing1.Axis == EnumAxis.Y ? missing1 : missing2;

                if (verticalMissing.Code == "up")
                {
                    return horizontalMissing.Code switch
                    {
                        "north" => "four-nu",
                        "east" => "four-eu",
                        "south" => "four-su",
                        "west" => "four-wu",
                        _ => "four-nu"
                    };
                }
                else // down
                {
                    return horizontalMissing.Code switch
                    {
                        "north" => "four-nd",
                        "east" => "four-ed",
                        "south" => "four-sd",
                        "west" => "four-wd",
                        _ => "four-nd"
                    };
                }
            }

            // 3. Обе вертикальные (вверх и вниз)
            if (missing1.Code == "up" && missing2.Code == "down")
            {
                // Проверяем, какие горизонтальные стороны присутствуют
                int horizontalCount = facings.Count(f => f.Axis != EnumAxis.Y);

                if (horizontalCount == 4)
                {
                    // Все 4 горизонтальные стороны
                    return "four-n"; // Горизонтальный крест
                }
                else if (horizontalCount == 3)
                {
                    // 3 горизонтальные стороны
                    // Определяем недостающую горизонтальную сторону
                    List<string> allHorizontalCodes = ["north", "east", "south", "west"];
                    List<string> presentHorizontalCodes = facings
                        .Where(f => f.Axis != EnumAxis.Y)
                        .Select(f => f.Code)
                        .ToList();

                    string missingHorizontal = allHorizontalCodes
                        .FirstOrDefault(code => !presentHorizontalCodes.Contains(code));

                    return missingHorizontal switch
                    {
                        "north" => "four-n",
                        "east" => "four-e",
                        "south" => "four-s",
                        "west" => "four-w",
                        _ => "four-n"
                    };
                }
            }

            // Запасной вариант
            return "four-n";
        }

        /// <summary>
        /// Проверяет, являются ли две горизонтальные стороны смежными
        /// </summary>
        private static bool AreAdjacentHorizontal(BlockFacing f1, BlockFacing f2)
        {
            if (f1.Axis == EnumAxis.Y || f2.Axis == EnumAxis.Y) return false;

            // Определяем порядок сторон по часовой стрелке
            var adjacencyMap = new Dictionary<string, string[]>
            {
                { "north", ["west", "east"] },
                { "east", ["north", "south"] },
                { "south", ["east", "west"] },
                { "west", ["south", "north"] }
            };

            return adjacencyMap.ContainsKey(f1.Code) &&
                   adjacencyMap[f1.Code].Contains(f2.Code);
        }

        /// <summary>
        /// Определяет тип для пяти соединений
        /// </summary>
        private static string DetermineFiveConnectionType(List<BlockFacing> facings)
        {
            // Находим отсутствующую сторону
            BlockFacing missingSide = null;
            for (int i = 0; i < 6; i++)
            {
                BlockFacing facing = BlockFacing.ALLFACES[i];
                if (!facings.Contains(facing))
                {
                    missingSide = facing;
                    break;
                }
            }

            if (missingSide == null)
                return "five-n";

            // Возвращаем тип на основе отсутствующей стороны
            return missingSide.Code switch
            {
                "north" => "five-n", // Отсутствует север
                "east" => "five-e",  // Отсутствует восток
                "south" => "five-s", // Отсутствует юг
                "west" => "five-w",  // Отсутствует запад
                "up" => "five-u",    // Отсутствует верх
                "down" => "five-d",  // Отсутствует низ
                _ => "five-n"
            };
        }

        public void BreakConnection(BlockFacing side)
        {
            int index = side.Index;
            connectedSides[index] = false;
            connectedPipes[index] = null;
            connectedToInventory[index] = false;

            // Обновляем модель после разрыва соединения
            UpdateBlockModel();

            MarkDirty();
        }

        /// <summary>
        /// Получает список всех блоков с инвентарем, соединенных с этой трубой
        /// </summary>
        public List<BlockPos> GetConnectedInventories()
        {
            List<BlockPos> inventories = [];

            for (int i = 0; i < 6; i++)
            {
                if (connectedSides[i] && connectedToInventory[i] && connectedPipes[i] != null)
                {
                    inventories.Add((BlockPos)connectedPipes[i]);
                }
            }

            return inventories;
        }

        /// <summary>
        /// Получает инвентарь из подключенной позиции
        /// </summary>
        public IInventory GetConnectedInventory(BlockPos inventoryPos)
        {
            return GetInventoryAtPosition(inventoryPos);
        }

        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);
            UpdateConnections();
        }

        public override void OnBlockRemoved()
        {
            // Разрываем соединения с соседями
            for (int i = 0; i < 6; i++)
            {
                if (connectedSides[i] && connectedPipes[i] != null && !connectedToInventory[i])
                {
                    BreakNeighborConnection(connectedPipes[i]!, BlockFacing.ALLFACES[i]);
                }
            }

            // Удаляем трубу из сети
            networkManager?.RemovePipe(Pos);

            base.OnBlockRemoved();
        }

        private void BreakNeighborConnection(BlockPos neighborPos, BlockFacing direction)
        {
            if (Api.World.BlockAccessor.GetBlockEntity(neighborPos) is BlockEntityPipeBase neighborPipe)
            {
                neighborPipe.BreakConnection(direction.Opposite);
            }
        }

        /// <summary>
        /// Отображает информацию о соединениях
        /// </summary>
        public virtual void GetPipeBlockInfo(StringBuilder sb)
        {
            int connections = 0;
            int inventoryConnections = 0;

            for (int i = 0; i < 6; i++)
            {
                if (connectedSides[i])
                {
                    connections++;
                    if (connectedToInventory[i]) inventoryConnections++;
                }
            }

            sb.AppendLine(Lang.Get("electricalprogressivetransport:connections", connections));

            if (inventoryConnections > 0)
            {
                sb.AppendLine(Lang.Get("electricalprogressivetransport:inventory-connections", inventoryConnections));
            }

            if (networkManager != null)
            {
                var network = networkManager.GetNetwork(Pos);
                if (network != null)
                {
                    sb.AppendLine(Lang.Get("electricalprogressivetransport:network-size", network.Pipes.Count));
                    sb.AppendLine(Lang.Get("electricalprogressivetransport:inserters", network.Inserters.Count));
                }
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            // Загружаем соединения
            byte[] connBytes = tree.GetBytes("connections", null);
            if (connBytes != null && connBytes.Length == 6)
            {
                for (int i = 0; i < 6; i++)
                {
                    connectedSides[i] = connBytes[i] == 1;
                }
            }

            // Загружаем информацию о соединениях с инвентарем
            byte[] invConnBytes = tree.GetBytes("inventoryConnections", null);
            if (invConnBytes != null && invConnBytes.Length == 6)
            {
                for (int i = 0; i < 6; i++)
                {
                    connectedToInventory[i] = invConnBytes[i] == 1;
                }
            }

            // Загружаем текущий тип трубы
            currentPipeType = tree.GetString("currentPipeType", "cross");
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            // Сохраняем соединения
            byte[] connBytes = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                connBytes[i] = (byte)(connectedSides[i] ? 1 : 0);
            }
            tree.SetBytes("connections", connBytes);

            // Сохраняем информацию о соединениях с инвентарем
            byte[] invConnBytes = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                invConnBytes[i] = (byte)(connectedToInventory[i] ? 1 : 0);
            }
            tree.SetBytes("inventoryConnections", invConnBytes);

            // Сохраняем текущий тип трубы
            tree.SetString("currentPipeType", currentPipeType);
        }

        /// <summary>
        /// Метод для получения информации о блоке
        /// </summary>
        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            base.GetBlockInfo(forPlayer, sb);
            GetPipeBlockInfo(sb);
        }
    }
}