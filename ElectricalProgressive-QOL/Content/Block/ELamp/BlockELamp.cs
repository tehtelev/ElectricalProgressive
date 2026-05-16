using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ELamp
{
    internal class BlockELamp : BlockEBase
    {
        private static readonly Dictionary<CacheDataKey, MeshData> MeshDataCache = [];
        private static readonly Dictionary<CacheDataKey, Cuboidf[]> SelectionBoxesCache = [];
        private static readonly Dictionary<CacheDataKey, Cuboidf[]> CollisionBoxesCache = [];

        // Кэш преобразований поворотов
        private static readonly Dictionary<Facing, RotationData> RotationCache = CreateRotationCache();

        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);
            MeshDataCache?.Clear();
            SelectionBoxesCache?.Clear();
            CollisionBoxesCache?.Clear();
        }

      

        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
        {
            // если блок сгорел, то не ставим
            if (byItemStack.Block.Variant["state"] == "burned")
                return false;

            var selection = new Selection(blockSel);
            var facing = FacingHelper.From(selection.Face, selection.Direction);

            if (!base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack) ||
                world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityELamp entity)
            {
                return false;
            }

            entity.Facing = facing;
            LoadEProperties.Load(this, entity, selection.Face.Index);
            return true;
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            var newState = Variant["state"] switch
            {
                "enabled" => "disabled",
                "disabled" => "disabled",
                _ => "burned"
            };

            var blockCode = CodeWithVariants(new Dictionary<string, string>
            {
                { "tempK", Variant["tempK"] },
                { "state", newState }
            });

            var block = world.BlockAccessor.GetBlock(blockCode);
            return new ItemStack(block);
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            return [OnPickBlock(world, pos)];
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack,
            BlockSelection blockSel, ref string failureCode)
        {
            var selection = new Selection(blockSel);
            var facing = Facing.None;

            try
            {
                facing = FacingHelper.From(selection.Face, selection.Direction);
            }
            catch
            {
                return false;
            }

            // целая ли грань, на которую ставим
            if (!MyMiniLib.CheckSolidFace(world.BlockAccessor, blockSel.Position, facing))
            {
                return false;
            }

            return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            // проверим целостность грани
            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityELamp entity)
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
            return GetRotatedBoxes(pos, SelectionBoxesCache, SelectionBoxes);
        }

        private Cuboidf[] GetRotatedBoxes(BlockPos pos, Dictionary<CacheDataKey, Cuboidf[]> cache, Cuboidf[] sourceBoxes)
        {
            if (api?.World?.BlockAccessor.GetBlockEntity(pos) is not BlockEntityELamp entity ||
                entity.Facing == Facing.None)
            {
                return [];
            }

            var key = CacheDataKey.FromEntity(entity);

            if (!cache.TryGetValue(key, out var boxes))
            {
                if (RotationCache.TryGetValue(key.Facing, out var rotation))
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
                api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityELamp entity ||
                entity.Facing == Facing.None)
            {
                return;
            }

            var key = CacheDataKey.FromEntity(entity);

            if (!MeshDataCache.TryGetValue(key, out var meshData))
            {
                clientApi.Tesselator.TesselateBlock(this, out meshData);
                clientApi.TesselatorManager.ThreadDispose();

                if (RotationCache.TryGetValue(key.Facing, out var rotation))
                {
                    var origin = new Vec3f(0.5f, 0.5f, 0.5f);
                    meshData.Rotate(origin,
                        rotation.X * GameMath.DEG2RAD,
                        rotation.Y * GameMath.DEG2RAD,
                        rotation.Z * GameMath.DEG2RAD);
                }

                MeshDataCache.TryAdd(key, meshData);
            }

            sourceMesh = meshData;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            var block = inSlot.Itemstack.Block;

            dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " + MyMiniLib.GetAttributeInt(block, "voltage", 0) + " " + Lang.Get("electricalprogressivebasics:V"));
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:Consumption") + ": " + MyMiniLib.GetAttributeFloat(block, "maxConsumption", 0) + " " + Lang.Get("electricalprogressivebasics:W"));
            dsc.AppendLine(Lang.Get("electricalprogressiveqol:max-light") + ": " + MyMiniLib.GetAttributeInt(block, "HSV", 0));
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " +
                (MyMiniLib.GetAttributeBool(block, "isolatedEnvironment", false) ? Lang.Get("electricalprogressivebasics:Yes") : Lang.Get("electricalprogressivebasics:No")));
        }

        private static Dictionary<Facing, RotationData> CreateRotationCache()
        {
            return new Dictionary<Facing, RotationData>
            {
                { Facing.NorthEast, new RotationData(90.0f, 270.0f, 0.0f) },
                { Facing.NorthWest, new RotationData(90.0f, 90.0f, 0.0f) },
                { Facing.NorthUp, new RotationData(90.0f, 0.0f, 0.0f) },
                { Facing.NorthDown, new RotationData(90.0f, 180.0f, 0.0f) },
                { Facing.EastNorth, new RotationData(0.0f, 0.0f, 90.0f) },
                { Facing.EastSouth, new RotationData(180.0f, 0.0f, 90.0f) },
                { Facing.EastUp, new RotationData(90.0f, 0.0f, 90.0f) },
                { Facing.EastDown, new RotationData(270.0f, 0.0f, 90.0f) },
                { Facing.SouthEast, new RotationData(90.0f, 270.0f, 180.0f) },
                { Facing.SouthWest, new RotationData(90.0f, 90.0f, 180.0f) },
                { Facing.SouthUp, new RotationData(90.0f, 0.0f, 180.0f) },
                { Facing.SouthDown, new RotationData(90.0f, 180.0f, 180.0f) },
                { Facing.WestNorth, new RotationData(0.0f, 0.0f, 270.0f) },
                { Facing.WestSouth, new RotationData(180.0f, 0.0f, 270.0f) },
                { Facing.WestUp, new RotationData(90.0f, 0.0f, 270.0f) },
                { Facing.WestDown, new RotationData(270.0f, 0.0f, 270.0f) },
                { Facing.UpNorth, new RotationData(0.0f, 0.0f, 180.0f) },
                { Facing.UpEast, new RotationData(0.0f, 270.0f, 180.0f) },
                { Facing.UpSouth, new RotationData(0.0f, 180.0f, 180.0f) },
                { Facing.UpWest, new RotationData(0.0f, 90.0f, 180.0f) },
                { Facing.DownNorth, new RotationData(0.0f, 0.0f, 0.0f) },
                { Facing.DownEast, new RotationData(0.0f, 270.0f, 0.0f) },
                { Facing.DownSouth, new RotationData(0.0f, 180.0f, 0.0f) },
                { Facing.DownWest, new RotationData(0.0f, 90.0f, 0.0f) }
            };
        }

        internal struct CacheDataKey
        {
            public readonly Facing Facing;
            public readonly bool IsEnabled;
            public readonly string Code;

            public CacheDataKey(Facing facing, bool isEnabled, string code)
            {
                Facing = facing;
                IsEnabled = isEnabled;
                Code = code;
            }

            public static CacheDataKey FromEntity(BlockEntityELamp entity)
            {
                return new CacheDataKey(entity.Facing, entity.IsEnabled, entity.Block.Code);
            }
        }

        private readonly struct RotationData
        {
            public readonly float X;
            public readonly float Y;
            public readonly float Z;

            public RotationData(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
            }
        }
    }
}