using ElectricalProgressive.Content.Block;
using ElectricalProgressive.Content.Block.EGenerator;
using ElectricalProgressive.Content.Block.EMotor;
using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent.Mechanics;

namespace ElectricalProgressive.Content.Block.ETermoGenerator;

public class BlockETermoGenerator : BlockEBase
{



    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);
        
    }



    public override bool CanAttachBlockAt(IBlockAccessor blockAccessor, Vintagestory.API.Common.Block block, BlockPos pos,
        BlockFacing blockFace, Cuboidi attachmentArea = null!)
    {
        return true;
    }



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
        var selection = new Selection(blockSel);
        Facing facing = Facing.None;

        try
        {
            facing = FacingHelper.From(selection.Face, selection.Direction);
        }
        catch
        {
            return false;
        }


        if (
            FacingHelper.Faces(facing).First() is { } blockFacing &&
            !world.BlockAccessor
                .GetBlock(blockSel.Position.AddCopy(blockFacing)).SideSolid[blockFacing.Opposite.Index]
        )
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
        // если блок сгорел, то не ставим
        if (byItemStack.Block.Variant["type"] == "burned")
        {
            return false;
        }

        var selection = new Selection(blockSel);

        Facing facing = Facing.None;

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
            world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityETermoGenerator entity
        )
        {
            entity.Facing = facing;    //сообщаем направление

            //задаем параметры блока/проводника
            var voltage = MyMiniLib.GetAttributeInt(this, "voltage", 32);
            var maxCurrent = MyMiniLib.GetAttributeFloat(this, "maxCurrent", 5.0F);
            var isolated = MyMiniLib.GetAttributeBool(this, "isolated", false);
            var isolatedEnvironment = MyMiniLib.GetAttributeBool(this, "isolatedEnvironment", false);

            entity.Eparams = (
                new(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment),
                FacingHelper.Faces(facing).First().Index);


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

        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityETermoGenerator entity)
        {
            var faces = FacingHelper.Faces(entity.Facing).ToList();
            if (
            faces != null &&
            faces.Any() &&
            faces.First() is { } blockFacing &&
            !world.BlockAccessor.GetBlock(pos.AddCopy(blockFacing)).SideSolid[blockFacing.Opposite.Index]) //если блок под ним перестал быть сплошным
            {
                world.BlockAccessor.BreakBlock(pos, null);
            }
        }
    }





    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer,
        float dropQuantityMultiplier = 1)
    {
        return new[] { OnPickBlock(world, pos) };
    }



    /// <summary>
    /// Получение подсказок для взаимодействия с блоком
    /// </summary>
    /// <param name="world"></param>
    /// <param name="selection"></param>
    /// <param name="forPlayer"></param>
    /// <returns></returns>
    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return new WorldInteraction[]
        {
                new WorldInteraction()
                {
                    ActionLangCode = "blockhelp-door-openclose",
                    MouseButton = EnumMouseButton.Right
                }
        }.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
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
        dsc.AppendLine(Lang.Get("Voltage") + ": " + MyMiniLib.GetAttributeInt(inSlot.Itemstack.Block, "voltage", 0) + " " + Lang.Get("V"));
        dsc.AppendLine(Lang.Get("WResistance") + ": " + ((MyMiniLib.GetAttributeBool(inSlot.Itemstack.Block, "isolatedEnvironment", false)) ? Lang.Get("Yes") : Lang.Get("No")));
    }
}