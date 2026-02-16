using ElectricalProgressive.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Config;

namespace ElectricalProgressive.Content.Block.CableDot
{
    internal class BlockCableDotDown : ImmersiveWireBlock
    {
        private static readonly Dictionary<CacheDataKey, MeshData> MeshDataCache = [];
        private static readonly Dictionary<CacheDataKey, Cuboidf[]> SelectionBoxesCache = [];
        private static readonly Dictionary<CacheDataKey, Cuboidf[]> CollisionBoxesCache = [];

       

        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);
            MeshDataCache.Clear();
            SelectionBoxesCache.Clear();
            CollisionBoxesCache.Clear();
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            var selection = new Selection(blockSel);
            // только вариации нижние
            var facing = FacingHelper.From(BlockFacing.DOWN, selection.Direction);

            //только на пол
            if (facing != Facing.None &&
                FacingHelper.Faces(facing).First() is { } blockFacing &&
                selection.Face != BlockFacing.DOWN)
            {
                var neighborBlock = world.BlockAccessor
                    .GetBlock(blockSel.Position.AddCopy(blockFacing));

                
                if (!neighborBlock.SideSolid[blockFacing.Index])
                {
                    return false;
                }
                
            }

            return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
        }

        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
        {
            
            var selection = new Selection(blockSel);
            // только вариации нижние
            var facing = FacingHelper.From(BlockFacing.DOWN, selection.Direction);

            if (facing == Facing.None ||
                !base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack) ||
                world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityCableDotDown entity)
            {
                return false;
            }

            entity.Facing = facing;

            LoadImmersiveEProperties.Load(this, entity);

            return true;
        }

     

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityCableDotDown entity)
            {
                var blockFacing = BlockFacing.FromVector(neibpos.X - pos.X, neibpos.Y - pos.Y, neibpos.Z - pos.Z);
                var selectedFacing = FacingHelper.FromFace(blockFacing);

                if ((entity.Facing & ~selectedFacing) == Facing.None)
                {
                    world.BlockAccessor.BreakBlock(pos, null);
                }
            }
        }

        public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            return GetRotatedBoxes(pos, CollisionBoxesCache, CollisionBoxes);
        }

        public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        {
            // передаем выделения ниже, чтобы ими управлял ImmersiveWireBlock
            _CustomSelBoxes = GetRotatedBoxes(pos, SelectionBoxesCache, SelectionBoxes);
            return base.GetSelectionBoxes(blockAccessor, pos);
        }

        private Cuboidf[] GetRotatedBoxes(BlockPos pos, Dictionary<CacheDataKey, Cuboidf[]> cache, Cuboidf[] sourceBoxes)
        {
            if (api?.World?.BlockAccessor.GetBlockEntity(pos) is not BlockEntityCableDotDown entity ||
                entity.Facing == Facing.None)
            {
                return [];
            }

            var key = CacheDataKey.FromEntity(entity);

            if (!cache.TryGetValue(key, out var boxes))
            {
                if (entity.RotationCache.TryGetValue(key.Facing, out var rotation))
                {
                    var origin = new Vec3d(0.5, 0.5, 0.5);
                    boxes = sourceBoxes.Select(box => box.RotatedCopy(rotation.X, rotation.Y, rotation.Z, origin)).ToArray();
                    cache.TryAdd(key, boxes);
                }
            }

            return boxes ?? [];
        }

        public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
        {
            if (api is not ICoreClientAPI clientApi ||
                api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityCableDotDown entity ||
                entity.Facing == Facing.None)
            {
                base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);
                return;
            }

            var key = CacheDataKey.FromEntity(entity);

            if (!MeshDataCache.TryGetValue(key, out var meshData))
            {
                var origin = new Vec3f(0.5f, 0.5f, 0.5f);
                clientApi.Tesselator.TesselateBlock(this, out meshData);
                clientApi.TesselatorManager.ThreadDispose();

                if (entity.RotationCache.TryGetValue(key.Facing, out var rotation))
                {
                    meshData.Rotate(origin,
                        rotation.X * GameMath.DEG2RAD,
                        rotation.Y * GameMath.DEG2RAD,
                        rotation.Z * GameMath.DEG2RAD);
                }

                MeshDataCache.TryAdd(key, meshData);
            }

            // передаем мэш, чтобы им управлял ImmersiveWireBlock
            _CustomMeshData = meshData;

            base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);
        }



        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            var block = inSlot.Itemstack.Block;

            dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " + MyMiniLib.GetAttributeInt(block, "voltage", 0) + " " + Lang.Get("electricalprogressivebasics:V"));
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " +
                (MyMiniLib.GetAttributeBool(block, "isolatedEnvironment", false) ? Lang.Get("electricalprogressivebasics:Yes") : Lang.Get("electricalprogressivebasics:No")));
        }



        internal struct CacheDataKey
        {
            public readonly Facing Facing;
            public readonly string Code;

            public CacheDataKey(Facing facing, string code)
            {
                Facing = facing;
                Code = code;
            }

            public static CacheDataKey FromEntity(BlockEntityCableDotDown entity)
            {
                return new CacheDataKey(entity.Facing, entity.Block.Code);
            }
        }

      
    }
}