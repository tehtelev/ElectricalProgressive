using Cairo.Freetype;
using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ELamp
{
    internal class BlockESmallLamp : BlockEBase
    {
        private static readonly Dictionary<(Facing, string, int), MeshData> MeshCache = [];
        private static readonly Dictionary<(Facing, string), Cuboidf[]> SelectionBoxesCache = [];
        private static readonly Dictionary<(Facing, string), Cuboidf[]> CollisionBoxesCache = [];






        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
        {
            // если блок сгорел, то не ставим
            if (byItemStack.Block.Variant["state"] == "burned")
                return false;

            var selection = new Selection(blockSel);
            var facing = FacingHelper.From(selection.Face, selection.Direction);

            if (
                base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack) &&
                world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityELamp entity
            )
            {
                entity.Facing = facing;

                //задаем электрические параметры блока/проводника
                LoadEProperties.Load(this, entity, selection.Face.Index, facing);

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
                { "tempK", this.Variant["tempK"] },
                { "state", newState }
            });

            var block = world.BlockAccessor.GetBlock(blockCode);
            return new(block);
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer,
            float dropQuantityMultiplier = 1)
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



        private Cuboidf[] GetRotatedBoxes(BlockPos pos, Dictionary<(Facing, string), Cuboidf[]> cache, Cuboidf[] sourceBoxes)
        {
            if (api?.World?.BlockAccessor.GetBlockEntity(pos) is not BlockEntityELamp entity ||
                entity.Facing == Facing.None)
            {
                return [];
            }

            var facing = entity.Facing;
            string code = entity.Block.Code.ToString();

            if (!cache.TryGetValue((facing, code), out var boxes))
            {
                boxes = (Cuboidf[]?)sourceBoxes.Clone();

                // быстро враащем коллизии
                FacingRotations.ApplyRotations(boxes, facing);

                cache.TryAdd((facing, code), boxes);
            }

            return boxes ?? [];
        }


        public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
        {
            base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);

            if (api is ICoreClientAPI &&
                api.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityELamp entity &&
                entity.Facing != Facing.None)
            {
                var facing = entity.Facing;
                string code = entity.Block.Code.ToString();

                // VerticesCount отличается у LOD0 и LOD2
                var cacheKey = (facing, code, sourceMesh.VerticesCount);

                if (!MeshCache.TryGetValue(cacheKey, out var meshData))
                {
                    // Клонируем входящий меш (уже правильный — LOD0 или LOD2)
                    meshData = sourceMesh.Clone();

                    // быстро враащем обьект
                    FacingRotations.ApplyRotations(meshData, facing);


                    MeshCache.TryAdd(cacheKey, meshData);
                }

                sourceMesh = meshData;
            }

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

            var block = inSlot.Itemstack?.Block;

            if (block == null)
                return;

            dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " + MyMiniLib.GetAttributeInt(block, "voltage", 0) + " " + Lang.Get("electricalprogressivebasics:V"));
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:Consumption") + ": " + MyMiniLib.GetAttributeFloat(block, "maxConsumption", 0) + " " + Lang.Get("electricalprogressivebasics:W"));
            dsc.AppendLine(Lang.Get("electricalprogressiveqol:max-light") + ": " + MyMiniLib.GetAttributeInt(block, "HSv", 0));
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " + ((MyMiniLib.GetAttributeBool(block, "isolatedEnvironment", false)) ? Lang.Get("electricalprogressivebasics:Yes") : Lang.Get("electricalprogressivebasics:No")));
        }




        public override void OnUnloaded(ICoreAPI api)
        {
            base.OnUnloaded(api);
            MeshCache?.Clear();
            SelectionBoxesCache?.Clear();
            CollisionBoxesCache?.Clear();
        }
    }
}
