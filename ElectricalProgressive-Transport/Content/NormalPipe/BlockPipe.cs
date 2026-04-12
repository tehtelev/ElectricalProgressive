using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.NormalPipe;

/// <summary>
/// Блок для кроссового соединения труб в моде Electrical Progressive
/// </summary>
public class BlockPipe : BlockPipeBase
{
    /// <summary>
    /// Получает предмет при добыче блока (крестовина)
    /// </summary>
    /// <param name="world">Доступ к миру</param>
    /// <param name="pos">Позиция блока</param>
    /// <returns>Предмет для выдачи или null, если блок недействителен</returns>
    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        // Получаем сущность блока
        var be = world.BlockAccessor.GetBlockEntity(pos) as BEPipe;

        // Проверяем существование сущности
        if (be == null)
            return null;

        // Формируем код кроссового блока на основе базового кода трубы
        var blockCode = "electricalprogressivetransport:" + be.GetBaseBlockCode() + "-cross";

        // Получаем блок по коду
        var block = world.BlockAccessor.GetBlock(blockCode);

        // Возвращаем новый предмет с блоком
        return new ItemStack(block);
    }

    /// <summary>
    /// Получает предметы, выпадающие при добыче блока
    /// </summary>
    /// <param name="world">Доступ к миру</param>
    /// <param name="pos">Позиция блока</param>
    /// <param name="byPlayer">Игрок, который добывает блок</param>
    /// <param name="dropQuantityMultiplier">Множитель количества выпадающих предметов</param>
    /// <returns>Массив предметов для выдачи</returns>
    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        // Возвращаем результат добычи (кроссовая труба)
        return new[] { OnPickBlock(world, pos) };
    }

    /// <summary>
    /// Получает информацию о блоке для отображения в подсказке (tooltip)
    /// </summary>
    /// <param name="world">Доступ к миру</param>
    /// <param name="pos">Позиция блока</param>
    /// <param name="forPlayer">Игрок, которому показывается подсказка</param>
    /// <returns>Текст для отображения в интерфейсе</returns>
    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        // Создаём строковый буфер для формирования текста подсказки
        var sb = new StringBuilder();

        // Получаем сущность блока
        var pipe = world.BlockAccessor.GetBlockEntity(pos) as BEPipe;

        // Если сущность существует, получаем информацию о трубе
        if (pipe != null)
        {
            pipe.GetBlockInfo(forPlayer, sb);
        }

        return sb.ToString();
    }
}