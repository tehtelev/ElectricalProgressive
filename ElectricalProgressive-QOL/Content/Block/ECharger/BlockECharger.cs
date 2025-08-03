using System.Collections.Generic;
using System.Text;
using ElectricalProgressive.Content.Block.EMotor;
using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.Block.ECharger;

public class BlockECharger : BlockEBase
{
    public static Dictionary<Item, ToolTextures> ToolTextureSubIds(ICoreAPI api)
    {
        Dictionary<Item, ToolTextures> result;

        if (api.ObjectCache.TryGetValue("toolTextureSubIdsTest", out var obj)
            && obj is Dictionary<Item, ToolTextures> toolTextureSubIdsTest)
        {
            result = toolTextureSubIdsTest;
        }
        else
        {
            api.ObjectCache["toolTextureSubIdsTest"] = result = new();
        }

        return result;
    }

    private WorldInteraction[]? _interactions;
    private int _output;

    public override void OnLoaded(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Client)
            return;

        ICoreClientAPI? capi = api as ICoreClientAPI;


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

    // We need the tool item textures also in the block atlas
    public override void OnCollectTextures(ICoreAPI api, ITextureLocationDictionary textureDict)
    {
        base.OnCollectTextures(api, textureDict);

        foreach (var item in api.World.Items)
        {
            if (item.Attributes?["chargable"].AsBool() != true)
                continue;

            var toolTextures = new ToolTextures();

            if (item.Shape != null)
            {
                var asset = api.Assets.TryGet(item.Shape.Base.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json"));
                if (asset != null)
                {
                    var shape = asset.ToObject<Shape>();
                    foreach (var val in shape.Textures)
                    {
                        var ctex = new CompositeTexture(val.Value.Clone());
                        ctex.Bake(api.Assets);

                        textureDict.AddTextureLocation(new AssetLocationAndSource(ctex.Baked.BakedName, "Shape code " + item.Shape.Base));
                        toolTextures.TextureSubIdsByCode[val.Key] = textureDict[new(ctex.Baked.BakedName)];
                    }
                }
            }

            foreach (var val in item.Textures)
            {
                val.Value.Bake(api.Assets);
                textureDict.AddTextureLocation(new(val.Value.Baked.BakedName, "Item code " + item.Code));
                toolTextures.TextureSubIdsByCode[val.Key] = textureDict[new(val.Value.Baked.BakedName)];
            }

            ToolTextureSubIds(api)[item] = toolTextures;
        }
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        return _interactions.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
    }

    public override bool CanAttachBlockAt(IBlockAccessor blockAccessor, Vintagestory.API.Common.Block block, BlockPos pos, BlockFacing blockFace, Cuboidi attachmentArea = null!)
    {
        return blockFace == BlockFacing.DOWN;
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