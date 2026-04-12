using ElectricalProgressive.Content;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

public class BEPipe : BlockEntity
{
    private PipeConnectionComponent _pipeConnection;

    /// <summary>
    /// Статус подключений по сторонам блока.
    /// </summary>
    public bool[] ConnectedSides => _pipeConnection?.ConnectedSides;


    /// <summary>
    /// Инициализация блок-сущности при создании или загрузке.
    /// </summary>
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        _pipeConnection = new PipeConnectionComponent(this, api, Pos);
        _pipeConnection.Initialize();
    }



    /// <summary>
    /// Обновляет подключение для конкретной стороны.
    /// </summary>
    public void UpdateSingleConnection(BlockFacing side, BlockPos fromPos, bool fromInventory = false)
        => _pipeConnection?.UpdateSingleConnection(side, fromPos, fromInventory);

    /// <summary>
    /// Принудительно разрывает соединение на указанной стороне.
    /// </summary>
    public void BreakConnection(BlockFacing side) 
        => _pipeConnection?.BreakConnection(side);



    /// <summary>
    /// Восстановление состояния из сериализованных данных дерева атрибутов.
    /// </summary>
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        _pipeConnection?.FromTreeAttributes(tree);
    }

    /// <summary>
    /// Сохранение состояния в атрибуты дерева для сериализации.
    /// </summary>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        _pipeConnection?.ToTreeAttributes(tree);
    }

    /// <summary>
    /// Обновляет состояние соединений блока и оповещает соседей.
    /// </summary>
    public void UpdateConnections(bool updateNeighbors = true)
        => _pipeConnection?.UpdateConnections(updateNeighbors);

    /// <summary>
    /// Вызывается при размещении нового блока в мире.
    /// </summary>
    public override void OnBlockPlaced(ItemStack byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);
        // При установке пересчитываем соединения локально и у соседей
        UpdateConnections(true);
    }

    /// <summary>
    /// Вызывается при удалении блока из мира.
    /// </summary>
    public override void OnBlockRemoved()
    {
        // Сохраняем позиции соседей, которые могут нуждаться в обновлении
        List<BlockPos> neighborsToUpdate = new List<BlockPos>();

        if (_pipeConnection != null)
        {
            for (int i = 0; i < 6; i++)
            {
                // Проверяем наличие физического соединения с трубой, а не инвентарем
                if (_pipeConnection.ConnectedSides[i] && 
                    !_pipeConnection.ConnectedToInventory[i] && 
                    _pipeConnection.ConnectedPipes[i] != null)
                {
                    neighborsToUpdate.Add(_pipeConnection.ConnectedPipes[i].Copy());
                }
            }

            // Уведомляем компонент о удалении для внутреннего состояния
            _pipeConnection.OnPipeRemoved();
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

    /// <summary>
    /// Возвращает код базового блока для логики соединения.
    /// </summary>
    public virtual string GetBaseBlockCode() => _pipeConnection?.GetBaseBlockCode();

    /// <summary>
    /// Отображает информацию о блоке в интерфейсе (при наведении курсора).
    /// </summary>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        _pipeConnection?.GetBlockInfo(dsc);
    }
}