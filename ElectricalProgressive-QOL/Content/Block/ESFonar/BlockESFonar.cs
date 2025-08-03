using ElectricalProgressive.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;


namespace ElectricalProgressive.Content.Block.ESFonar
{
    internal class BlockESFonar : BlockEBase
    {
        private readonly static Dictionary<CacheDataKey, MeshData> MeshDataCache = new();

        //private int[] null_HSV = { 0, 0, 0 };   //заглушка нулевого света
        public override void OnLoaded(ICoreAPI coreApi)
        {
            base.OnLoaded(coreApi);

        }
        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);
            BlockESFonar.MeshDataCache.Clear();
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack,
            BlockSelection blockSel, ref string failureCode)
        {
            //неваляжка - только вертикально
            return world.BlockAccessor
                       .GetBlock(blockSel.Position.AddCopy(BlockFacing.DOWN))
                       .SideSolid[BlockFacing.indexUP]
                       && base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
        }

        /// <summary>
        /// Проверка на возможность установки блока
        /// </summary>
        /// <param name="world"></param>
        /// <param name="byPlayer"></param>
        /// <param name="blockSelection"></param>
        /// <param name="byItemStack"></param>
        /// <returns></returns>
        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
        {
            //if (byItemStack.Block.Variant["state"] == "burned")
            //    return false;

            var selection = new Selection(blockSel);
            var facing = FacingHelper.From(BlockFacing.DOWN, selection.Direction);

            if (
                facing != Facing.None &&
                base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack) &&
                world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityESFonar entity
            )
            {
                entity.Facing = facing;

                //задаем параметры блока/проводника
                var voltage = MyMiniLib.GetAttributeInt(byItemStack!.Block, "voltage", 32);
                var maxCurrent = MyMiniLib.GetAttributeFloat(byItemStack!.Block, "maxCurrent", 5.0F);
                var isolated = MyMiniLib.GetAttributeBool(byItemStack!.Block, "isolated", false);
                var isolatedEnvironment = MyMiniLib.GetAttributeBool(byItemStack!.Block, "isolatedEnvironment", false);

                entity.Eparams = (
                    new(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment),
                    FacingHelper.Faces(Facing.DownAll).First().Index);

                return true;
            }

            return false;
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
                { "height", this.Variant["height"] },
                { "format", this.Variant["format"] },
                { "state", newState }
            });

            var block = world.BlockAccessor.GetBlock(blockCode);
            return new(block);
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            return new[] { OnPickBlock(world, pos) };
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

        public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
        {
            if (this.api is not ICoreClientAPI clientApi ||
                this.api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityESFonar entity ||
                entity.Facing == Facing.None)
            {
                return;
            }

            var key = CacheDataKey.FromEntity(entity);

            if (!BlockESFonar.MeshDataCache.TryGetValue(key, out var meshData))
            {

                    var origin = new Vec3f(0.5f, 0.5f, 0.5f);


                    clientApi.Tesselator.TesselateBlock(this, out meshData);

                    clientApi.TesselatorManager.ThreadDispose(); //обязательно?

                    if ((key.Facing & Facing.NorthEast) != 0)
                    {
                        meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 270.0f * GameMath.DEG2RAD, 0.0f);
                    }

                    if ((key.Facing & Facing.NorthWest) != 0)
                    {
                        meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 90.0f * GameMath.DEG2RAD, 0.0f);
                    }

                    if ((key.Facing & Facing.NorthUp) != 0)
                    {
                        meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 0.0f * GameMath.DEG2RAD, 0.0f);
                    }

                    if ((key.Facing & Facing.NorthDown) != 0)
                    {
                        meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD, 0.0f);
                    }

                    if ((key.Facing & Facing.EastNorth) != 0)
                    {
                        meshData.Rotate(origin, 0.0f * GameMath.DEG2RAD, 0.0f, 90.0f * GameMath.DEG2RAD);
                    }

                    if ((key.Facing & Facing.EastSouth) != 0)
                    {
                        meshData.Rotate(origin, 180.0f * GameMath.DEG2RAD, 0.0f, 90.0f * GameMath.DEG2RAD);
                    }

                    if ((key.Facing & Facing.EastUp) != 0)
                    {
                        meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 0.0f, 90.0f * GameMath.DEG2RAD);
                    }

                    if ((key.Facing & Facing.EastDown) != 0)
                    {
                        meshData.Rotate(origin, 270.0f * GameMath.DEG2RAD, 0.0f, 90.0f * GameMath.DEG2RAD);
                    }

                    if ((key.Facing & Facing.SouthEast) != 0)
                    {
                        meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 270.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD);
                    }

                    if ((key.Facing & Facing.SouthWest) != 0)
                    {
                        meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 90.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD);
                    }

                    if ((key.Facing & Facing.SouthUp) != 0)
                    {
                        meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 0.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD);
                    }

                    if ((key.Facing & Facing.SouthDown) != 0)
                    {
                        meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD);
                    }

                    if ((key.Facing & Facing.WestNorth) != 0)
                    {
                        meshData.Rotate(origin, 0.0f * GameMath.DEG2RAD, 0.0f, 270.0f * GameMath.DEG2RAD);
                    }

                    if ((key.Facing & Facing.WestSouth) != 0)
                    {
                        meshData.Rotate(origin, 180.0f * GameMath.DEG2RAD, 0.0f, 270.0f * GameMath.DEG2RAD);
                    }

                    if ((key.Facing & Facing.WestUp) != 0)
                    {
                        meshData.Rotate(origin, 90.0f * GameMath.DEG2RAD, 0.0f, 270.0f * GameMath.DEG2RAD);
                    }

                    if ((key.Facing & Facing.WestDown) != 0)
                    {
                        meshData.Rotate(origin, 270.0f * GameMath.DEG2RAD, 0.0f, 270.0f * GameMath.DEG2RAD);
                    }

                    if ((key.Facing & Facing.UpNorth) != 0)
                    {
                        meshData.Rotate(origin, 0.0f, 0.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD);
                    }

                    if ((key.Facing & Facing.UpEast) != 0)
                    {
                        meshData.Rotate(origin, 0.0f, 270.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD);
                    }

                    if ((key.Facing & Facing.UpSouth) != 0)
                    {
                        meshData.Rotate(origin, 0.0f, 180.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD);
                    }

                    if ((key.Facing & Facing.UpWest) != 0)
                    {
                        meshData.Rotate(origin, 0.0f, 90.0f * GameMath.DEG2RAD, 180.0f * GameMath.DEG2RAD);
                    }

                    if ((key.Facing & Facing.DownNorth) != 0)
                    {
                        meshData.Rotate(origin, 0.0f, 0.0f * GameMath.DEG2RAD, 0.0f);
                    }

                    if ((key.Facing & Facing.DownEast) != 0)
                    {
                        meshData.Rotate(origin, 0.0f, 270.0f * GameMath.DEG2RAD, 0.0f);
                    }

                    if ((key.Facing & Facing.DownSouth) != 0)
                    {
                        meshData.Rotate(origin, 0.0f, 180.0f * GameMath.DEG2RAD, 0.0f);
                    }

                    if ((key.Facing & Facing.DownWest) != 0)
                    {
                        meshData.Rotate(origin, 0.0f, 90.0f * GameMath.DEG2RAD, 0.0f);
                    }

                    BlockESFonar.MeshDataCache.TryAdd(key, meshData);

                
            }

            sourceMesh = meshData;
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
            dsc.AppendLine(Lang.Get("max-light") + ": " + MyMiniLib.GetAttributeInt(inSlot.Itemstack.Block, "HSV", 0));
            dsc.AppendLine(Lang.Get("height") + ": " + this.Variant["height"]);
            dsc.AppendLine(Lang.Get("WResistance") + ": " + ((MyMiniLib.GetAttributeBool(inSlot.Itemstack.Block, "isolatedEnvironment", false)) ? Lang.Get("Yes") : Lang.Get("No")));
        }

        /// <summary>
        /// Структура ключа для кеширования данных блока.
        /// </summary>
        internal struct CacheDataKey
        {
            public readonly Facing Facing;
            public readonly bool IsEnabled;
            public readonly string code;

            public CacheDataKey(Facing facing, bool isEnabled, string code)
            {
                this.Facing = facing;
                this.IsEnabled = isEnabled;
                this.code = code;
            }

            public static CacheDataKey FromEntity(BlockEntityESFonar entity)
            {
                return new CacheDataKey(
                    entity.Facing,
                    entity.IsEnabled,
                    entity.Block.Code
                );
            }
        }

    }
}
