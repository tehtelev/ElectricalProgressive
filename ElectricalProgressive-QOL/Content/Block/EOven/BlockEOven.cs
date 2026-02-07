using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.Block.EOven;

public class BlockEOven : BlockEBase
{
    private WorldInteraction[] _interactions = Array.Empty<WorldInteraction>();

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        if (api.Side != EnumAppSide.Client)
            return;

        var capi = api as ICoreClientAPI;
        _interactions = ObjectCacheUtil.GetOrCreate(api, "EOvenBlockInteractions", () =>
        {
            var rackableStacklist = new List<ItemStack>();

            foreach (var obj in api.World.Collectibles)
            {
                if (obj.Attributes?["bakingProperties"]?.AsObject<BakingProperties>() == null)
                    continue;
                var stacks = obj.GetHandBookStacks(capi);
                if (stacks != null) rackableStacklist.AddRange(stacks);
            }

            return new[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "blockhelp-oven-bakeable",
                    HotKeyCode = null,
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = rackableStacklist.ToArray(),
                },
                new WorldInteraction
                {
                    ActionLangCode = "blockhelp-toolrack-take",
                    HotKeyCode = null,
                    MouseButton = EnumMouseButton.Right,
                }
            };
        });
    }

    public override bool DoPartialSelection(IWorldAccessor world, BlockPos pos) => true;

    public override bool OnBlockInteractStart(
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection bs)
    {
        return world.BlockAccessor.GetBlockEntity(bs.Position) is BlockEntityEOven blockEntity
            ? blockEntity.OnInteract(byPlayer, bs)
            : base.OnBlockInteractStart(world, byPlayer, bs);
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return _interactions; // такой вариант самый производительный
    }



    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        var newState = this.Variant["state"] switch
        {
            "enabled" => "disabled",
            "disabled" => "disabled",
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