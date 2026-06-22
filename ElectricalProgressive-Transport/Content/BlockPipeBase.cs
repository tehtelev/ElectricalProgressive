using ElectricalProgressive.Content.ItemInsertionPipe;
using ElectricalProgressive.Content.LiquidInsertionPipe;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content
{
    /// <summary>
    /// ������� ����� ��� ���� ����� ���� � ���� Electrical Progressive Transport
    /// ������������ �������������� � ������ � ������������ ����� ������ ����
    /// </summary>
    public class BlockPipeBase : Vintagestory.API.Common.Block
    {
        /// <summary>
        /// ���������� ��� ������ �������������� � ������ (������ ����)
        /// </summary>
        public override bool OnBlockInteractStart(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel)
        {
            if (blockSel == null)
                return false;

            ItemSlot activeSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
            bool hasWrench = activeSlot.Itemstack?.Collectible?.Code?.ToString()?.Contains("wrench") == true;

            if (hasWrench)
                return TransformPipeType(world, blockSel.Position, byPlayer);

            var be = world.BlockAccessor.GetBlockEntity(blockSel.Position);

            if (be != null)
            {
                if (be is BEItemInsertionPipe itempipe)
                    itempipe.OnPlayerRightClick(byPlayer, blockSel);

                if (be is BELiquidInsertionPipe liquidPipe)
                    liquidPipe.OnPlayerRightClick(byPlayer, blockSel);
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        /// <summary>
        /// ����������� ����� ����� ����� �������, ����������� ��� ��������� � ����������� ��� ���������
        /// </summary>
        protected virtual bool TransformPipeType(IWorldAccessor world, BlockPos pos, IPlayer player)
        {
            var currentBlock = world.BlockAccessor.GetBlock(pos);
            string currentCode = currentBlock.Code.Path;

            string baseType = currentCode switch
            {
                var code when code.Contains("pipe-item-insertion") => "pipe-item-insertion",
                var code when code.Contains("pipe-liquid-insertion") => "pipe-liquid-insertion",
                var code when code.Contains("pipe-normal") => "pipe-normal",
                _ => null
            };

            if (baseType == null)
                return false;

            string nextBaseType = baseType switch
            {
                "pipe-normal" => "pipe-item-insertion",
                "pipe-item-insertion" => "pipe-liquid-insertion",
                "pipe-liquid-insertion" => "pipe-normal",
                _ => "pipe-normal"
            };

            var newBlock = world.GetBlock(new AssetLocation($"electricalprogressivetransport:{nextBaseType}"));
            if (newBlock == null)
                return false;

            ITreeAttribute tree = SaveEntityData(world, pos);

            world.BlockAccessor.SetBlock(newBlock.BlockId, pos);
            world.Api.Event.EnqueueMainThreadTask(() =>
            {
                if (tree != null)
                {
                    BlockEntity newEntity = world.BlockAccessor.GetBlockEntity(pos);
                    if (newEntity != null)
                    {
                        newEntity.FromTreeAttributes(tree, world);
                        newEntity.MarkDirty();
                    }
                }

                UpdatePipeConnections(world, pos, false);
                UpdateNeighborConnections(world, pos);

                world.BlockAccessor.MarkBlockDirty(pos);
                world.PlaySoundAt(new AssetLocation("game:sounds/effect/tooluse"),
                    pos.X, pos.Y, pos.Z, player);
            }, "transform-pipe");

            return true;
        }

        private ITreeAttribute SaveEntityData(IWorldAccessor world, BlockPos pos)
        {
            var currentEntity = world.BlockAccessor.GetBlockEntity(pos);
            if (currentEntity == null)
                return null;

            var tree = new TreeAttribute();
            currentEntity.ToTreeAttributes(tree);

            if (currentEntity is BEItemInsertionPipe itemPipe)
            {
                tree.SetInt("transferRate", itemPipe.TransferRate);
                tree.SetInt("filterMode", (int)itemPipe.CurrentFilterMode);
                tree.SetBool("matchMod", itemPipe.MatchMod);
                tree.SetBool("matchType", itemPipe.MatchType);
                tree.SetBool("matchAttributes", itemPipe.MatchAttributes);
            }
            else if (currentEntity is BELiquidInsertionPipe liquidPipe)
            {
                tree.SetInt("transferRate", liquidPipe.TransferRate);
                tree.SetInt("filterMode", (int)liquidPipe.CurrentFilterMode);
            }

            return tree;
        }

        private void UpdateNeighborConnections(IWorldAccessor world, BlockPos pos)
        {
            for (int i = 0; i < 6; i++)
            {
                BlockFacing facing = BlockFacing.ALLFACES[i];
                BlockPos neighborPos = pos.AddCopy(facing);

                if (world.BlockAccessor.GetBlock(neighborPos) is BlockPipeBase)
                    UpdatePipeConnections(world, neighborPos, true);
            }
        }

        private static void UpdatePipeConnections(IWorldAccessor world, BlockPos pos, bool updateNeighbors)
        {
            var entity = world.BlockAccessor.GetBlockEntity(pos);

            if (entity is BEPipe normalPipe)
                normalPipe.UpdateConnections(updateNeighbors);
            else if (entity is BlockEntityPipeBase pipe)
                pipe.UpdateConnections(updateNeighbors);
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(
            IWorldAccessor world,
            BlockSelection selection,
            IPlayer forPlayer)
        {
            var wrenchStacks = new List<ItemStack>();

            foreach (var obj in world.Collectibles)
            {
                if (obj.FirstCodePart() == "wrench")
                {
                    var stacks = obj.GetHandBookStacks(api as ICoreClientAPI);
                    if (stacks != null)
                        wrenchStacks.AddRange(stacks);
                }
            }

            return new WorldInteraction[1]
            {
                new WorldInteraction()
                {
                    ActionLangCode = "electricalprogressivetransport:blockhelp-pipe-switch-type",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = wrenchStacks.Count > 0 ? wrenchStacks.ToArray() : null
                }
            }.Append<WorldInteraction>(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
        }

        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            var entity = world.BlockAccessor.GetBlockEntity(pos);
            if (entity == null)
                return;

            if (entity is BEPipe pipe)
                pipe.UpdateConnections(false);
            else if (entity is BlockEntityPipeBase pipe2)
                pipe2.UpdateConnections(false);
        }

        public override void OnJsonTesselation(
            ref MeshData sourceMesh,
            ref int[] lightRgbsByCorner,
            BlockPos pos,
            Vintagestory.API.Common.Block[] chunkExtBlocks,
            int extIndex3d)
        {
            base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);

            if (api is not ICoreClientAPI capi)
                return;

            if (capi.World.BlockAccessor.GetBlockEntity(pos) is not IPipeRenderState renderState)
                return;

            MeshData builtMesh = PipeMeshBuilder.Build(
                capi,
                this,
                pos,
                renderState.ConnectedSides,
                renderState.ConnectedToInventory,
                renderState.UseInserterHead);

            if (builtMesh != null)
                sourceMesh = builtMesh;
        }
    }
}