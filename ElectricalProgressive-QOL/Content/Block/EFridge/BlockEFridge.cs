using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.Block.EFridge;

class BlockEFridge : BlockEBase
{
    private BlockEntityEFridge? _blockEntityEFridge;


    private WorldInteraction[] _interactions = Array.Empty<WorldInteraction>();

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        _interactions = ObjectCacheUtil.GetOrCreate(api, "fridgeBlockInteractions", () =>
        {
            return new[]
            {
                new WorldInteraction { ActionLangCode = "electricalprogressiveqol:freezer-over-help", MouseButton = EnumMouseButton.Right, }
            };
        });
    }


    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        _blockEntityEFridge = null!;
        if (blockSel.Position != null && world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityEFridge blockEntityEFreezer)
            _blockEntityEFridge = blockEntityEFreezer;

        var handled = base.OnBlockInteractStart(world, byPlayer, blockSel);
        if (!handled && blockSel.Position != null)
        {
            if (_blockEntityEFridge != null)
            {
                
               _blockEntityEFridge.OnBlockInteract(byPlayer, false, blockSel);
             
            }

            return true;
        }

        if (_blockEntityEFridge is null)
            return true;


        return true;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {

        var blockCode = CodeWithVariants(new()
        {
            { "side", "north" }
        });

        var block = world.BlockAccessor.GetBlock(blockCode);
        return new(block);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return [OnPickBlock(world, pos)];
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return _interactions; // такой вариант самый производительный
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