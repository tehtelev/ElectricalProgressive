using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EFence;

public class BlockEFence : BlockEBase
{
    private BlockBehaviorRopeTieable bbrt;

    private static string[] OneDir;

    private static string[] TwoDir;

    private static string[] AngledDir;

    private static string[] ThreeDir;

    private static string[] GateLeft;

    private static string[] GateRight;

    private static Dictionary<string, KeyValuePair<string[], int>> AngleGroups;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        CanStep = false;
        bbrt = GetBehavior<BlockBehaviorRopeTieable>();
    }

    public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
    {
    }

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        return base.GetSelectionBoxes(blockAccessor, pos);
    }

    public string GetOrientations(IWorldAccessor world, BlockPos pos)
    {
        string text = GetFenceCode(world, pos, BlockFacing.NORTH) + GetFenceCode(world, pos, BlockFacing.EAST) + GetFenceCode(world, pos, BlockFacing.SOUTH) + GetFenceCode(world, pos, BlockFacing.WEST);
        if (text.Length == 0)
        {
            text = "empty";
        }

        return text;
    }

    private string GetFenceCode(IWorldAccessor world, BlockPos pos, BlockFacing facing)
    {
        if (ShouldConnectAt(world, pos, facing))
        {
            return facing.Code[0].ToString() ?? "";
        }

        return "";
    }

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        string orientations = GetOrientations(world, blockSel.Position);
        var block = world.BlockAccessor.GetBlock(CodeWithVariant("type", orientations));
        if (block == null)
        {
            block = this;
        }

        if (block.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
        {
            world.BlockAccessor.SetBlock(block.BlockId, blockSel.Position);
            return true;
        }

        return false;
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        string orientations = GetOrientations(world, pos);
        AssetLocation assetLocation = CodeWithVariant("type", orientations);
        if (!Code.Equals(assetLocation))
        {
            var block = world.BlockAccessor.GetBlock(assetLocation);
            if (block != null)
            {
                world.BlockAccessor.SetBlock(block.BlockId, pos);
                world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
            }
        }
        else
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);
        }
    }

    public override BlockDropItemStack[] GetDropsForHandbook(ItemStack handbookStack, IPlayer forPlayer)
    {
        return new BlockDropItemStack[1]
        {
            new BlockDropItemStack(handbookStack)
        };
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
    {
        var block = world.BlockAccessor.GetBlock(CodeWithVariants(new string[2] { "type", "cover" }, new string[2] { "ew", "free" }));
        return new ItemStack[1]
        {
            new ItemStack(block)
        };
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        return new ItemStack(world.BlockAccessor.GetBlock(CodeWithVariants(new string[2] { "type", "cover" }, new string[2] { "ew", "free" })));
    }


    public bool ShouldConnectAt(IWorldAccessor world, BlockPos ownPos, BlockFacing side)
    {
        var block = world.BlockAccessor.GetBlock(ownPos.AddCopy(side));
        JsonObject attributes = block.Attributes;
        if (attributes != null && attributes["fenceConnect"][side.Code].Exists)
        {
            return block.Attributes["fenceConnect"][side.Code].AsBool(defaultValue: true);
        }

        Cuboidi attachmentArea = (new Cuboidi[4]
        {
            new RotatableCube(6f, 0f, 15f, 10f, 14f, 15f).ToHitboxCuboidi(180f),
            new RotatableCube(6f, 0f, 15f, 10f, 14f, 15f).ToHitboxCuboidi(270f),
            new RotatableCube(6f, 0f, 15f, 10f, 14f, 15f).ToHitboxCuboidi(0f),
            new RotatableCube(6f, 0f, 15f, 10f, 14f, 15f).ToHitboxCuboidi(90f)
        })[side.Index];
        if (!(block is BlockEFence)
            && (!(block is BlockFenceGate blockFenceGate) || blockFenceGate.GetDirection() == side || blockFenceGate.GetDirection() == side.Opposite)
            && (!(block is BlockFenceGateRoughHewn blockFenceGateRoughHewn) || blockFenceGateRoughHewn.GetDirection() == side || blockFenceGateRoughHewn.GetDirection() == side.Opposite))
        {
            return block.CanAttachBlockAt(world.BlockAccessor, this, ownPos.AddCopy(side), side.Opposite, attachmentArea);
        }

        return true;
    }

    static BlockEFence()
    {
        OneDir = new string[4] { "n", "e", "s", "w" };
        TwoDir = new string[2] { "ns", "ew" };
        AngledDir = new string[4] { "ne", "es", "sw", "nw" };
        ThreeDir = new string[4] { "nes", "new", "nsw", "esw" };
        GateLeft = new string[2] { "egw", "ngs" };
        GateRight = new string[2] { "gew", "gns" };
        AngleGroups = new Dictionary<string, KeyValuePair<string[], int>>();
        AngleGroups["n"] = new KeyValuePair<string[], int>(OneDir, 0);
        AngleGroups["e"] = new KeyValuePair<string[], int>(OneDir, 1);
        AngleGroups["s"] = new KeyValuePair<string[], int>(OneDir, 2);
        AngleGroups["w"] = new KeyValuePair<string[], int>(OneDir, 3);
        AngleGroups["ns"] = new KeyValuePair<string[], int>(TwoDir, 0);
        AngleGroups["ew"] = new KeyValuePair<string[], int>(TwoDir, 1);
        AngleGroups["ne"] = new KeyValuePair<string[], int>(AngledDir, 0);
        AngleGroups["es"] = new KeyValuePair<string[], int>(AngledDir, 1);
        AngleGroups["sw"] = new KeyValuePair<string[], int>(AngledDir, 2);
        AngleGroups["nw"] = new KeyValuePair<string[], int>(AngledDir, 3);
        AngleGroups["nes"] = new KeyValuePair<string[], int>(ThreeDir, 0);
        AngleGroups["new"] = new KeyValuePair<string[], int>(ThreeDir, 1);
        AngleGroups["nsw"] = new KeyValuePair<string[], int>(ThreeDir, 2);
        AngleGroups["esw"] = new KeyValuePair<string[], int>(ThreeDir, 3);
        AngleGroups["egw"] = new KeyValuePair<string[], int>(GateLeft, 0);
        AngleGroups["ngs"] = new KeyValuePair<string[], int>(GateLeft, 1);
        AngleGroups["gew"] = new KeyValuePair<string[], int>(GateRight, 0);
        AngleGroups["gns"] = new KeyValuePair<string[], int>(GateRight, 1);
    }

    public override AssetLocation GetRotatedBlockCode(int angle)
    {
        string text = Variant["type"];
        if (text == "empty" || text == "nesw")
        {
            return Code;
        }

        int num = angle / 90;
        KeyValuePair<string[], int> keyValuePair = AngleGroups[text];
        string value = keyValuePair.Key[GameMath.Mod(keyValuePair.Value + num, keyValuePair.Key.Length)];
        return CodeWithVariant("type", value);
    }
}
