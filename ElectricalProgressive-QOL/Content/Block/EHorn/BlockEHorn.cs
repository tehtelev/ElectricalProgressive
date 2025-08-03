using ElectricalProgressive.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.Block.EHorn;

public class BlockEHorn : BlockEBase
{
    private WorldInteraction[]? _interactions;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is not ICoreClientAPI clientApi)
            return;

        this._interactions = ObjectCacheUtil.GetOrCreate(
            api,
            "forgeBlockInteractions",
            () =>
            {
                var heatableStacklist = new List<ItemStack>();

                foreach (
                    var stacks in
                    from obj in api.World.Collectibles
                    let firstCodePart = obj.FirstCodePart()
                    where firstCodePart == "ingot" || firstCodePart == "metalplate" || firstCodePart == "workitem"
                    select obj.GetHandBookStacks(clientApi)
                    into stacks
                    where stacks != null
                    select stacks
                )
                {
                    heatableStacklist.AddRange(stacks);
                }

                return new[]
                {
                    new WorldInteraction
                    {
                        ActionLangCode = "blockhelp-forge-addworkitem",
                        HotKeyCode = "shift",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = heatableStacklist.ToArray(),
                        GetMatchingStacks = (worldInteraction, blockSelection, _) =>
                        {
                            if (api.World.BlockAccessor.GetBlockEntity(blockSelection.Position) is
                                BlockEntityEHorn { Contents: not null } bef)
                            {
                                return worldInteraction.Itemstacks.Where(stack =>
                                        stack.Equals(api.World, bef.Contents,
                                            GlobalConstants.IgnoredStackAttributes))
                                    .ToArray();
                            }

                            return worldInteraction.Itemstacks;
                        }
                    },
                    new WorldInteraction
                    {
                        ActionLangCode = "blockhelp-forge-takeworkitem",
                        HotKeyCode = null,
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = heatableStacklist.ToArray(),
                        GetMatchingStacks = (_, blockSelection, _) =>
                        {
                            if (api.World.BlockAccessor.GetBlockEntity(blockSelection.Position) is
                                BlockEntityEHorn { Contents: not null } bef)
                            {
                                return new[]
                                {
                                    bef.Contents
                                };
                            }

                            return null;
                        }
                    }
                };
            }
        );
    }

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack,
        BlockSelection blockSel, ref string failureCode)
    {
        return world.BlockAccessor
                   .GetBlock(blockSel.Position.AddCopy(BlockFacing.DOWN))
                   .SideSolid[BlockFacing.indexUP] &&
               base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);

        if (
            !world.BlockAccessor
                .GetBlock(pos.AddCopy(BlockFacing.DOWN))
                .SideSolid[BlockFacing.indexUP]
        )
        {
            world.BlockAccessor.BreakBlock(pos, null);
        }
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityEHorn entity)
        {
            return entity.OnPlayerInteract(world, byPlayer, blockSel);
        }

        return base.OnBlockInteractStart(world, byPlayer, blockSel);
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
        return new[] { OnPickBlock(world, pos) };
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return this._interactions.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
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