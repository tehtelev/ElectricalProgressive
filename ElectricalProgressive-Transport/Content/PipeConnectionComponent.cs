using ElectricalProgressive.Content.NetworkPipe;
using System;
using System.Collections.Generic;
using System.Reflection;
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

        private bool _isUpdating;

        public bool[] ConnectedSides => _connectedSides;
        public bool[] ConnectedToInventory => _connectedToInventory;
        public BlockPos?[] ConnectedPipes => _connectedPipes;

        private static readonly string[] inventoryKeywords =
        [
            "chest", "crate", "box", "barrel", "shelf",
            "hopper", "funnel", "container", "storage",
            "cabinet", "drawer", "bin", "basket", "bag",
            "vessel", "pot", "jar", "tub", "tank",
            "mill", "quern", "press", "forge", "crucible",
            "machine", "machinebase", "generator", "machinerack"
        ];
        private static readonly Dictionary<Type, PropertyInfo?> inventoryPropertyCache = [];
        private static readonly object inventoryPropertyCacheLock = new();

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
                return;

            _isUpdating = true;

            try
            {
                bool[] oldConnectedSides = (bool[])_connectedSides.Clone();
                bool[] oldConnectedToInventory = (bool[])_connectedToInventory.Clone();

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

                bool connectionsChanged = !SameConnections(oldConnectedSides, oldConnectedToInventory);
                if (connectionsChanged)
                {
                    _owner.MarkDirty(true);
                    if (_api.Side == EnumAppSide.Server)
                        _api.World.BlockAccessor.MarkBlockDirty(_pos);

                    _networkManager?.RefreshNetworkCache(_pos);
                }
                else if (!updateNeighbors)
                {
                    _networkManager?.RefreshNetworkCache(_pos);
                }

                if (updateNeighbors)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        if (_connectedSides[i] && !_connectedToInventory[i] && _connectedPipes[i] != null)
                            NotifyNeighborOfUpdate(_connectedPipes[i]);
                    }
                }
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void NotifyNeighborOfUpdate(BlockPos neighborPos)
        {
            var be = _api.World.BlockAccessor.GetBlockEntity(neighborPos);
            if (be is BlockEntityPipeBase neighborPipe)
                neighborPipe.UpdateConnections(false);
            else if (be is BEPipe neighborSimplePipe)
                neighborSimplePipe.UpdateConnections(false);
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

                var blockEntity = _api.World.BlockAccessor.GetBlockEntity(pos);
                if (blockEntity != null)
                {
                    IInventory inventory = GetInventoryFromBlockEntity(blockEntity);
                    if (inventory?.Count > 0)
                        return true;
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

        public void UpdateSingleConnection(BlockFacing side, BlockPos fromPos, bool fromInventory = false)
        {
            int index = side.Index;
            bool changed = !_connectedSides[index]
                || _connectedToInventory[index] != fromInventory
                || _connectedPipes[index] == null
                || !_connectedPipes[index].Equals(fromPos);

            _connectedSides[index] = true;
            _connectedPipes[index] = fromPos.Copy();
            _connectedToInventory[index] = fromInventory;

            if (changed)
            {
                _owner.MarkDirty(true);
                if (_api.Side == EnumAppSide.Server)
                    _api.World.BlockAccessor.MarkBlockDirty(_pos);

                _networkManager?.RefreshNetworkCache(_pos);
            }
        }

        public void BreakConnection(BlockFacing side)
        {
            int index = side.Index;
            bool changed = _connectedSides[index] || _connectedPipes[index] != null || _connectedToInventory[index];

            _connectedSides[index] = false;
            _connectedPipes[index] = null;
            _connectedToInventory[index] = false;

            if (changed)
            {
                _owner.MarkDirty(true);
                if (_api.Side == EnumAppSide.Server)
                    _api.World.BlockAccessor.MarkBlockDirty(_pos);

                _networkManager?.RefreshNetworkCache(_pos);
            }
        }

        public virtual void OnPipeRemoved()
        {
            _networkManager?.RemovePipe(_pos);
        }

        private bool SameConnections(bool[] oldConnectedSides, bool[] oldConnectedToInventory)
        {
            for (int i = 0; i < 6; i++)
            {
                if (oldConnectedSides[i] != _connectedSides[i] ||
                    oldConnectedToInventory[i] != _connectedToInventory[i])
                {
                    return false;
                }
            }

            return true;
        }

        public List<BlockPos> GetConnectedInventories()
        {
            var result = new List<BlockPos>();
            for (int i = 0; i < 6; i++)
            {
                if (_connectedSides[i] && _connectedToInventory[i] && _connectedPipes[i] != null)
                    result.Add((BlockPos)_connectedPipes[i]);
            }

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
                var prop = GetInventoryProperty(be.GetType());
                return prop?.GetValue(be) as IInventory;
            }
            catch { return null; }
        }

        private static PropertyInfo? GetInventoryProperty(Type blockEntityType)
        {
            lock (inventoryPropertyCacheLock)
            {
                if (inventoryPropertyCache.TryGetValue(blockEntityType, out PropertyInfo? cached))
                    return cached;

                PropertyInfo? property = blockEntityType.GetProperty("Inventory");
                if (property != null && !typeof(IInventory).IsAssignableFrom(property.PropertyType))
                    property = null;

                inventoryPropertyCache[blockEntityType] = property;
                return property;
            }
        }

        public void FromTreeAttributes(ITreeAttribute tree)
        {
            var connBytes = tree.GetBytes("connections", null);
            if (connBytes != null && connBytes.Length == 6)
            {
                for (int i = 0; i < 6; i++)
                    _connectedSides[i] = connBytes[i] == 1;
            }

            var invConnBytes = tree.GetBytes("inventoryConnections", null);
            if (invConnBytes != null && invConnBytes.Length == 6)
            {
                for (int i = 0; i < 6; i++)
                    _connectedToInventory[i] = invConnBytes[i] == 1;
            }
        }

        public void ToTreeAttributes(ITreeAttribute tree)
        {
            var connBytes = new byte[6];
            for (int i = 0; i < 6; i++)
                connBytes[i] = (byte)(_connectedSides[i] ? 1 : 0);
            tree.SetBytes("connections", connBytes);

            var invConnBytes = new byte[6];
            for (int i = 0; i < 6; i++)
                invConnBytes[i] = (byte)(_connectedToInventory[i] ? 1 : 0);
            tree.SetBytes("inventoryConnections", invConnBytes);
        }

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
