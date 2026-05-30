using ElectricalProgressive.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.Block.CableSwitch
{
    internal class BlockCableSwitchWall : ImmersiveWireBlock
    {
        // кеши для мешей и коллизий
        private static readonly Dictionary<(Facing, string, int), MeshData> MeshDataCache = [];
        private static readonly Dictionary<(Facing, string), Cuboidf[]> SelectionBoxesCache = [];
        private static readonly Dictionary<(Facing, string), Cuboidf[]> CollisionBoxesCache = [];

        private WorldInteraction[] _interactions = [];



        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            _interactions = ObjectCacheUtil.GetOrCreate(api, "BlockCableSwitchWallInteractions", () =>
            {
                return new[]
                {
                    new WorldInteraction
                    {
                        ActionLangCode = "electricalprogressivebasics:switch",
                        HotKeyCode = null,
                        MouseButton = EnumMouseButton.Right,
                    }
                };
            });

        }



        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);
            MeshDataCache?.Clear();
            SelectionBoxesCache?.Clear();
            CollisionBoxesCache?.Clear();
        }


        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            var selection = new Selection(blockSel);
            var facing = FacingHelper.From(selection.Face, BlockFacing.DOWN);
            

            // целая ли грань, на которую ставим
            if (!MyMiniLib.CheckSolidFace(world.BlockAccessor, blockSel.Position, facing))
            {
                return false;
            }

            return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
        }

        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
        {
            
            var selection = new Selection(blockSel);
            // только вариации настенные нижние
            var facing = FacingHelper.From(selection.Face, BlockFacing.DOWN);

            if (facing == Facing.None ||
                !base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack) ||
                world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityCableSwitch entity)
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

            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityCableSwitch entity)
            {
                // целая ли грань еще
                if (MyMiniLib.CheckSolidFace(world.BlockAccessor, pos, entity.Facing))
                {
                    return;
                }

                // иначе ломаем
                world.BlockAccessor.BreakBlock(pos, null);
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
            if (api?.World?.BlockAccessor.GetBlockEntity(pos) is not BlockEntityCableSwitch entity ||
                entity.Facing == Facing.None)
            {
                return [];
            }
            

            var facing = entity.Facing;
            string code = entity.Block.Code.ToString();

            var cacheKey = (facing, code);

            if (!cache.TryGetValue(cacheKey, out var boxes))
            {
                if (entity.RotationCache.TryGetValue(cacheKey.facing, out var rotation))
                {
                    var origin = new Vec3d(0.5, 0.5, 0.5);
                    boxes = sourceBoxes.Select(box => box.RotatedCopy(rotation.X, rotation.Y, rotation.Z, origin)).ToArray();
                    cache.TryAdd(cacheKey, boxes);
                }
            }

            return boxes ?? [];
        }




        public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
        {
            if (api is not ICoreClientAPI clientApi ||
                api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityCableSwitch entity ||
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

                if (entity.RotationCache.TryGetValue(cacheKey.facing, out var rotation))
                {
                    var origin = new Vec3f(0.5f, 0.5f, 0.5f);

                    meshData.Rotate(origin,
                        rotation.X * GameMath.DEG2RAD,
                        rotation.Y * GameMath.DEG2RAD,
                        rotation.Z * GameMath.DEG2RAD);
                }

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

            if (block==null)
                return;

            dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " + MyMiniLib.GetAttributeInt(block, "voltage", 0) + " " + Lang.Get("electricalprogressivebasics:V"));
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " +
                (MyMiniLib.GetAttributeBool(block, "isolatedEnvironment", false) ? Lang.Get("electricalprogressivebasics:Yes") : Lang.Get("electricalprogressivebasics:No")));
        }



        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {

            return true;

        }



        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return _interactions;
        }



        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);
            
            // если игрок держит провод или инструмент, то не переключаем
            if (IsHoldingWireTool(byPlayer) || IsHoldingEKit(byPlayer))
                return;

            var entity = world.BlockAccessor.GetBlockEntity(blockSel.Position);
            var beh= entity?.GetBehavior<BEBehaviorCableSwitch>();

            if (beh != null)
            {
                beh.IsOpen = !beh.IsOpen;

                // делаем анимацию переключения
                if (api is ICoreClientAPI)
                {
                    if (beh.IsOpen)
                    {
                        (entity as BlockEntityCableSwitch)?.Open();
                    }
                    else
                    {
                        (entity as BlockEntityCableSwitch)?.Close();
                    }
                }
            }

        }


        
    }
}