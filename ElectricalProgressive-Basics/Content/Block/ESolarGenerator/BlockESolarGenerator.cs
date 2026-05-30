using ElectricalProgressive.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ESolarGenerator;

public class BlockESolarGenerator : BlockEBase
{
    /// <summary>
    /// Проверка возможности установки блока
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
        //неваляжка - только вертикально
        // целая ли грань, на которую ставим
        if (!MyMiniLib.CheckSolidFace(world.BlockAccessor, blockSel.Position, Facing.DownAll))
        {
            return false;
        }

        return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }




    /// <summary>
    /// ставим блок
    /// </summary>
    /// <param name="world"></param>
    /// <param name="byPlayer"></param>
    /// <param name="blockSel"></param>
    /// <param name="byItemStack"></param>
    /// <returns></returns>
    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
        ItemStack byItemStack)
    {
        var selection = new Selection(blockSel);
        
        // Disallow stacking on the same block type
        var belowPos = blockSel.Position.DownCopy();
        var belowBlock = world.BlockAccessor.GetBlock(belowPos);

        if (belowBlock == this)
        {
            return false;
        }


        var facing = Facing.None;

        try
        {
            facing = FacingHelper.From(selection.Face, selection.Direction);
        }
        catch
        {
            return false;
        }

        if (
            base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack) &&
            world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityESolarGenerator entity
        )
        {
            entity.Facing = facing;
            LoadEProperties.Load(this, entity);
            
            return true;
        }

        return false;
    }




    /// <summary>
    /// Обработчик изменения соседнего блока
    /// </summary>
    /// <param name="world"></param>
    /// <param name="pos"></param>
    /// <param name="neibpos"></param>
    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);

        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityESolarGenerator entity)
        {
            // целая ли грань еще
            if (MyMiniLib.CheckSolidFace(world.BlockAccessor, pos, entity.Facing))
            {
                return;
            }

            // иначе ломаем
            world.BlockAccessor.BreakBlock(pos, null);
        }
    }

   

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer,
        float dropQuantityMultiplier = 1)
    {
        return [OnPickBlock(world, pos)];
    }


    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        var newState = Variant["state"] switch
        {
            "on" => "off",
            "off" => "off",
            _ => "off" // Обработка всех остальных случаев (например, пустая строка)
        };

        var blockCode = CodeWithVariants(new Dictionary<string, string>
        {
            { "state", newState },
            { "side", "south" }
        });

        var block = world.BlockAccessor.GetBlock(blockCode);
        return new ItemStack(block);
    }


    /// <summary>
    /// Получение информации о предмете в инвентаре
    /// </summary>
    /// <param name="inSlot"></param>
    /// <param name="dsc"></param>
    /// <param name="world"></param>
    /// <param name="withDebugInfo"></param>
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        var block = inSlot.Itemstack?.Block;
        if (block == null)
            return;

        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " + MyMiniLib.GetAttributeInt(block, "voltage", 0) + " " + Lang.Get("electricalprogressivebasics:V"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " + ((MyMiniLib.GetAttributeBool(block, "isolatedEnvironment", false)) ? Lang.Get("electricalprogressivebasics:Yes") : Lang.Get("electricalprogressivebasics:No")));
    }
}