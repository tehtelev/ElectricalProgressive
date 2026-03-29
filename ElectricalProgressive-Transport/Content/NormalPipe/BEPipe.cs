using ElectricalProgressive.Content;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

public class BEPipe : BlockEntity
{
    private PipeConnectionComponent _pipeConnection;

    public bool[] ConnectedSides => _pipeConnection?.ConnectedSides;
    public bool[] ConnectedToInventory => _pipeConnection?.ConnectedToInventory;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        _pipeConnection = new PipeConnectionComponent(this, api, Pos);
        _pipeConnection.Initialize();
    }

    // Делегирование методов
    public void UpdateSingleConnection(BlockFacing side, BlockPos fromPos, bool fromInventory = false)
        => _pipeConnection?.UpdateSingleConnection(side, fromPos, fromInventory);

    public void UpdateBlockModel() => _pipeConnection?.UpdateBlockModel();
    public void BreakConnection(BlockFacing side) => _pipeConnection?.BreakConnection(side);
    public List<BlockPos> GetConnectedInventories() => _pipeConnection?.GetConnectedInventories() ?? new List<BlockPos>();
    public IInventory GetConnectedInventory(BlockPos inventoryPos) => _pipeConnection?.GetConnectedInventory(inventoryPos);
    public IInventory GetInventoryAtPosition(BlockPos pos) => _pipeConnection?.GetInventoryAtPosition(pos);

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

    public void UpdateConnections(bool updateNeighbors = true)
        => _pipeConnection?.UpdateConnections(updateNeighbors);

    public override void OnBlockPlaced(ItemStack byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);
        // При установке обновляем себя и всех вокруг
        UpdateConnections(true);
    }

    public override void OnBlockRemoved()
    {
        // Перед удалением сохраняем позиции соседей
        List<BlockPos> neighborsToUpdate = new List<BlockPos>();
        if (_pipeConnection != null)
        {
            for (int i = 0; i < 6; i++)
            {
                if (_pipeConnection.ConnectedSides[i] && !_pipeConnection.ConnectedToInventory[i])
                {
                    neighborsToUpdate.Add(_pipeConnection.ConnectedPipes[i].Copy());
                }
            }
        }

        base.OnBlockRemoved();

        // Оповещаем бывших соседей, чтобы они перерисовали свои модели без этой трубы
        foreach (var pos in neighborsToUpdate)
        {
            var be = Api.World.BlockAccessor.GetBlockEntity(pos);
            if (be is BEPipe pipe)
                pipe.UpdateConnections(false);
            else if (be is BlockEntityPipeBase pipe2)
                pipe2.UpdateConnections(false);
        }
    }

    private void BreakNeighborConnection(BlockPos neighborPos, BlockFacing direction)
    {
        var neighbor = Api.World.BlockAccessor.GetBlockEntity(neighborPos);
        if (neighbor is BlockEntityPipeBase pipe) pipe.BreakConnection(direction.Opposite);
        else if (neighbor is BEPipe simplePipe) simplePipe.BreakConnection(direction.Opposite);
    }
    public virtual string GetBaseBlockCode() => _pipeConnection?.GetBaseBlockCode();

    // Информация о блоке при наведении на него
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        _pipeConnection?.GetBlockInfo(dsc);
    }
}