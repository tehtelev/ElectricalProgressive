using ElectricalProgressive.Utils;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ESolenoid;

/// <summary>Плита на конце штока. Ломается без дропа и отпускает основание.</summary>
public class BlockESolenoidHead : Vintagestory.API.Common.Block
{
    private static readonly Dictionary<Facing, Cuboidf[]> BoxCache = new();

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        var body = world.GetBlock(new AssetLocation("electricalprogressiveindustry:esolenoid"));
        return new ItemStack(body ?? this);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return [];
    }

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        return RotatedBoxes(pos);
    }

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        return RotatedBoxes(pos);
    }

    private Cuboidf[] RotatedBoxes(BlockPos pos)
    {
        var source = CollisionBoxes;
        if (source == null || source.Length == 0)
            return [];

        if (api?.World?.BlockAccessor.GetBlockEntity(pos) is not BlockEntityESolenoidHead entity ||
            entity.Facing == Facing.None)
            return source;

        if (!BoxCache.TryGetValue(entity.Facing, out var boxes))
        {
            boxes = (Cuboidf[])source.Clone();
            FacingRotations.ApplyRotations(boxes, entity.Facing);
            BoxCache[entity.Facing] = boxes;
        }

        return boxes;
    }

    public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
    {
        sourceMesh.Clear();
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);
        BoxCache.Clear();
    }
}
