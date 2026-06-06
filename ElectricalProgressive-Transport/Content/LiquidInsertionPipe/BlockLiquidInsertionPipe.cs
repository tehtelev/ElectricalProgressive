// BlockLiquidInsertionPipe.cs
// ================================================
// Блок-труба для фильтрации жидкостей
// Обеспечивает интерфейс взаимодействия с трубой и логику выбора жидкостей

using ElectricalProgressive.Content.ItemInsertionPipe;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.LiquidInsertionPipe;

public class BlockLiquidInsertionPipe : BlockPipeBase
{
    /// <summary>
    /// Получает список действий при взаимодействии с размещенным блоком.
    /// Добавляет опцию фильтров жидкостей для правой кнопки мыши.
    /// </summary>
    /// <param name="world">Доступ к миру</param>
    /// <param name="selection">Выбор блока игроком</param>
    /// <param name="forPlayer">Игрок, взаимодействующий с блоком</param>
    /// <returns>Массив WorldInteraction для отображения подсказок</returns>
    public override WorldInteraction[] GetPlacedBlockInteractionHelp(
        IWorldAccessor world,
        BlockSelection selection,
        IPlayer forPlayer)
    {
        base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);

        // Добавляем действие для правой кнопки мыши - настройки фильтров жидкостей
        return new WorldInteraction[]
        {
            new WorldInteraction()
            {
                ActionLangCode = Lang.Get("electricalprogressivetransport:blockhelp-liquid-filter-settings"),
                MouseButton = EnumMouseButton.Right,
            },

        }.Append<WorldInteraction>(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));// Прикладываем базовые подсказки к действию фильтра жидкостей
    }

    /// <summary>
    /// Обработка выбора блока (пик) - извлекает жидкость из трубы.
    /// </summary>
    /// <param name="world">Доступ к миру</param>
    /// <param name="pos">Позиция выбранного блока</param>
    /// <returns>ItemStack с жидкостью или null если нет содержимого</returns>
    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        // Получаем блок-сущность трубы
        var be = world.BlockAccessor.GetBlockEntity(pos) as BELiquidInsertionPipe;

        if (be == null)
            return null;

        // Формируем код блока для получения - добавляем суффикс "-cross"
        var blockCode = be.GetBaseBlockCode() + "-cross";

        // Получаем блок по коду
        var block = world.BlockAccessor.GetBlock(blockCode);

        return new(block);
    }

    /// <summary>
    /// Получает выпадающие предметы при разрушении блока.
    /// </summary>
    /// <param name="world">Доступ к миру</param>
    /// <param name="pos">Позиция блока</param>
    /// <param name="byPlayer">Игрок, разрушающий блок</param>
    /// <param name="dropQuantityMultiplier">Мультипликатор количества выпадающих предметов</param>
    /// <returns>Массив ItemStack с выпадением</returns>
    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return [OnPickBlock(world, pos)];
    }

    /// <summary>
    /// Обработка начала взаимодействия с блоком (например, при клике правой кнопкой мыши).
    /// </summary>
    /// <param name="world">Доступ к миру</param>
    /// <param name="byPlayer">Игрок, взаимодействующий с блоком</param>
    /// <param name="blockSel">Выбор блока игроком</param>
    /// <returns>true если взаимодействие обработано</returns>
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        // Проверяем, что позиция выбрана и это труба с жидкостью
        if (blockSel.Position == null ||
            world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BELiquidInsertionPipe be)
        {
            return false;
        }

        bool handled = base.OnBlockInteractStart(world, byPlayer, blockSel);

        // Если базовое взаимодействие не обработано и позиция выбрана - обрабатываем как фильтр жидкостей
        if (!handled && blockSel.Position != null)
        {
            return true;
        }

        return true;
    }

    /// <summary>
    /// Получает информацию о блоке для отображения в подсказке.
    /// </summary>
    /// <param name="world">Доступ к миру</param>
    /// <param name="pos">Позиция блока</param>
    /// <param name="forPlayer">Игрок, запрашивающий информацию</param>
    /// <returns>Строка с информацией о блоке</returns>
    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        var sb = new StringBuilder();

        // Получаем блок-сущность трубы
        var pipe = world.BlockAccessor.GetBlockEntity(pos) as BELiquidInsertionPipe;

        // Запрашиваем информацию у самой сущности
        pipe?.GetBlockInfo(forPlayer, sb);

        return sb.ToString();
    }
}