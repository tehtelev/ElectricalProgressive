using ElectricalProgressive.Content.NetworkPipe;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content
{
    public class PipeConnectionComponent
    {
        private readonly BlockEntity _owner;
        private readonly ICoreAPI _api;
        private readonly BlockPos _pos;
        public readonly PipeNetworkManager _networkManager;

        private readonly bool[] _connectedSides = new bool[6];
        private readonly BlockPos?[] _connectedPipes = new BlockPos?[6];
        private readonly bool[] _connectedToInventory = new bool[6];
        private string _currentPipeType = "cross";

        public event Action<string> OnPipeTypeChanged;

        private bool _isUpdating = false;

        public bool[] ConnectedSides => _connectedSides;
        public bool[] ConnectedToInventory => _connectedToInventory;
        public BlockPos?[] ConnectedPipes => _connectedPipes;

        // Ключевые слова для определения блоков с инвентарём по коду блока
        private static string[] inventoryKeywords =
        [
            "chest", "crate", "box", "barrel", "shelf",
            "hopper", "funnel", "container", "storage",
            "cabinet", "drawer", "bin", "basket", "bag",
            "vessel", "pot", "jar", "tub", "tank",
            "mill", "quern", "press", "forge", "crucible",
            "machine", "machinebase", "generator", "machinerack"
        ];

        // Соседние стороны для определения угловых соединений (горизонтальные)
        private static Dictionary<string, string[]> adjacency = new()
        {
            { "north", ["west", "east"] },
            { "east", ["north", "south"] },
            { "south", ["east", "west"] },
            { "west", ["south", "north"] }
        };

        public PipeConnectionComponent(BlockEntity owner, ICoreAPI api, BlockPos pos)
        {
            _owner = owner;
            _api = api;
            _pos = pos;
            _networkManager = ElectricalProgressiveTransport.Instance?.GetNetworkManager();
        }

        /// <summary>
        /// Добавляет трубу в сеть и обновляет соединения
        /// </summary>
        public void Initialize()
        {
            _networkManager?.AddPipe(_pos, _owner);
            UpdateConnections();
        }

        /// <summary>
        /// Обновляет все соединения трубы с соседями
        /// </summary>
        /// <param name="updateNeighbors">Если true, уведомляет соседей об обновлении</param>
        public virtual void UpdateConnections(bool updateNeighbors = true)
        {
            if (_isUpdating)
                return; // Защита от бесконечной рекурсии
            _isUpdating = true;

            try
            {
                // 1. Сброс и поиск соединений
                for (int i = 0; i < 6; i++)
                {
                    _connectedSides[i] = false;
                    _connectedPipes[i] = null;
                    _connectedToInventory[i] = false;
                }

                for (int i = 0; i < 6; i++)
                {
                    BlockFacing facing = BlockFacing.ALLFACES[i];
                    BlockPos checkPos = _pos.AddCopy(facing);
                    var neighborBlock = _api.World.BlockAccessor.GetBlock(checkPos);

                    if (IsPipeBlock(neighborBlock))
                    {
                        _connectedSides[i] = true;
                        _connectedPipes[i] = checkPos.Copy();
                    }
                    else if (HasValidInventoryBlock(checkPos))
                    {
                        _connectedSides[i] = true;
                        _connectedPipes[i] = checkPos.Copy();
                        _connectedToInventory[i] = true;
                    }
                }

                // 2. Обновление визуальной модели самой трубы
                UpdateBlockModel();
                _owner.MarkDirty();

                // 3. Уведомление соседей после собственного обновления
                if (updateNeighbors)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        if (_connectedSides[i] && !_connectedToInventory[i] && _connectedPipes[i] != null)
                        {
                            NotifyNeighborOfUpdate(_connectedPipes[i]);
                        }
                    }
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }

        /// <summary>
        /// Уведомляет соседнюю трубу об обновлении без рекурсивного уведомления её соседей
        /// </summary>
        private void NotifyNeighborOfUpdate(BlockPos neighborPos)
        {
            var be = _api.World.BlockAccessor.GetBlockEntity(neighborPos);
            if (be is BlockEntityPipeBase neighborPipe)
            {
                neighborPipe.UpdateConnections(false);
            }
            else if (be is BEPipe neighborSimplePipe)
            {
                neighborSimplePipe.UpdateConnections(false);
            }
        }

        /// <summary>
        /// Проверяет, является ли блок трубой
        /// </summary>
        protected virtual bool IsPipeBlock(Vintagestory.API.Common.Block block)
        {
            if (block == null)
                return false;
            string code = block.Code?.ToString() ?? "";
            return code.Contains("pipe") || block is BlockPipeBase;
        }

        /// <summary>
        /// Проверяет, содержит ли блок в данной позиции инвентарь
        /// </summary>
        protected virtual bool HasValidInventoryBlock(BlockPos pos)
        {
            if (_api == null)
                return false;

            try
            {
                var block = _api.World.BlockAccessor.GetBlock(pos);
                if (block == null)
                    return false;

                // Проверка через BlockEntityContainer
                var container = block.GetBlockEntity<BlockEntityContainer>(pos);
                if (container?.Inventory?.Count > 0)
                    return true;

                var blockEntity = _api.World.BlockAccessor.GetBlockEntity(pos);
                if (blockEntity != null)
                {
                    if (blockEntity is BlockEntityContainer bec && bec.Inventory?.Count > 0)
                        return true;
                    if (blockEntity is IBlockEntityContainer ibec && ibec.Inventory?.Count > 0)
                        return true;
                    if (blockEntity is IInventory inv && inv.Count > 0)
                        return true;

                    // Рефлексивная проверка свойства Inventory
                    try
                    {
                        var prop = blockEntity.GetType().GetProperty("Inventory");
                        if (prop?.GetValue(blockEntity) is IInventory invProp && invProp.Count > 0)
                            return true;
                    }
                    catch { }
                }

                // Проверка по ключевым словам в коде блока
                string code = block.Code?.ToString() ?? "";
                foreach (var keyword in inventoryKeywords)
                {
                    if (code.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _api?.Logger?.Error($"Ошибка при проверке инвентаря в позиции {pos}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Обновляет соединение с соседом через конкретную сторону
        /// </summary>
        private void UpdateNeighborConnection(BlockPos neighborPos, BlockFacing fromDirection)
        {
            if (_api.World.BlockAccessor.GetBlockEntity(neighborPos) is BlockEntityPipeBase neighborPipe)
            {
                neighborPipe.UpdateSingleConnection(fromDirection, _pos, false);
            }
            else if (_api.World.BlockAccessor.GetBlockEntity(neighborPos) is BEPipe neighborSimplePipe)
            {
                neighborSimplePipe.UpdateSingleConnection(fromDirection, _pos, false);
            }
        }

        /// <summary>
        /// Обновляет одно соединение (вызывается соседом при изменении его состояния)
        /// </summary>
        public void UpdateSingleConnection(BlockFacing side, BlockPos fromPos, bool fromInventory = false)
        {
            int index = side.Index;
            _connectedSides[index] = true;
            _connectedPipes[index] = fromPos.Copy();
            _connectedToInventory[index] = fromInventory;

            UpdateBlockModel();
            _owner.MarkDirty();
        }

        /// <summary>
        /// Разрывает соединение с указанной стороны
        /// </summary>
        public void BreakConnection(BlockFacing side)
        {
            int index = side.Index;
            _connectedSides[index] = false;
            _connectedPipes[index] = null;
            _connectedToInventory[index] = false;

            UpdateBlockModel();
            _owner.MarkDirty();
        }

        /// <summary>
        /// Вызывается при удалении трубы для очистки из сети
        /// </summary>
        public virtual void OnPipeRemoved()
        {
            _networkManager?.RemovePipe(_pos);
        }

        /// <summary>
        /// Определяет текущий тип визуальной модели трубы на основе соединений
        /// </summary>
        public virtual void UpdateBlockModel()
        {
            if (_api == null || _api.Side != EnumAppSide.Server)
                return;

            var connectedFacings = new List<BlockFacing>();
            for (int i = 0; i < 6; i++)
            {
                if (_connectedSides[i])
                    connectedFacings.Add(BlockFacing.ALLFACES[i]);
            }

            string newPipeType = DeterminePipeType(connectedFacings);
            if (newPipeType != _currentPipeType)
            {
                _currentPipeType = newPipeType;
                OnPipeTypeChanged?.Invoke(newPipeType);
                UpdateVisualBlockType(newPipeType);
            }
        }

        /// <summary>
        /// Обновляет визуальный тип блока на основе типа трубы
        /// </summary>
        protected virtual void UpdateVisualBlockType(string pipeType)
        {
            if (_api == null || _api.Side != EnumAppSide.Server)
                return;

            var currentBlock = _api.World.BlockAccessor.GetBlock(_pos);
            if (currentBlock == null)
                return;

            string baseBlockCode = GetBaseBlockCode();
            if (string.IsNullOrEmpty(baseBlockCode))
                return;

            string newBlockCodeString = $"electricalprogressivetransport:{baseBlockCode}-{pipeType}";
            var newBlock = _api.World.GetBlock(new AssetLocation(newBlockCodeString));
            if (newBlock == null)
            {
                //Api.Logger.Error($"Блок не найден: {newBlockCodeString}");
                return;
            }

            if (newBlock.Id != currentBlock.Id)
            {
                var tree = new TreeAttribute();
                _owner.ToTreeAttributes(tree);

                _api.World.BlockAccessor.ExchangeBlock(newBlock.BlockId, _pos);
                var newEntity = _api.World.BlockAccessor.GetBlockEntity(_pos);
                if (newEntity is BlockEntity newBe)
                {
                    newBe.FromTreeAttributes(tree, _api.World);
                    newBe.MarkDirty();
                }

                _api.World.BlockAccessor.MarkBlockDirty(_pos);
            }
        }

        /// <summary>
        /// Получает базовый код блока без вариантов (для построения кода визуального типа)
        /// </summary>
        public string GetBaseBlockCode()
        {
            var currentBlock = _api.World.BlockAccessor.GetBlock(_pos);
            if (currentBlock == null)
                return null;

            // Подсчёт количества частей в вариантах для удаления их из кода
            int partsToRemove = 0;
            foreach (var variantValue in currentBlock.Variant.Values)
            {
                partsToRemove += variantValue.Split('-').Length;
            }

            return currentBlock.CodeWithoutParts(partsToRemove);
        }

        /// <summary>
        /// Определяет тип трубы на основе списка подключённых сторон
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

        private static string DetermineSingleConnectionType(BlockFacing facing) => facing.Axis switch
        {
            EnumAxis.X => "straight-ew",
            EnumAxis.Z => "straight-ns",
            EnumAxis.Y => "straight-ud",
            _ => "cross"
        };

        /// <summary>
        /// Определяет тип трубы при двух соединениях (прямая или угол)
        /// </summary>
        private static string DetermineTwoConnectionType(BlockFacing f1, BlockFacing f2)
        {
            var sorted = new[] { f1, f2 }.OrderBy(f => f.Index).ToList();
            f1 = sorted[0];
            f2 = sorted[1];

            if (f1.Opposite == f2)
            {
                if (f1.Axis == EnumAxis.Z) return "straight-ns";
                if (f1.Axis == EnumAxis.X) return "straight-ew";
                if (f1.Axis == EnumAxis.Y) return "straight-ud";
            }

            return (f1.Code, f2.Code) switch
            {
                ("north", "east") => "corner-ne",
                ("east", "south") => "corner-se",
                ("south", "west") => "corner-sw",
                ("north", "west") => "corner-nw",
                ("north", "up") => "corner-nu",
                ("south", "up") => "corner-su",
                ("east", "up") => "corner-eu",
                ("west", "up") => "corner-wu",
                ("north", "down") => "corner-nd",
                ("south", "down") => "corner-sd",
                ("east", "down") => "corner-ed",
                ("west", "down") => "corner-wd",
                _ => "cross"
            };
        }

        /// <summary>
        /// Определяет тип трубы при трёх соединениях (тройник, угол или вертикальное)
        /// </summary>
        private static string DetermineThreeConnectionType(List<BlockFacing> facings)
        {
            if (IsTripleCorner(facings))
                return DetermineTripleCornerType(facings);
            if (IsTeeConnection(facings))
                return DetermineTeeType(facings);

            return DetermineThreePlusVerticalType(facings);
        }

        /// <summary>
        /// Проверяет, является ли соединение тройным углом (3 оси без противоположных)
        /// </summary>
        private static bool IsTripleCorner(List<BlockFacing> facings)
        {
            if (facings.Count != 3)
                return false;
            foreach (var f in facings)
                if (facings.Contains(f.Opposite))
                    return false;
            return facings.Select(f => f.Axis).Distinct().Count() == 3;
        }

        /// <summary>
        /// Определяет тип тройного угла по кодам сторон
        /// </summary>
        private static string DetermineTripleCornerType(List<BlockFacing> facings)
        {
            var codes = facings.Select(f => f.Code).OrderBy(c => c).ToList();
            string key = string.Join("-", codes);
            return key switch
            {
                "east-north-up" => "triple-neu",
                "down-east-north" => "triple-ned",
                "east-south-up" => "triple-seu",
                "down-east-south" => "triple-sed",
                "south-up-west" => "triple-swu",
                "down-south-west" => "triple-swd",
                "north-up-west" => "triple-nwu",
                "down-north-west" => "triple-nwd",
                _ => "cross"
            };
        }

        /// <summary>
        /// Проверяет, является ли соединение Т-образным (есть противоположные стороны)
        /// </summary>
        private static bool IsTeeConnection(List<BlockFacing> facings)
        {
            if (facings.Count != 3)
                return false;
            foreach (var f in facings)
                if (facings.Contains(f.Opposite))
                    return true;
            return false;
        }

        /// <summary>
        /// Определяет тип Т-образного соединения по кодам сторон
        /// </summary>
        private static string DetermineTeeType(List<BlockFacing> facings)
        {
            var codes = facings.Select(f => f.Code).OrderBy(c => c).ToList();
            string key = string.Join("-", codes);
            return key switch
            {
                "east-north-south" => "tee-w",
                "north-south-west" => "tee-e",
                "east-north-west" => "tee-n",
                "east-south-west" => "tee-s",
                "north-south-up" => "tee-un",
                "down-north-south" => "tee-dn",
                "east-up-west" => "tee-uw",
                "down-east-west" => "tee-dw",
                "down-east-up" => "tee-eh",
                "down-north-up" => "tee-nh",
                "down-south-up" => "tee-sh",
                "down-up-west" => "tee-wh",
                _ => "cross"
            };
        }

        /// <summary>
        /// Определяет тип трубы при 3 горизонтальных + 1 вертикальном соединении
        /// </summary>
        private static string DetermineThreePlusVerticalType(List<BlockFacing> facings)
        {
            var horizontal = facings.Where(f => f.Axis != EnumAxis.Y).ToList();
            var vertical = facings.Where(f => f.Axis == EnumAxis.Y).ToList();
            if (horizontal.Count == 3 && vertical.Count == 1)
            {
                var allHorizontal = new[] { "north", "east", "south", "west" };
                var present = horizontal.Select(f => f.Code).ToList();
                string missing = allHorizontal.FirstOrDefault(c => !present.Contains(c));
                string vertCode = vertical[0].Code;
                return vertCode == "up" ? "four-d" : "four-u";
            }
            return "four-n";
        }

        /// <summary>
        /// Определяет тип трубы при четырёх соединениях
        /// </summary>
        private static string DetermineFourConnectionType(List<BlockFacing> facings)
        {
            if (facings.Count != 4)
                return "four-n";

            var missingSides = new List<BlockFacing>();
            for (int i = 0; i < 6; i++)
            {
                var f = BlockFacing.ALLFACES[i];
                if (!facings.Contains(f)) missingSides.Add(f);
            }
            if (missingSides.Count != 2) return "four-n";

            var m1 = missingSides[0];
            var m2 = missingSides[1];

            // Обе пропущенные стороны горизонтальные
            if (m1.Axis != EnumAxis.Y && m2.Axis != EnumAxis.Y)
            {
                if (AreAdjacentHorizontal(m1, m2))
                {
                    return (m1.Code, m2.Code) switch
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
                    bool hasUp = facings.Contains(BlockFacing.UP);
                    bool hasDown = facings.Contains(BlockFacing.DOWN);
                    if (hasUp && hasDown)
                        return m1.Axis == EnumAxis.X ? "four-u" : "four-v";
                    if (hasUp) return "four-u";
                    if (hasDown) return "four-d";
                    return "cross";
                }
            }

            // Одна горизонтальная, одна вертикальная
            if ((m1.Axis != EnumAxis.Y && m2.Axis == EnumAxis.Y) || (m1.Axis == EnumAxis.Y && m2.Axis != EnumAxis.Y))
            {
                var horiz = m1.Axis != EnumAxis.Y ? m1 : m2;
                var vert = m1.Axis == EnumAxis.Y ? m1 : m2;
                return vert.Code switch
                {
                    "up" => horiz.Code switch
                    {
                        "north" => "four-nu",
                        "east" => "four-eu",
                        "south" => "four-su",
                        "west" => "four-wu",
                        _ => "four-nu"
                    },
                    "down" => horiz.Code switch
                    {
                        "north" => "four-nd",
                        "east" => "four-ed",
                        "south" => "four-sd",
                        "west" => "four-wd",
                        _ => "four-nd"
                    },
                    _ => "four-n"
                };
            }

            // Обе пропущенные стороны вертикальные
            if (m1.Code == "up" && m2.Code == "down")
            {
                int horizontalCount = facings.Count(f => f.Axis != EnumAxis.Y);
                if (horizontalCount == 4) return "four-n";
                if (horizontalCount == 3)
                {
                    var allHorizontal = new[] { "north", "east", "south", "west" };
                    var present = facings.Where(f => f.Axis != EnumAxis.Y).Select(f => f.Code).ToList();
                    string missing = allHorizontal.FirstOrDefault(c => !present.Contains(c));
                    return missing switch
                    {
                        "north" => "four-n",
                        "east" => "four-e",
                        "south" => "four-s",
                        "west" => "four-w",
                        _ => "four-n"
                    };
                }
            }
            return "four-n";
        }

        /// <summary>
        /// Проверяет, являются ли две горизонтальные стороны соседними
        /// </summary>
        private static bool AreAdjacentHorizontal(BlockFacing f1, BlockFacing f2)
        {
            return adjacency.ContainsKey(f1.Code) && adjacency[f1.Code].Contains(f2.Code);
        }

        /// <summary>
        /// Определяет тип трубы при пяти соединениях (по пропущенной стороне)
        /// </summary>
        private static string DetermineFiveConnectionType(List<BlockFacing> facings)
        {
            for (int i = 0; i < 6; i++)
            {
                var f = BlockFacing.ALLFACES[i];
                if (!facings.Contains(f))
                    return f.Code switch
                    {
                        "north" => "five-n",
                        "east" => "five-e",
                        "south" => "five-s",
                        "west" => "five-w",
                        "up" => "five-u",
                        "down" => "five-d",
                        _ => "five-n"
                    };
            }
            return "five-n";
        }

        /// <summary>
        /// Получает список позиций подключённых инвентарей
        /// </summary>
        public List<BlockPos> GetConnectedInventories()
        {
            var result = new List<BlockPos>();
            for (int i = 0; i < 6; i++)
                if (_connectedSides[i] && _connectedToInventory[i] && _connectedPipes[i] != null)
                    result.Add((BlockPos)_connectedPipes[i]);
            return result;
        }

        /// <summary>
        /// Получает инвентарь по позиции (публичный интерфейс)
        /// </summary>
        public IInventory GetConnectedInventory(BlockPos inventoryPos)
        {
            return GetInventoryAtPosition(inventoryPos);
        }

        /// <summary>
        /// Получает инвентарь из блока в указанной позиции
        /// </summary>
        public IInventory GetInventoryAtPosition(BlockPos pos)
        {
            if (_api == null)
                return null;
            var block = _api.World.BlockAccessor.GetBlock(pos);
            var container = block?.GetBlockEntity<BlockEntityContainer>(pos);
            if (container?.Inventory != null)
                return container.Inventory;
            var blockEntity = _api.World.BlockAccessor.GetBlockEntity(pos);
            return GetInventoryFromBlockEntity(blockEntity);
        }

        /// <summary>
        /// Получает инвентарь из BlockEntity через проверку типов и рефлексию
        /// </summary>
        public static IInventory GetInventoryFromBlockEntity(BlockEntity be)
        {
            if (be == null)
                return null;
            if (be is BlockEntityContainer container)
                return container.Inventory;
            if (be is IBlockEntityContainer icon)
                return icon.Inventory;
            if (be is IInventory inv)
                return inv;
            try
            {
                var prop = be.GetType().GetProperty("Inventory");
                return prop?.GetValue(be) as IInventory;
            }
            catch { return null; }
        }

        /// <summary>
        /// Восстанавливает состояние компонента из атрибутов дерева (серийлизация)
        /// </summary>
        public void FromTreeAttributes(ITreeAttribute tree)
        {
            var connBytes = tree.GetBytes("connections", null);
            if (connBytes != null && connBytes.Length == 6)
                for (int i = 0; i < 6; i++)
                    _connectedSides[i] = connBytes[i] == 1;

            var invConnBytes = tree.GetBytes("inventoryConnections", null);
            if (invConnBytes != null && invConnBytes.Length == 6)
                for (int i = 0; i < 6; i++)
                    _connectedToInventory[i] = invConnBytes[i] == 1;

            _currentPipeType = tree.GetString("currentPipeType", "cross");
        }

        /// <summary>
        /// Сохраняет состояние компонента в атрибуты дерева (серийлизация)
        /// </summary>
        public void ToTreeAttributes(ITreeAttribute tree)
        {
            var connBytes = new byte[6];
            for (int i = 0; i < 6; i++) connBytes[i] = (byte)(_connectedSides[i] ? 1 : 0);
            tree.SetBytes("connections", connBytes);

            var invConnBytes = new byte[6];
            for (int i = 0; i < 6; i++) invConnBytes[i] = (byte)(_connectedToInventory[i] ? 1 : 0);
            tree.SetBytes("inventoryConnections", invConnBytes);

            tree.SetString("currentPipeType", _currentPipeType);
        }

        /// <summary>
        /// Добавляет информацию о трубе в StringBuilder при наведении курсора
        /// </summary>
        public void GetBlockInfo(StringBuilder sb)
        {
            int connections = 0;
            int inventoryConnections = 0;
            for (int i = 0; i < 6; i++)
            {
                if (_connectedSides[i])
                {
                    connections++;
                    if (_connectedToInventory[i])
                        inventoryConnections++;
                }
            }

            sb.AppendLine(Lang.Get("electricalprogressivetransport:connections", connections));
            if (inventoryConnections > 0)
                sb.AppendLine(Lang.Get("electricalprogressivetransport:inventory-connections", inventoryConnections));

            if (_networkManager != null)
            {
                var network = _networkManager.GetNetwork(_pos);
                if (network != null)
                {
                    sb.AppendLine(Lang.Get("electricalprogressivetransport:network-size", network.Pipes.Count));
                    sb.AppendLine(Lang.Get("electricalprogressivetransport:inserters", network.Inserters.Count));
                }
            }
        }
    }
}