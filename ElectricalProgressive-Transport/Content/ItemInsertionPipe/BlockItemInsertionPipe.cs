using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.ItemInsertionPipe;

/// <summary>
/// Блок-обертка для фильтрующей трубы. Позволяет использовать её как обычный блок,
/// добавляя возможность установки через правую кнопку мыши (Help).
/// </summary>
public class BlockItemInsertionPipe : BlockPipeBase
{

    /// <summary>
    /// Получает подсказки при размещении блока.
    /// Добавляет действие "Настройки фильтра" для правой кнопки мыши.
    /// </summary>
    public override WorldInteraction[] GetPlacedBlockInteractionHelp(
        IWorldAccessor world,
        BlockSelection selection,
        IPlayer forPlayer)
    {
        base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        return new WorldInteraction[1]
        {
            new WorldInteraction()
            {
                ActionLangCode = Lang.Get("electricalprogressivetransport:blockhelp-filter-settings"),
                MouseButton = EnumMouseButton.Right,
            }
        }.Append<WorldInteraction>(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }


    /// <summary>
    /// Обработка начала взаимодействия с блоком (например, при клике правой кнопкой мыши).
    /// Если блок является фильтрующей трубой, перехватывает ввод для открытия настроек.
    /// </summary>
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        // Проверяем, что это именно наша труба и игрок на клиенте
        if (blockSel.Position == null || world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BEItemInsertionPipe be)
            return false;

        var handled = base.OnBlockInteractStart(world, byPlayer, blockSel);
        if (!handled && blockSel.Position != null)
        {
            return true;
        }

        if (be is null)
            return true;

        return true;
    }


    /// <summary>
    /// Получение предмета при клике на блок.
    /// Возвращает копию блока с модификатором "-cross".
    /// </summary>
    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        var be = world.BlockAccessor.GetBlockEntity(pos) as BEItemInsertionPipe;
        if (be == null)
            return null;

        var blockCode = be.GetBaseBlockCode() + "-cross";
        var block = world.BlockAccessor.GetBlock(blockCode);

        return new(block);
    }

    /// <summary>
    /// Список выпадающих предметов при ломании.
    /// </summary>
    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return [OnPickBlock(world, pos)];
    }



    /// <summary>
    /// Получение информации о блоке для отображения в подсказке.
    /// Вызывает метод из BEItemInsertionPipe.
    /// </summary>
    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        var sb = new StringBuilder();
        var pipe = world.BlockAccessor.GetBlockEntity(pos) as BEItemInsertionPipe;

        pipe?.GetBlockInfo(forPlayer, sb);

        return sb.ToString();
    }
}