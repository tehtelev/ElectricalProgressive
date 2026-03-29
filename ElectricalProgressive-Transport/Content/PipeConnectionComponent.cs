using ElectricalProgressive.Content.NetworkPipe;
using ElectricalProgressive.Content.NormalPipe;
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

        private static string[] inventoryKeywords =
        [
            "chest", "crate", "box", "barrel", "shelf",
            "hopper", "funnel", "container", "storage",
            "cabinet", "drawer", "bin", "basket", "bag",
            "vessel", "pot", "jar", "tub", "tank",
            "mill", "quern", "press", "forge", "crucible",
            "machine", "machinebase", "generator", "machinerack"
        ];

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

        public void Initialize()
        {
            _networkManager?.AddPipe(_pos, _owner);
            UpdateConnections();
        }




        public virtual void UpdateConnections(bool updateNeighbors = true)
        {
            if (_isUpdating)
                return; // Защита от бесконечной рекурсии
            _isUpdating = true;

            try
            {
                // 1. Сначала полностью сбрасываем и ищем соединения (как у вас и было)
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

                // 2. ВАЖНО: Сначала обновляем визуальную модель САМОЙ трубы
                UpdateBlockModel();
                _owner.MarkDirty();

                // 3. Только после того, как мы сами обновились, оповещаем соседей
                if (updateNeighbors)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        if (_connectedSides[i] && !_connectedToInventory[i] && _connectedPipes[i] != null)
                        {
                            // Вызываем ПОЛНОЕ обновление у соседа
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

        // Новый вспомогательный метод для уведомления соседа
        private void NotifyNeighborOfUpdate(BlockPos neighborPos)
        {
            var be = _api.World.BlockAccessor.GetBlockEntity(neighborPos);
            if (be is BlockEntityPipeBase neighborPipe)
            {
                // Вызываем обновление БЕЗ уведомления соседей (чтобы не вернуться к нам)
                neighborPipe.UpdateConnections(false);
            }
            else if (be is BEPipe neighborSimplePipe)
            {
                // Вызываем обновление БЕЗ уведомления соседей (чтобы не вернуться к нам)
                neighborSimplePipe.UpdateConnections(false);
            }

            // Если есть базовый класс для других труб, добавьте и его
        }

        protected virtual bool IsPipeBlock(Vintagestory.API.Common.Block block)
        {
            if (block == null)
                return false;
            string code = block.Code?.ToString() ?? "";
            return code.Contains("pipe") || block is BlockPipeBase;
        }

        protected virtual bool HasValidInventoryBlock(BlockPos pos)
        {
            if (_api == null)
                return false;

            try
            {
                var block = _api.World.BlockAccessor.GetBlock(pos);
                if (block == null)
                    return false;

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

                    try
                    {
                        var prop = blockEntity.GetType().GetProperty("Inventory");
                        if (prop?.GetValue(blockEntity) is IInventory invProp && invProp.Count > 0)
                            return true;
                    }
                    catch { }
                }

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

        public void UpdateSingleConnection(BlockFacing side, BlockPos fromPos, bool fromInventory = false)
        {
            int index = side.Index;
            _connectedSides[index] = true;
            _connectedPipes[index] = fromPos.Copy();
            _connectedToInventory[index] = fromInventory;

            UpdateBlockModel();
            _owner.MarkDirty();
        }

        public void BreakConnection(BlockFacing side)
        {
            int index = side.Index;
            _connectedSides[index] = false;
            _connectedPipes[index] = null;
            _connectedToInventory[index] = false;

            UpdateBlockModel();
            _owner.MarkDirty();
        }

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




        public string GetBaseBlockCode()
        {
            var currentBlock = _api.World.BlockAccessor.GetBlock(_pos);
            if (currentBlock == null)
                return null;
            // Считаем, сколько дефисов добавили варианты
            int partsToRemove = 0;
            foreach (var variantValue in currentBlock.Variant.Values)
            {
                // Считаем количество сегментов в значении варианта (например, "straight-ns" -> 2 сегмента)
                partsToRemove += variantValue.Split('-').Length;
            }

            // Отрезаем ровно столько частей, сколько пришло из вариантов
            return currentBlock.CodeWithoutParts(partsToRemove);
        }

        // Методы определения типа трубы (копируются из существующего кода, но могут быть вынесены в статический класс)
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

        private static string DetermineTwoConnectionType(BlockFacing f1, BlockFacing f2)
        {
            // Сортировка для единообразия
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

        private static string DetermineThreeConnectionType(List<BlockFacing> facings)
        {
            if (IsTripleCorner(facings))
                return DetermineTripleCornerType(facings);
            if (IsTeeConnection(facings))
                return DetermineTeeType(facings);

            return DetermineThreePlusVerticalType(facings);
        }

        private static bool IsTripleCorner(List<BlockFacing> facings)
        {
            if (facings.Count != 3)
                return false;
            foreach (var f in facings)
                if (facings.Contains(f.Opposite))
                    return false;
            return facings.Select(f => f.Axis).Distinct().Count() == 3;
        }

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

        private static bool IsTeeConnection(List<BlockFacing> facings)
        {
            if (facings.Count != 3)
                return false;
            foreach (var f in facings)
                if (facings.Contains(f.Opposite))
                    return true;
            return false;
        }

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
                return vertCode == "up" ? "four-d" : "four-u"; // временно
            }
            return "four-n";
        }

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
            // обе горизонтальные
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
            // одна горизонтальная, одна вертикальная
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
            // обе вертикальные
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

        private static bool AreAdjacentHorizontal(BlockFacing f1, BlockFacing f2)
        {
            return adjacency.ContainsKey(f1.Code) && adjacency[f1.Code].Contains(f2.Code);
        }

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

        public List<BlockPos> GetConnectedInventories()
        {
            var result = new List<BlockPos>();
            for (int i = 0; i < 6; i++)
                if (_connectedSides[i] && _connectedToInventory[i] && _connectedPipes[i] != null)
                    result.Add((BlockPos)_connectedPipes[i]);
            return result;
        }

        public IInventory GetConnectedInventory(BlockPos inventoryPos)
        {
            return GetInventoryAtPosition(inventoryPos);
        }

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
        /// Информация при навелении на блок трубы
        /// </summary>
        /// <param name="sb"></param>
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