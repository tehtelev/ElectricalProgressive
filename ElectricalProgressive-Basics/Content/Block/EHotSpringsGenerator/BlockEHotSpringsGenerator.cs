using ElectricalProgressive.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EHotSpringsGenerator;

public class BlockEHotSpringsGenerator : BlockEBase
{
    /// <summary>
    /// Checks if the block can be placed
    /// </summary>
    /// <param name="world"></paramf>
    /// <param name="byPlayer"></param>
    /// <param name="itemstack"></param>
    /// <param name="blockSel"></param>
    /// <param name="failureCode"></param>
    /// <returns></returns>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack,
       BlockSelection blockSel, ref string failureCode)
    {
        var selection = new Selection(blockSel);
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
            FacingHelper.Faces(facing).First() is { } blockFacing &&
            !world.BlockAccessor
                .GetBlock(blockSel.Position.AddCopy(blockFacing)).SideSolid[blockFacing.Opposite.Index])
        {
            return false;
        }

        return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }




    /// <summary>
    /// Place the block
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
            world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityEHotSpringsGenerator entity
        )
        {
            entity.Facing = facing;
            LoadEProperties.Load(this, entity);

            return true;
        }

        return false;
    }




    /// <summary>
    /// Handler for neighbor block changes
    /// </summary>
    /// <param name="world"></param>
    /// <param name="pos"></param>
    /// <param name="neibpos"></param>
    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);

        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityEHotSpringsGenerator)
        {

            if (!world.BlockAccessor.GetBlock(pos.AddCopy(BlockFacing.DOWN)).SideSolid[4]) // if the block below is no longer solid
            {
                world.BlockAccessor.BreakBlock(pos, null);
            }
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
    /// Get held item info for inventory
    /// </summary>
    /// <param name="inSlot"></param>
    /// <param name="dsc"></param>
    /// <param name="world"></param>
    /// <param name="withDebugInfo"></param>
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " + MyMiniLib.GetAttributeInt(inSlot.Itemstack.Block, "voltage", 0) + " " + Lang.Get("electricalprogressivebasics:V"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " + ((MyMiniLib.GetAttributeBool(inSlot.Itemstack.Block, "isolatedEnvironment", false)) ? Lang.Get("electricalprogressivebasics:Yes") : Lang.Get("electricalprogressivebasics:No")));
    }
}
