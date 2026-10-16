using System;
using ElectricalProgressive.Content.Block;
using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage.Controller;

/// <summary>
/// Контроллер сети. Соседние по грани блоки дают каналы вместе.
/// </summary>
public class BlockEStorageController : BlockEBase
{
    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
    {
        if (!base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack))
            return false;

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityEStorageController entity)
            LoadEProperties.Load(this, entity);

        StorageAccess.DirtyTopology();
        return true;
    }

    public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
    {
        base.OnBlockRemoved(world, pos);
        StorageAccess.DirtyTopology();
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        world.BlockAccessor.MarkBlockDirty(pos);
        foreach (var face in BlockFacing.ALLFACES)
            world.BlockAccessor.MarkBlockDirty(pos.AddCopy(face));
    }

    public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
    {
        if (api is not ICoreClientAPI capi || chunkExtBlocks == null)
        {
            base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);
            return;
        }

        var mask = ControllerMesh.Mask(chunkExtBlocks, extIndex3d);
        var drives = ControllerMesh.DriveMask(chunkExtBlocks, extIndex3d);
        var custom = false;
        if (mask != 0)
        {
            var parity = (Math.Abs(pos.X) + Math.Abs(pos.Y) + Math.Abs(pos.Z)) & 1;
            var mesh = ControllerMesh.For(capi, this, mask, drives, parity);
            if (mesh != null)
            {
                sourceMesh = mesh.Clone();
                custom = true;
            }
        }

        if (!custom && drives != 0 && sourceMesh != null)
        {
            var cover = ControllerMesh.ShellCover(capi, this, drives);
            if (cover != null)
            {
                var copy = sourceMesh.Clone();
                copy.AddMeshData(cover);
                sourceMesh = copy;
            }
        }

        base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);
    }
}
