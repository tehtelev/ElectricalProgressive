using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.ItemInsertionPipe;

public class BlockItemInsertionPipe : BlockPipeBase
{

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
    /// Обработка начала взаимодействия с блоком (например, при клике правой кнопкой мыши)
    /// </summary>
    /// <param name="world"></param>
    /// <param name="byPlayer"></param>
    /// <param name="blockSel"></param>
    /// <returns></returns>
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {

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


    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {

        var be= world.BlockAccessor.GetBlockEntity(pos) as BEItemInsertionPipe;
        if (be == null)
            return null;

        var blockCode = be.GetBaseBlockCode() + "-cross";

        var block = world.BlockAccessor.GetBlock(blockCode);

        return new(block);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return [OnPickBlock(world, pos)];
    }



    /// <summary>
    /// Получение информации о блоке для отображения в подсказке
    /// </summary>
    /// <param name="world"></param>
    /// <param name="pos"></param>
    /// <param name="forPlayer"></param>
    /// <returns></returns>
    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        StringBuilder sb = new StringBuilder();
        var pipe = world.BlockAccessor.GetBlockEntity(pos) as BEItemInsertionPipe;
        if (pipe != null)
        {
            pipe.GetBlockInfo(forPlayer, sb);
        }

        return sb.ToString();
    }
}