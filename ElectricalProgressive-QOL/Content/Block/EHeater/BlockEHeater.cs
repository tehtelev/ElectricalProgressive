using ElectricalProgressive.Patch;
using ElectricalProgressive.Utils;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using XSkills;

namespace ElectricalProgressive.Content.Block.EHeater
{
    public class BlockEHeater : BlockEBase
    {
        private WorldInteraction[] _interactions = [];

        private static readonly Dictionary<CacheDataKey, MeshData> MeshDataCache = new();
        private static readonly Dictionary<CacheDataKey, Cuboidf[]> SelectionBoxesCache = new();
        private static readonly Dictionary<CacheDataKey, Cuboidf[]> CollisionBoxesCache = new();



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
                world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityEHeater entity)
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
            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityEHeater entity)
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
            if (api?.World?.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEHeater entity ||
                entity.Facing == Facing.None)
            {
                return [];
            }

            var key = CacheDataKey.FromEntity(entity);

            if (!cache.TryGetValue(key, out var boxes))
            {
                boxes = (Cuboidf[]?)sourceBoxes.Clone();

                // быстро враащем коллизии
                FacingRotations.ApplyRotations(ref boxes, key.Facing);

                cache.TryAdd(key, boxes);

            }

            return boxes ?? [];
        }

        public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
        {
            if (api is not ICoreClientAPI clientApi ||
                api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEHeater entity ||
                entity.Facing == Facing.None)
            {
                return;
            }

            var key = CacheDataKey.FromEntity(entity);

            if (!MeshDataCache.TryGetValue(key, out var meshData))
            {
                clientApi.Tesselator.TesselateBlock(this, out meshData);
                clientApi.TesselatorManager.ThreadDispose();

                // быстро враащем обьект
                FacingRotations.ApplyRotations(meshData, key.Facing);

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
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " +
                (MyMiniLib.GetAttributeBool(block, "isolatedEnvironment", false) ? Lang.Get("electricalprogressivebasics:Yes") : Lang.Get("electricalprogressivebasics:No")));
        }



        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            return true;
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);

            if (api.Side==EnumAppSide.Client)
                return;

            // держит ключ?
            ItemSlot activeSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
            if (activeSlot?.Itemstack?.Item?.Tool != EnumTool.Wrench)
                return;

            // система комнат готова?
            RoomRegistry roomreg = api.ModLoader.GetModSystem<RoomRegistry>();
            if (roomreg == null)
                return;
            
            //выделение корректно?
            if (blockSel == null || blockSel.Position == null)
                return;

            // есть ли комната тут
            Room roomForPosition = roomreg.GetRoomForPosition(blockSel.Position);
            if (roomForPosition == null)
                return;
            
            FarmlandHeaterPatch.CalculateHeaterBonus(api, blockSel.Position, roomForPosition);

        }

        public override void OnLoaded(ICoreAPI api)
        {
            if (api.Side != EnumAppSide.Client)
                return;

            var capi = api as ICoreClientAPI;


            _interactions = ObjectCacheUtil.GetOrCreate(api, "heaterBlockInteractions", () =>
            {
                var wrenchItems = new List<ItemStack>();

                Vintagestory.API.Common.Item[] wrenches = capi.World.SearchItems(new AssetLocation("wrench-*"));
                foreach (Vintagestory.API.Common.Item item in wrenches)
                    wrenchItems.Add(new ItemStack(item));

                return new[] {
                    new WorldInteraction
                    {
                        ActionLangCode = "electricalprogressiveqol:update_heater_info",
                        HotKeyCode = null,
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = wrenchItems.ToArray()
                    }
                };
            });
        }


        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return _interactions; // такой вариант самый производительный
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

            public static CacheDataKey FromEntity(BlockEntityEHeater entity)
            {
                return new CacheDataKey(entity.Facing, entity.IsEnabled, entity.Block.Code);
            }
        }


    }
}