using ElectricalProgressive.Content;
using ElectricalProgressive.Content.ItemInsertionPipe;
using ElectricalProgressive.Content.LiquidInsertionPipe;
using ElectricalProgressive.Content.NetworkPipe;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

/// <summary>
/// Lightweight base for filter/insertion pipes — plain BlockEntity, not GenericTypedContainer.
/// Filter inventory is owned by subclasses and serialized as a small tree attribute.
/// </summary>
public abstract class BlockEntityPipeBase : BlockEntity, IPipeRenderState
{
    private PipeConnectionComponent _pipeConnection;
    /// <summary>
    /// FromTreeAttributes runs before Initialize, when _pipeConnection is still null.
    /// Stash tree so connections can be restored when the component is created.
    /// </summary>
    private ITreeAttribute? _pendingConnectionTree;

    /// <summary>Player UID of who placed (or first configured) this pipe — used for claim-aware transfers.</summary>
    public string? OwnerUid { get; private set; }

    public bool[] ConnectedSides => _pipeConnection?.ConnectedSides;
    public bool[] ConnectedToInventory => _pipeConnection?.ConnectedToInventory;
    public BlockPos?[] ConnectedPipes => _pipeConnection?.ConnectedPipes;
    public bool UseInserterHead => true;

    /// <summary>Filter snapshot inventory (not a real storage container).</summary>
    public abstract InventoryBase Inventory { get; }

    /// <summary>Inventory id prefix for LateInitialize.</summary>
    public abstract string InventoryClassName { get; }

    public void SetOwnerIfEmpty(IPlayer? player)
    {
        if (player == null || string.IsNullOrEmpty(player.PlayerUID))
            return;
        if (!string.IsNullOrEmpty(OwnerUid))
            return;

        OwnerUid = player.PlayerUID;
    }

    public void ForceSetOwner(IPlayer? player)
    {
        if (player == null || string.IsNullOrEmpty(player.PlayerUID))
            return;

        if (OwnerUid == player.PlayerUID)
            return;

        OwnerUid = player.PlayerUID;
    }

    /// <summary>
    /// Owner only for wrench transform. Connections are always rebuilt live after SetBlock.
    /// </summary>
    public void WriteTransformState(ITreeAttribute tree)
    {
        if (!string.IsNullOrEmpty(OwnerUid))
            tree.SetString("ownerUid", OwnerUid);
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        EnsureInventoryInitialized(api);

        _pipeConnection = new PipeConnectionComponent(this, api, Pos);
        // Restore saved connection flags before first scan (chunk-border / load order).
        if (_pendingConnectionTree != null)
        {
            _pipeConnection.FromTreeAttributes(_pendingConnectionTree);
            _pendingConnectionTree = null;
        }

        _pipeConnection.Initialize();
    }

    /// <summary>Wire inventory to world after Pos/Api are available.</summary>
    protected void EnsureInventoryInitialized(ICoreAPI api)
    {
        if (Inventory == null || Pos == null || api == null)
            return;

        string invId = InventoryClassName + "-" + Pos;
        if (Inventory.InventoryID != invId || Inventory.Api == null)
            Inventory.LateInitialize(invId, api);

        Inventory.Pos = Pos;

        // Safe after LateInitialize — Api is set.
        try
        {
            Inventory.ResolveBlocksOrItems();
        }
        catch
        {
            // Already resolved in LoadFilterInventory when possible.
        }
    }

    public virtual void UpdateConnections(bool updateNeighbors = true, bool forceEndpointRefresh = false)
        => _pipeConnection?.UpdateConnections(updateNeighbors, forceEndpointRefresh);

    public void UpdateSingleConnection(BlockFacing side, BlockPos fromPos, bool fromInventory = false)
        => _pipeConnection?.UpdateSingleConnection(side, fromPos, fromInventory);

    public PipeNetworkManager NetworkManager => _pipeConnection?._networkManager;

    public void BreakConnection(BlockFacing side)
        => _pipeConnection?.BreakConnection(side);

    public List<BlockPos> GetConnectedInventories()
        => _pipeConnection?.GetConnectedInventories() ?? new List<BlockPos>();

    public IInventory GetConnectedInventory(BlockPos inventoryPos)
        => _pipeConnection?.GetConnectedInventory(inventoryPos);

    public IInventory GetInventoryAtPosition(BlockPos pos)
        => _pipeConnection?.GetInventoryAtPosition(pos);

