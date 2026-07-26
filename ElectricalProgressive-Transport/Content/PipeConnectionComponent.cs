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
        // Scratch buffers — avoid allocating Clone() arrays on every connection scan.
        private readonly bool[] _prevSides = new bool[6];
        private readonly bool[] _prevInventory = new bool[6];

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
            // Notify loaded neighbors so chunk-border pipes re-link after world/chunk load.
            // Local-only scan left the far side disconnected until a manual block update.
            UpdateConnections(updateNeighbors: true);
        }

        public virtual void UpdateConnections(bool updateNeighbors = true, bool forceEndpointRefresh = false)
        {
            if (_isUpdating)
                return;

            _isUpdating = true;

            try
            {
                for (int i = 0; i < 6; i++)
                {
                    _prevSides[i] = _connectedSides[i];
                    _prevInventory[i] = _connectedToInventory[i];
                    _connectedSides[i] = false;
                    _connectedPipes[i] = null;
                    _connectedToInventory[i] = false;
                }

                for (int i = 0; i < 6; i++)
                {
                    BlockFacing facing = BlockFacing.ALLFACES[i];
                    BlockPos checkPos = _pos.AddCopy(facing);

                    // Unloaded neighbor chunk: GetBlock is air — keep previous/saved link until chunk loads.
                    if (!IsChunkLoaded(checkPos))
                    {
                        if (_prevSides[i])
                        {
                            _connectedSides[i] = true;
                            _connectedToInventory[i] = _prevInventory[i];
                            _connectedPipes[i] = checkPos.Copy();
                        }

                        continue;
                    }

                    var neighborBlock = _api.World.BlockAccessor.GetBlock(checkPos);

                    if (IsPipeBlock(neighborBlock))
                    {
                        _connectedSides[i] = true;
                        _connectedPipes[i] = checkPos.Copy();
                    }
                    else if (HasValidInventoryBlock(checkPos, neighborBlock))
                    {
                        _connectedSides[i] = true;
                        _connectedPipes[i] = checkPos.Copy();
                        _connectedToInventory[i] = true;
                    }
                }

                bool connectionsChanged = !SameConnections(_prevSides, _prevInventory);
                if (connectionsChanged)
                {
                    _owner.MarkDirty(true);
                    // Client + server: retesselate arms when border pipe re-links after chunk load.
                    _api.World.BlockAccessor.MarkBlockDirty(_pos);

                    _networkManager?.RefreshNetworkCache(_pos);
                }
                else if (forceEndpointRefresh)
                {
                    // Wrench type-swap: flags may match but inserter/source role changed.
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

        /// <summary>
        /// True when the chunk containing pos is available. Unloaded border cells look like air.
        /// </summary>
        private bool IsChunkLoaded(BlockPos pos)
        {
            try
            {
                return _api.World.BlockAccessor.GetChunkAtBlockPos(pos) != null;
            }
            catch
            {
                return false;
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

        protected virtual bool HasValidInventoryBlock(BlockPos pos, Vintagestory.API.Common.Block? block = null)
        {
            if (_api == null)
                return false;

            try
            {
                block ??= _api.World.BlockAccessor.GetBlock(pos);
                if (block == null || block.Id == 0)
                    return false;

                // Most solid terrain has no entity class — skip BE lookup.
                string entityClass = block.EntityClass;
                if (!string.IsNullOrEmpty(entityClass))
                {
                    var blockEntity = _api.World.BlockAccessor.GetBlockEntity(pos);
                    if (blockEntity != null)
                    {
                        IInventory inventory = GetInventoryFromBlockEntity(blockEntity);
                        if (inventory?.Count > 0)
                            return true;
                    }
                }

                // Keyword fallback for blocks that act as containers without a standard BE inventory.
                string code = block.Code?.Path ?? "";
                if (code.Length == 0)
                    return false;

                for (int k = 0; k < inventoryKeywords.Length; k++)
                {
                    if (code.Contains(inventoryKeywords[k], StringComparison.OrdinalIgnoreCase))
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
                _api.World.BlockAccessor.MarkBlockDirty(_pos);
                _networkManager?.RefreshNetworkCache(_pos);
            }
        }

        public virtual void OnPipeRemoved()
        {
            // Pass caller so same-cell wrench replace does not unregister the new pipe BE.
            _networkManager?.RemovePipe(_pos, _owner);
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
            // Prefer live scan after load/transform. Only apply saved flags when present AND
            // leave positions for a subsequent UpdateConnections to overwrite with world truth.
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

            // Tree only stores flags — rebuild neighbour positions so pathfinding/endpoints work.
            for (int i = 0; i < 6; i++)
            {
                _connectedPipes[i] = _connectedSides[i]
                    ? _pos.AddCopy(BlockFacing.ALLFACES[i])
                    : null;
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
