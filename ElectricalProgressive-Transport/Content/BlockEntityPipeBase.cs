using ElectricalProgressive.Content;
using ElectricalProgressive.Content.NetworkPipe;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

/// <summary>
/// Базовый класс для сущностей труб в моде Electrical Progressive Transport
/// Обеспечивает подключение к соседним блокам и управление сетью труб
/// </summary>
public class BlockEntityPipeBase : BlockEntityGenericTypedContainer, IPipeRenderState
{
    /// <summary>
    /// Компонент управления соединениями труб
    /// </summary>
    private PipeConnectionComponent _pipeConnection;

    #region Публичные свойства (информация о подключениях)

    /// <summary>
    /// Массив, указывающий подключённые стороны (true = подключено)
    /// </summary>
    public bool[] ConnectedSides => _pipeConnection?.ConnectedSides;

    /// <summary>
    /// Массив, указывающий подключение к инвентарям (true = инвентарь)
    /// </summary>
    public bool[] ConnectedToInventory => _pipeConnection?.ConnectedToInventory;

    /// <summary>
    /// Позиции подключённых труб (null если не подключено)
    /// </summary>
    public BlockPos?[] ConnectedPipes => _pipeConnection?.ConnectedPipes;

    public string CurrentPipeType => _pipeConnection?.CurrentPipeType ?? "cross";

    #endregion

    /// <summary>
    /// Инициализация сущности трубы при загрузке блока
    /// </summary>
    /// <param name="api">Интерфейс ядра игры</param>
    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        // Создаём компонент управления соединениями
        _pipeConnection = new PipeConnectionComponent(this, api, Pos);
        _pipeConnection.Initialize();
    }



    /// <summary>
    /// Обновляет все подключения трубы (по умолчанию обновляет соседей)
    /// </summary>
    /// <param name="updateNeighbors">Обновлять ли состояние соседних блоков</param>
    public virtual void UpdateConnections(bool updateNeighbors = true)
        => _pipeConnection?.UpdateConnections(updateNeighbors);

    /// <summary>
    /// Обновляет одно конкретное подключение трубы
    /// </summary>
    /// <param name="side">Сторона блока для обновления</param>
    /// <param name="fromPos">Позиция блока, от которого идёт подключение</param>
    /// <param name="fromInventory">Подключён ли объект к инвентарю</param>
    public void UpdateSingleConnection(BlockFacing side, BlockPos fromPos, bool fromInventory = false)
        => _pipeConnection?.UpdateSingleConnection(side, fromPos, fromInventory);

    /// <summary>
    /// Перерисовывает модель блока в зависимости от подключений
    /// </summary>
    public void UpdateBlockModel()
        => _pipeConnection?.UpdateBlockModel();



    /// <summary>
    /// Менеджер сети труб (для управления потоками, логикой и т.д.)
    /// </summary>
    public PipeNetworkManager NetworkManager => _pipeConnection._networkManager;

    /// <summary>
    /// Разрывает соединение на указанной стороне
    /// </summary>
    /// <param name="side">Сторона блока для разрыва соединения</param>
    public void BreakConnection(BlockFacing side)
        => _pipeConnection?.BreakConnection(side);



    /// <summary>
    /// Получает список позиций всех подключённых инвентарей
    /// </summary>
    /// <returns>Список позиций инвентарей</returns>
    public List<BlockPos> GetConnectedInventories()
        => _pipeConnection?.GetConnectedInventories() ?? new List<BlockPos>();

    /// <summary>
    /// Получает конкретный подключённый инвентарь по позиции
    /// </summary>
    /// <param name="inventoryPos">Позиция инвентаря</param>
    /// <returns>Объект инвентаря или null</returns>
    public IInventory GetConnectedInventory(BlockPos inventoryPos)
        => _pipeConnection?.GetConnectedInventory(inventoryPos);

    /// <summary>
    /// Получает инвентарь по указанной позиции в мире
    /// </summary>
    /// <param name="pos">Позиция для поиска</param>
    /// <returns>Объект инвентаря или null</returns>
    public IInventory GetInventoryAtPosition(BlockPos pos)
        => _pipeConnection?.GetInventoryAtPosition(pos);

    /// <summary>
    /// Статический метод для получения инвентаря из сущности блока
    /// </summary>
    /// <param name="be">Сущность блока</param>
    /// <returns>Объект инвентаря или null</returns>
    public static IInventory GetInventoryFromBlockEntity(BlockEntity be)
        => PipeConnectionComponent.GetInventoryFromBlockEntity(be);



    /// <summary>
    /// Загружает данные сущности из дерева атрибутов (при сохранении/загрузке мира)
    /// </summary>
    /// <param name="tree">Дерево атрибутов для загрузки</param>
    /// <param name="worldAccessForResolve">Доступ к миру для разрешения зависимостей</param>
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        _pipeConnection?.FromTreeAttributes(tree);
    }

    /// <summary>
    /// Сохраняет данные сущности в дерево атрибутов
    /// </summary>
    /// <param name="tree">Дерево атрибутов для сохранения</param>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        _pipeConnection?.ToTreeAttributes(tree);
    }



    /// <summary>
    /// Вызывается при установке трубы в мир
    /// </summary>
    /// <param name="byItemStack">Предмет, которым установлен блок</param>
    public override void OnBlockPlaced(ItemStack byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);

        // При установке обновляем себя и всех соседей
        UpdateConnections(updateNeighbors: true);
    }

    /// <summary>
    /// Вызывается при удалении трубы из мира
    /// </summary>
    public override void OnBlockRemoved()
    {
        // Список позиций соседей для уведомления о перерисовке
        List<BlockPos> neighborsToUpdate = new List<BlockPos>();

        // Сохраняем позиции подключённых труб (не инвентарей)
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

        // Сообщаем компоненту о удалении трубы
        _pipeConnection.OnPipeRemoved();

        // Удаляем сущность блока (вызывается базовый класс)
        base.OnBlockRemoved();

        // Оповещаем соседей, чтобы они перерисовали свои модели без этой трубы
        foreach (var pos in neighborsToUpdate)
        {
            var be = Api.World.BlockAccessor.GetBlockEntity(pos);

            if (be is BEPipe pipe)
                pipe.UpdateConnections(updateNeighbors: false);
            else if (be is BlockEntityPipeBase pipe2)
                pipe2.UpdateConnections(updateNeighbors: false);
        }
    }



    /// <summary>
    /// Разрывает соединение с соседним блоком по указанной стороне
    /// </summary>
    /// <param name="neighborPos">Позиция соседнего блока</param>
    /// <param name="direction">Направление к соседу (разрываем с противоположной стороны)</param>
    private void BreakNeighborConnection(BlockPos neighborPos, BlockFacing direction)
    {
        var neighbor = Api.World.BlockAccessor.GetBlockEntity(neighborPos);

        if (neighbor is BlockEntityPipeBase pipe)
            pipe.BreakConnection(direction.Opposite);
        else if (neighbor is BEPipe simplePipe)
            simplePipe.BreakConnection(direction.Opposite);
    }



    /// <summary>
    /// Получает информацию о блоке для отображения в подсказке при наведении
    /// </summary>
    /// <param name="forPlayer">Игрок, которому показывается информация</param>
    /// <param name="dsc">Строковый буфер для формирования текста подсказки</param>
    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        _pipeConnection?.GetBlockInfo(dsc);
    }



    /// <summary>
    /// Получает базовый код типа трубы (для определения вида кроссовины)
    /// </summary>
    /// <returns>Код базового блока</returns>
    public virtual string GetBaseBlockCode()
        => _pipeConnection?.GetBaseBlockCode();

   
}
