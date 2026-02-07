using ElectricalProgressive.Utils;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EHammer;

public class BlockEHammer : Vintagestory.API.Common.Block
{

   


    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection? blockSel)
    {
        if (blockSel is null)
            return false;

        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        blockSel.Block = this;

        var blockEntity = world.BlockAccessor.GetBlockEntity(blockSel.Position);
        if (blockEntity is null)
            return true;

        if (blockEntity is BlockEntityOpenableContainer openableContainer)
            openableContainer.OnPlayerRightClick(byPlayer, blockSel);

        return true;
    }


    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        var blockCode = CodeWithVariant("side", "north");

        var block = world.BlockAccessor.GetBlock(blockCode);
        return new(block);
    }


    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return [OnPickBlock(world, pos)];
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
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " + MyMiniLib.GetAttributeInt(inSlot.Itemstack.Block, "voltage", 0) + " " + Lang.Get("electricalprogressivebasics:V"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:Consumption") + ": " + MyMiniLib.GetAttributeFloat(inSlot.Itemstack.Block, "maxConsumption", 0) + " " + Lang.Get("electricalprogressivebasics:W"));
        dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " + ((MyMiniLib.GetAttributeBool(inSlot.Itemstack.Block, "isolatedEnvironment", false)) ? Lang.Get("electricalprogressivebasics:Yes") : Lang.Get("electricalprogressivebasics:No")));
    }



}