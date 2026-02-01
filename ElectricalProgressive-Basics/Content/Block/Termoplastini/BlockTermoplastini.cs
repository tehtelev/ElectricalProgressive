using ElectricalProgressive.Content.Block.ETermoGenerator;
using ElectricalProgressive.Utils;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.Termoplastini;

public class BlockTermoplastini : BlockEBase
{

    public static readonly Dictionary<BlockFacing, Vec3i?> varRotateOffset = new()
    {
        { BlockFacing.SOUTH, new Vec3i(1,0,0) },
        { BlockFacing.EAST, new Vec3i(0,0,-1) },
        { BlockFacing.NORTH, new Vec3i(-1,0,0) },
        { BlockFacing.WEST, new Vec3i(0,0,1) }
    };


    private BlockPos GetRealPosition(IWorldAccessor world, BlockPos pos)
    {
        Vintagestory.API.Common.Block block = world.BlockAccessor.GetBlock(pos);

        if (block is BlockMultiblock multiblock)
        {
            BlockPos controlPos = multiblock.GetControlBlockPos(pos);

            // Рекурсивно (на случай вложенных мультиблоков)
            return GetRealPosition(world, controlPos);
        }

        return pos;
    }

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
        //должно ставиться впритык к генератору или термопластинам
        
        var selection = new Selection(blockSel);

        var block = blockSel.Block;

        BlockFacing variant;
        BlockPos realPosition;

        if (block is BlockMultiblock)
        {
            variant = selection.Face;
        }
        else
        {
            realPosition = GetRealPosition(world, blockSel.Position);
            block = world.BlockAccessor.GetBlock(realPosition);

            // проверяем на какой блок мы навелись и тогда вычисляем на что смотреть
            if (block is BlockETermoGenerator || block is BlockTermoplastini)
            {
                variant = selection.Face;
            }
            else
            {
                if (block.Code.Path.Contains("air"))
                {
                    variant = selection.Face;
                }
                else
                    variant = selection.Direction;
            }
        }


        Vec3i? offset=new Vec3i(0,0,0); //стандартное смещение для главного блока
        // не главный блок? 
        if (block is BlockMultiblock multiblock)
        { 
            offset = multiblock.OffsetInv; // смещение относительно главного блока
        }
        else
        {
            block = world.BlockAccessor.GetBlock(blockSel.Position.AddCopy(variant));
            if (block is BlockMultiblock mb)
            {
                offset = mb.OffsetInv; // смещение относительно главного блока
            }
        }
        

        // получаем реальную координату, если это мультиблок
        realPosition = GetRealPosition(world, blockSel.Position.AddCopy(variant));

        block = world.BlockAccessor.GetBlock(realPosition);

        // должен быть термогенератор или термопластина
        if (block is not BlockETermoGenerator && block is not BlockTermoplastini)
            return false;

        
        if (block is BlockETermoGenerator)
            if (offset != varRotateOffset[variant]) 
                return false;

        if (block is BlockTermoplastini)
            if (offset != new Vec3i(0, 0, 0))
                return false;


        // 11й блок не должен быть термопластиной
        BlockPos check10Pos=null;

        if (variant== BlockFacing.NORTH)
            check10Pos = blockSel.Position.NorthCopy(10);
        else if (variant == BlockFacing.SOUTH)
            check10Pos = blockSel.Position.SouthCopy(10);
        else if (variant == BlockFacing.EAST)
            check10Pos = blockSel.Position.EastCopy(10);
        else if (variant == BlockFacing.WEST)
            check10Pos = blockSel.Position.WestCopy(10);
        else
            return false;

        block = world.BlockAccessor.GetBlock(check10Pos);
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

        // если блок около термопластины не термогенератор и не термопластина, то ломаем
        /*
        var variant = varRotate[this.Shape.rotateY];

        // получаем реальную координату блока
        var realPosition = GetRealPosition(world, pos.AddCopy(variant));

        var block = world.BlockAccessor.GetBlock(realPosition);
        if (block is not BlockETermoGenerator && block is not BlockTermoplastini)
            world.BlockAccessor.BreakBlock(pos, null);
        */
        
    }

 
}