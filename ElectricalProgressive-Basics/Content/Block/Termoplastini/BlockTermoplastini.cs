using ElectricalProgressive.Content.Block.ETermoGenerator;
using ElectricalProgressive.Utils;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.Termoplastini;

public class BlockTermoplastini : BlockEBase
{

    /// <summary>
    /// Проверка на возможность установки блока
    /// </summary>
    /// <param name="world"></param>
    /// <param name="byPlayer"></param>
    /// <param name="itemstack"></param>
    /// <param name="blockSel"></param>
    /// <param name="failureCode"></param>
    /// <returns></returns>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack,
        BlockSelection blockSel, ref string failureCode)
    {
        //проверка блок под блоком, на который мы ставим
        //должно ставиться нагенератор или термопластины
        var block = world.BlockAccessor.GetBlock(blockSel.Position.AddCopy(BlockFacing.DOWN));        
        if (block is not BlockETermoGenerator && block is not BlockTermoplastini)
            return false;

        // 11й блок не должен быть термопластиной
        var dawn10Pos = blockSel.Position.DownCopy(10);
        block = world.BlockAccessor.GetBlock(dawn10Pos);
        if (block is BlockTermoplastini)
            return false;

        return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }



    /// <summary>
    /// Соседний блок изменен
    /// </summary>
    /// <param name="world"></param>
    /// <param name="pos"></param>
    /// <param name="neibpos"></param>
    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);

        // если блок под термопластиной не термогенератор и не термопластина, то ломаем
        var block = world.BlockAccessor.GetBlock(pos.AddCopy(BlockFacing.DOWN));
        if (block is not BlockETermoGenerator && block is not BlockTermoplastini)
            world.BlockAccessor.BreakBlock(pos, null);


    }



}