using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.Block.EFreezer;

class BlockEFreezer : BlockEBase
{
    private BlockEntityEFreezer? _blockEntityEFreezer;

    private WorldInteraction[] _interactions= [];

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        _interactions = ObjectCacheUtil.GetOrCreate(api, "freezerBlockInteractions", () =>
        {
            return new[]
            {
                new WorldInteraction { ActionLangCode = "electricalprogressiveqol:freezer-over-help", MouseButton = EnumMouseButton.Right, }
            };
        });
    }


    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        _blockEntityEFreezer = null!;
        if (blockSel.Position != null && world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityEFreezer blockEntityEFreezer)
            _blockEntityEFreezer = blockEntityEFreezer;

        var handled = base.OnBlockInteractStart(world, byPlayer, blockSel);
        if (!handled && blockSel.Position != null)
        {
            if (_blockEntityEFreezer != null)
            {
                
               _blockEntityEFreezer.OnBlockInteract(byPlayer, false, blockSel);
             
            }

            return true;
        }

        if (_blockEntityEFreezer is null)
            return true;


        return true;
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        var newState = this.Variant["state"] switch
        {
            "frozen" => "melted",
            "melted" => "melted",
            _ => "burned"
        };
        var blockCode = CodeWithVariants(new()
        {
            { "state", newState },
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

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);

        //проверяем только блок под нами
        // целая ли грань еще
        if (MyMiniLib.CheckSolidFace(world.BlockAccessor, pos, Facing.DownAll))
        {
            return;
        }

        // иначе ломаем
        world.BlockAccessor.BreakBlock(pos, null);

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