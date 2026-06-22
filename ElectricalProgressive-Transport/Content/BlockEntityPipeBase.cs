using ElectricalProgressive.Content;
using ElectricalProgressive.Content.NetworkPipe;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

public class BlockEntityPipeBase : BlockEntityGenericTypedContainer, IPipeRenderState
{
    private PipeConnectionComponent _pipeConnection;

    public bool[] ConnectedSides => _pipeConnection?.ConnectedSides;
    public bool[] ConnectedToInventory => _pipeConnection?.ConnectedToInventory;
    public BlockPos?[] ConnectedPipes => _pipeConnection?.ConnectedPipes;
    public bool UseInserterHead => true;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        _pipeConnection = new PipeConnectionComponent(this, api, Pos);
        _pipeConnection.Initialize();
    }

    public virtual void UpdateConnections(bool updateNeighbors = true)
        => _pipeConnection?.UpdateConnections(updateNeighbors);

    public void UpdateSingleConnection(BlockFacing side, BlockPos fromPos, bool fromInventory = false)
        => _pipeConnection?.UpdateSingleConnection(side, fromPos, fromInventory);

    public PipeNetworkManager NetworkManager => _pipeConnection._networkManager;

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
        _pipeConnection?.FromTreeAttributes(tree);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        _pipeConnection?.ToTreeAttributes(tree);
    }

    public override void OnBlockPlaced(ItemStack byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);
        UpdateConnections(updateNeighbors: true);
    }

    public override void OnBlockRemoved()
    {
        List<BlockPos> neighborsToUpdate = new List<BlockPos>();

        if (_pipeConnection != null)
        {
            for (int i = 0; i < 6; i++)
            {
                if (_pipeConnection.ConnectedSides[i] && !_pipeConnection.ConnectedToInventory[i])
                    neighborsToUpdate.Add(_pipeConnection.ConnectedPipes[i].Copy());
            }
        }

        _pipeConnection.OnPipeRemoved();
        base.OnBlockRemoved();

        foreach (var pos in neighborsToUpdate)
        {
            var be = Api.World.BlockAccessor.GetBlockEntity(pos);

            if (be is BEPipe pipe)
                pipe.UpdateConnections(updateNeighbors: false);
            else if (be is BlockEntityPipeBase pipe2)
                pipe2.UpdateConnections(updateNeighbors: false);
        }
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        _pipeConnection?.GetBlockInfo(dsc);
    }

    public virtual string GetBaseBlockCode()
        => Block?.Code?.ToString();
}