using ElectricalProgressive.Utils;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.CableDot
{
    internal class BlockCableDotDown : ImmersiveWireBlock
    {
        // кеши для мешей и коллизий
        private static readonly Dictionary<(Facing, string, int), MeshData> MeshDataCache = [];
        private static readonly Dictionary<(Facing, string), Cuboidf[]> SelectionBoxesCache = [];
        private static readonly Dictionary<(Facing, string), Cuboidf[]> CollisionBoxesCache = [];

       

        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);
            MeshDataCache?.Clear();
            SelectionBoxesCache?.Clear();
            CollisionBoxesCache?.Clear();
        }


        
        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            var block = world.BlockAccessor.GetBlock(blockSel.Position.AddCopy(BlockFacing.DOWN));
            // просто проверяем наличие блока снизу
            if (block == null || block.Id==0 || block.IsLiquid())
            {
                return false;
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
                
                var block = world.BlockAccessor.GetBlock(neibpos);
                // просто проверяем наличие блока снизу
                if (neibpos.Equals(pos.AddCopy(BlockFacing.DOWN)) &&
                    (block==null || block.Id == 0 || block.IsLiquid()))
                {
                    // иначе ломаем
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

        private Cuboidf[] GetRotatedBoxes(BlockPos pos, Dictionary<(Facing, string), Cuboidf[]> cache, Cuboidf[] sourceBoxes)
        {
            if (api?.World?.BlockAccessor.GetBlockEntity(pos) is not BlockEntityCableDotDown entity ||
                entity.Facing == Facing.None)
            {
                return [];
            }

            var facing = entity.Facing;
            string code = entity.Block.Code.ToString();

            var cacheKey = (facing, code);

            if (!cache.TryGetValue(cacheKey, out var boxes))
            {
                boxes = (Cuboidf[]?)sourceBoxes.Clone();

                // быстро враащем коллизии
                FacingRotations.ApplyRotations(boxes, cacheKey.facing);

                cache.TryAdd(cacheKey, boxes);
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

            var facing = entity.Facing;
            string code = entity.Block.Code.ToString();

            // VerticesCount отличается у LOD0 и LOD2
            var cacheKey = (facing, code, sourceMesh.VerticesCount);

            if (!MeshDataCache.TryGetValue(cacheKey, out var meshData))
            {
                // Клонируем входящий меш (уже правильный — LOD0 или LOD2)
                meshData = sourceMesh.Clone();

                // быстро враащем обьект
                FacingRotations.ApplyRotations(meshData, facing);

                MeshDataCache.TryAdd(cacheKey, meshData);
            }

            // передаем мэш, чтобы им управлял ImmersiveWireBlock
            _CustomMeshData = meshData;

            base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);
        }



        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            var block = inSlot.Itemstack?.Block;

            if (block == null)
                return;

            dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " + MyMiniLib.GetAttributeInt(block, "voltage", 0) + " " + Lang.Get("electricalprogressivebasics:V"));
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " +
                (MyMiniLib.GetAttributeBool(block, "isolatedEnvironment", false) ? Lang.Get("electricalprogressivebasics:Yes") : Lang.Get("electricalprogressivebasics:No")));
        }

        

      
    }
}