    public static IInventory GetInventoryFromBlockEntity(BlockEntity be)
        => PipeConnectionComponent.GetInventoryFromBlockEntity(be);

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        if (_pipeConnection != null)
            _pipeConnection.FromTreeAttributes(tree);
        else if (tree != null)
            _pendingConnectionTree = tree;

        OwnerUid = tree?.GetString("ownerUid", null);
        if (string.IsNullOrEmpty(OwnerUid))
            OwnerUid = null;

        LoadFilterInventory(tree, worldAccessForResolve);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        _pipeConnection?.ToTreeAttributes(tree);

        if (!string.IsNullOrEmpty(OwnerUid))
            tree.SetString("ownerUid", OwnerUid);

        SaveFilterInventory(tree);
    }

    protected void LoadFilterInventory(ITreeAttribute tree, IWorldAccessor world)
    {
        if (Inventory == null || tree == null)
            return;

        if (tree["inventory"] is ITreeAttribute invTree)
            Inventory.FromTreeAttributes(invTree);

        // FromTreeAttributes runs before Initialize — Inventory.Api is still null.
        // InventoryBase.ResolveBlocksOrItems() uses Api.World and NREs (BE discarded on chunk load).
        // Resolve against the world accessor passed into FromTreeAttributes instead.
        if (world == null)
            return;

        for (int i = 0; i < Inventory.Count; i++)
        {
            ItemSlot slot = Inventory[i];
            if (slot?.Itemstack == null)
                continue;

            if (!slot.Itemstack.ResolveBlockOrItem(world))
                slot.Itemstack = null;
        }
    }

    protected void SaveFilterInventory(ITreeAttribute tree)
    {
        if (Inventory == null || tree == null)
            return;

        // Empty filters: write minimal tree (qslots only) — much cheaper than 18 empty slot nodes.
        if (IsFilterInventoryEmpty())
        {
            var empty = new TreeAttribute();
            empty.SetInt("qslots", Inventory.Count);
            tree["inventory"] = empty;
            return;
        }

        var invTree = new TreeAttribute();
        Inventory.ToTreeAttributes(invTree);
        tree["inventory"] = invTree;
    }

    protected virtual bool IsFilterInventoryEmpty()
    {
        if (Inventory == null)
            return true;

        for (int i = 0; i < Inventory.Count; i++)
        {
            if (!Inventory[i].Empty)
                return false;
        }

        return true;
    }

    public override void OnBlockPlaced(ItemStack byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);
        UpdateConnections(updateNeighbors: true);
    }

    /// <summary>
    /// Force live connection rebuild. Used after wrench transform.
    /// </summary>
    public void RefreshConnectionsAfterTransform()
    {
        NetworkManager?.ReregisterPipe(Pos, this);
        UpdateConnections(updateNeighbors: true, forceEndpointRefresh: true);
    }

    public override void OnBlockRemoved()
    {
        List<BlockPos> neighborsToUpdate = new List<BlockPos>();

        if (_pipeConnection != null)
        {
            for (int i = 0; i < 6; i++)
            {
                if (_pipeConnection.ConnectedSides[i] && !_pipeConnection.ConnectedToInventory[i]
                    && _pipeConnection.ConnectedPipes[i] != null)
                    neighborsToUpdate.Add(_pipeConnection.ConnectedPipes[i].Copy());
            }

            _pipeConnection.OnPipeRemoved();
        }

        // Drop filter snapshots — not real loot.
        Inventory?.DiscardAll();

        CancelClientTransitVisuals();

        base.OnBlockRemoved();

        if (Api?.World == null)
            return;

        foreach (var pos in neighborsToUpdate)
        {
            var be = Api.World.BlockAccessor.GetBlockEntity(pos);

            if (be is BEPipe pipe)
                pipe.UpdateConnections(updateNeighbors: false);
            else if (be is BlockEntityPipeBase pipe2)
                pipe2.UpdateConnections(updateNeighbors: false);
        }
    }

    /// <summary>Client: stop liquid/item flow meshes that still draw through this cell.</summary>
    protected void CancelClientTransitVisuals()
    {
        if (Api?.Side != EnumAppSide.Client || Pos == null)
            return;

        PipeLiquidTransitRenderer.Instance?.CancelTransitsThrough(Pos);
        PipeItemTransitRenderer.Instance?.CancelTransitsThrough(Pos);
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        _pipeConnection?.GetBlockInfo(dsc);
    }

    public virtual string GetBaseBlockCode()
        => Block?.Code?.ToString();
}
