using System.Collections.Generic;
using System.Text;
using ElectricalProgressive.Utils;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.Block.ECharger;

public class BlockECharger : BlockEBase
{
    private WorldInteraction[] _interactions = Array.Empty<WorldInteraction>();
    private int _output;

    public override void OnLoaded(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Client)
            return;

        var capi = api as ICoreClientAPI;


        _output = MyMiniLib.GetAttributeInt(this, "output", 1000);

        _interactions = ObjectCacheUtil.GetOrCreate(api, "chargerBlockInteractions", () =>
        {
            var rackableStacklist = new List<ItemStack>();

            foreach (var obj in api.World.Collectibles)
            {
                if (obj.Attributes?["chargable"].AsBool() != true)
                    continue;

                var stacks = obj.GetHandBookStacks(capi);
                if (stacks != null)
                    rackableStacklist.AddRange(stacks);
            }

            return new[] {
                new WorldInteraction
                {
                    ActionLangCode = "blockhelp-toolrack-place",
                    HotKeyCode = null,
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = rackableStacklist.ToArray()
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

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityECharger rack)
            return rack.OnPlayerInteract(byPlayer, blockSel.HitPosition);

        return false;
    }

    

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return _interactions; // такой вариант самый производительный
    }

    public override bool CanAttachBlockAt(IBlockAccessor blockAccessor, Vintagestory.API.Common.Block block, BlockPos pos, BlockFacing blockFace, Cuboidi attachmentArea = null!)
    {
        return true;
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
            { "side", "south" }
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
        dsc.AppendLine(Lang.Get("Voltage") + ": " + MyMiniLib.GetAttributeInt(inSlot.Itemstack.Block, "voltage", 0) + " " + Lang.Get("V"));
        dsc.AppendLine(Lang.Get("Consumption") + ": " + MyMiniLib.GetAttributeFloat(inSlot.Itemstack.Block, "maxConsumption", 0) + " " + Lang.Get("W"));
        dsc.AppendLine(Lang.Get("WResistance") + ": " + ((MyMiniLib.GetAttributeBool(inSlot.Itemstack.Block, "isolatedEnvironment", false)) ? Lang.Get("Yes") : Lang.Get("No")));
    }
}