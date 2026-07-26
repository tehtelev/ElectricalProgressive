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
                // Claim + range: do not open/configure foreign or remote pipes.
                if (world.Side == EnumAppSide.Server
                    && be is BlockEntityPipeBase pipeBe
                    && !PipeSecurity.CanPlayerConfigure(world, byPlayer, blockSel.Position))
                {
                    return false;
                }

                if (be is BlockEntityPipeBase ownedPipe)
                    ownedPipe.SetOwnerIfEmpty(byPlayer);

                if (be is BEItemInsertionPipe itempipe)
                    itempipe.OnPlayerRightClick(byPlayer, blockSel);

                if (be is BELiquidInsertionPipe liquidPipe)
                    liquidPipe.OnPlayerRightClick(byPlayer, blockSel);
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
        {
            bool placed = base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack);
            if (placed && byPlayer != null && blockSel?.Position != null
                && world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityPipeBase pipe)
            {
                pipe.ForceSetOwner(byPlayer);
            }

            return placed;
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

            // Lightweight state only — full ToTreeAttributes on filter pipes serializes 18 slots (major hitch).
            ITreeAttribute tree = SaveEntityDataLightweight(world, pos, baseType, nextBaseType);

            // Synchronous SetBlock + re-register. Deferred tasks were racing with old BE.RemovePipe
            // and left wrench-transformed filter pipes outside the network (place-fresh still worked).
            world.BlockAccessor.SetBlock(newBlock.BlockId, pos);

            BlockEntity newEntity = world.BlockAccessor.GetBlockEntity(pos);
            if (newEntity != null && tree != null)
            {
                // Apply owner/settings only — avoid full FromTreeAttributes stomping a healthy init.
                ApplyTransformSettings(newEntity, tree, player);
            }

            if (newEntity is BlockEntityPipeBase pipeBase)
                pipeBase.RefreshConnectionsAfterTransform();
            else if (newEntity is BEPipe normalPipe)
                normalPipe.RefreshConnectionsAfterTransform();
            else
                UpdatePipeConnections(world, pos, updateNeighbors: true);

            UpdateNeighborConnections(world, pos);

            newEntity?.MarkDirty(true);
            world.BlockAccessor.MarkBlockDirty(pos);
            world.PlaySoundAt(new AssetLocation("game:sounds/tool/padlock"),
                pos.X, pos.Y, pos.Z, player);

            return true;
        }

        private static void ApplyTransformSettings(BlockEntity entity, ITreeAttribute tree, IPlayer player)
        {
            if (entity is BlockEntityPipeBase pipeBase)
                pipeBase.SetOwnerIfEmpty(player);

            if (entity is BEItemInsertionPipe itemPipe)
            {
                itemPipe.ApplyWrenchTransformSettings(
                    tree.GetInt("transferRate", itemPipe.TransferRate),
                    (BEItemInsertionPipe.FilterMode)tree.GetInt("filterMode", (int)itemPipe.CurrentFilterMode),
                    tree.GetBool("matchMod", itemPipe.MatchMod),
                    tree.GetBool("matchType", itemPipe.MatchType),
                    tree.GetBool("matchAttributes", itemPipe.MatchAttributes));
            }
            else if (entity is BELiquidInsertionPipe liquidPipe)
            {
                liquidPipe.ApplyWrenchTransformSettings(
                    tree.GetInt("transferRate", liquidPipe.TransferRate),
                    (BELiquidInsertionPipe.FilterMode)tree.GetInt("filterMode", (int)liquidPipe.CurrentFilterMode));
            }
        }

        /// <summary>
        /// Save only what must survive a wrench transform (connections, owner, filter settings).
        /// Avoids serializing the full 18-slot filter inventory (main transform hitch).
        /// </summary>
        private ITreeAttribute SaveEntityDataLightweight(
            IWorldAccessor world,
            BlockPos pos,
            string fromType,
            string toType)
        {
            var currentEntity = world.BlockAccessor.GetBlockEntity(pos);
            if (currentEntity == null)
                return null;

            var tree = new TreeAttribute();

            // Owner only (connections rebuilt live after SetBlock — do not restore stale flags).
            if (currentEntity is BlockEntityPipeBase pipeBase)
                pipeBase.WriteTransformState(tree);

            // Settings that can carry into the next filter pipe type.
            if (toType == "pipe-item-insertion" && currentEntity is BEItemInsertionPipe itemPipe)
            {
                tree.SetInt("transferRate", itemPipe.TransferRate);
                tree.SetInt("filterMode", (int)itemPipe.CurrentFilterMode);
                tree.SetBool("matchMod", itemPipe.MatchMod);
                tree.SetBool("matchType", itemPipe.MatchType);
                tree.SetBool("matchAttributes", itemPipe.MatchAttributes);
            }
            else if (toType == "pipe-liquid-insertion")
            {
                if (currentEntity is BELiquidInsertionPipe liquidPipe)
                {
                    tree.SetInt("transferRate", liquidPipe.TransferRate);
                    tree.SetInt("filterMode", (int)liquidPipe.CurrentFilterMode);
                }
                else if (currentEntity is BEItemInsertionPipe fromItem)
                {
                    // Item → liquid: keep allow/deny mode, reset rate to liquid default.
                    tree.SetInt("transferRate", 100);
                    tree.SetInt("filterMode", (int)fromItem.CurrentFilterMode);
                }
            }

            // New filter pipes always start with an empty filter list after wrench transform.
            if (toType == "pipe-item-insertion" || toType == "pipe-liquid-insertion")
                ClearFilterInventoryForTransform(tree, slotCount: 18);

            return tree;
        }

        /// <summary>
        /// Drops filter slot contents (and item-only match flags) so the next pipe type starts empty.
        /// </summary>
        private static void ClearFilterInventoryForTransform(ITreeAttribute tree, int slotCount)
        {
            if (tree == null)
                return;

            var invTree = new TreeAttribute();
            invTree.SetInt("qslots", slotCount);
            tree["inventory"] = invTree;

            tree.RemoveAttribute("matchMod");
            tree.RemoveAttribute("matchType");
            tree.RemoveAttribute("matchAttributes");
        }

        private void UpdateNeighborConnections(IWorldAccessor world, BlockPos pos)
        {
            for (int i = 0; i < 6; i++)
            {
                BlockPos neighborPos = pos.AddCopy(BlockFacing.ALLFACES[i]);
                if (world.BlockAccessor.GetBlock(neighborPos) is BlockPipeBase)
                    UpdatePipeConnections(world, neighborPos, updateNeighbors: false);
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

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            // Bake Lod2Mesh before first chunk tessellation so the engine runs near+far passes.
            if (api is ICoreClientAPI capi)
                PipeMeshBuilder.EnsureLod2Mesh(capi, this);
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

            // EP style: engine already chose shape vs lod2shape (sourceMesh verts differ).
            bool isLod2 = PipeMeshBuilder.IsLod2Pass(capi, this, sourceMesh);

            MeshData builtMesh = PipeMeshBuilder.Build(
                capi,
                this,
                pos,
                renderState.ConnectedSides,
                renderState.ConnectedToInventory,
                renderState.UseInserterHead,
                isLod2);

            if (builtMesh != null)
                sourceMesh = builtMesh;
        }
    }
}