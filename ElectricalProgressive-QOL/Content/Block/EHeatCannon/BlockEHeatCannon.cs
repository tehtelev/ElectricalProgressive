using ElectricalProgressive.Patch;
using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EHeatCannon
{
    public class BlockEHeatCannon : BlockEBase
    {

        private WorldInteraction[] _interactions = [];


        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
        {
            var selection = new Selection(blockSel);
            var facing = FacingHelper.From(selection.Face, selection.Direction);

            if (FacingHelper.Faces(facing).FirstOrDefault() is BlockFacing blockFacing)
            {
                var neighborPos = blockSel.Position.AddCopy(blockFacing);
                var neighborBlock = world.BlockAccessor.GetBlock(neighborPos);

                if (!neighborBlock.SideSolid[blockFacing.Opposite.Index])
                {
                    return false;
                }
            }

            return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
        }

        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
        {
            var selection = new Selection(blockSel);
            var facing = FacingHelper.From(selection.Face, selection.Direction);

            if (!base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack) ||
                world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityEHeatCannon entity)
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
                _ => "disabled"
            };

            var blockCode = CodeWithVariants(new Dictionary<string, string>
            {
                { "side", "north" },
                { "state", newState }
            });
            var block = world.BlockAccessor.GetBlock(blockCode);

            return new ItemStack(block);
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            return [OnPickBlock(world, pos)];
        }
        


        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityEHeatCannon entity)
            {
                var blockFacing = BlockFacing.FromVector(neibpos.X - pos.X, neibpos.Y - pos.Y, neibpos.Z - pos.Z);
                var selectedFacing = FacingHelper.FromFace(blockFacing);

                if ((entity.Facing & ~selectedFacing) == Facing.None)
                    world.BlockAccessor.BreakBlock(pos, null);
            }
        }



        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            return true;
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);

            if (api.Side == EnumAppSide.Client)
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




        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            var block = inSlot.Itemstack.Block;

            dsc.AppendLine(Lang.Get("electricalprogressivebasics:Voltage") + ": " + MyMiniLib.GetAttributeInt(block, "voltage", 0) + " " + Lang.Get("electricalprogressivebasics:V"));
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:Consumption") + ": " + MyMiniLib.GetAttributeFloat(block, "maxConsumption", 0) + " " + Lang.Get("electricalprogressivebasics:W"));
            dsc.AppendLine(Lang.Get("electricalprogressivebasics:WResistance") + ": " + (MyMiniLib.GetAttributeBool(block, "isolatedEnvironment", false) ? Lang.Get("electricalprogressivebasics:Yes") : Lang.Get("electricalprogressivebasics:No")));
        }

    }
}