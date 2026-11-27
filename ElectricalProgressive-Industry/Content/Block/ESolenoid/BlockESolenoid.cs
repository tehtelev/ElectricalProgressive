using ElectricalProgressive.Utils;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ESolenoid;

/// <summary>
/// Соленоид. Ставится как мотор: основание к грани, шток смотрит по взгляду.
/// Модель в покое лежит штоком на север, поворот берётся из Facing так же, как у мотора.
/// </summary>
public class BlockESolenoid : BlockEBase
{
    private static readonly Dictionary<(Facing, int), MeshData> MeshCache = new();

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        if (!TryPickFacing(blockSel, out var facing))
            return false;

        if (!MyMiniLib.CheckSolidFace(world.BlockAccessor, blockSel.Position, facing))
            return false;

        return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }

    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
    {
        if (!TryPickFacing(blockSel, out var facing))
            return false;

        if (!base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack) ||
            world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityESolenoid entity)
        {
            return false;
        }

        entity.Facing = facing;
        LoadEProperties.Load(this, entity, new Selection(blockSel).Face.Index);
        entity.MarkDirty(true);
        return true;
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);

        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityESolenoid entity &&
            !MyMiniLib.CheckSolidFace(world.BlockAccessor, pos, entity.Facing))
        {
            world.BlockAccessor.BreakBlock(pos, null);
        }
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        return new ItemStack(this);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        return [OnPickBlock(world, pos)];
    }

    public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
    {
        base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);
        if (api.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityESolenoid entity && entity.Facing != Facing.None)
            SolenoidMesh.Apply(ref sourceMesh, entity.Facing, MeshCache);
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);
        MeshCache.Clear();
    }

    private static bool TryPickFacing(BlockSelection blockSel, out Facing facing)
    {
        facing = Facing.None;
        try
        {
            facing = new Selection(blockSel).Facing;
        }
        catch
        {
            return false;
        }

        return facing != Facing.None;
    }
}
