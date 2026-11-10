using System;
using System.Collections.Generic;
using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Storage.Terminal;

/// <summary>
/// Панель терминала на жиле. В одной клетке по панели на грань.
/// </summary>
public class BlockEStorageTerminal : BlockEStoragePart
{
    private static readonly Dictionary<(Facing, string, int), MeshData> MeshCache = [];
    private static readonly Dictionary<(int, int), MeshData> GuestMeshes = [];
    private static readonly Dictionary<int, (MeshData Solid, MeshData Decal)> GuestDecals = [];
    private static readonly Dictionary<(Facing, string), Cuboidf[]> SelectionBoxesCache = [];
    private static readonly Dictionary<(Facing, string), Cuboidf[]> CollisionBoxesCache = [];

    /// <summary>Панель, интерфейс и шина делят клетку: по одной на грань.</summary>
    public virtual bool Stacks => true;

    private static readonly WorldInteraction[] HeldClip =
    [
        new()
        {
            ActionLangCode = "electricalprogressivestorage:estorage-clip-held",
            MouseButton = EnumMouseButton.Right
        }
    ];

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
    {
        var inherited = base.GetHeldInteractionHelp(inSlot);
        if (inherited == null || inherited.Length == 0)
            return HeldClip;

        var merged = new WorldInteraction[HeldClip.Length + inherited.Length];
        HeldClip.CopyTo(merged, 0);
        inherited.CopyTo(merged, HeldClip.Length);
        return merged;
    }

    private static readonly Facing[] StrandBits =
    [
        Facing.NorthEast, Facing.NorthWest, Facing.NorthUp, Facing.NorthDown,
        Facing.EastNorth, Facing.EastSouth, Facing.EastUp, Facing.EastDown,
        Facing.SouthEast, Facing.SouthWest, Facing.SouthUp, Facing.SouthDown,
        Facing.WestNorth, Facing.WestSouth, Facing.WestUp, Facing.WestDown,
        Facing.UpNorth, Facing.UpEast, Facing.UpSouth, Facing.UpWest,
        Facing.DownNorth, Facing.DownEast, Facing.DownSouth, Facing.DownWest
    ];

    private readonly struct HeldCable
    {
        public Facing Facing { get; init; }
        public Facing Cable { get; init; }
        public int Lines { get; init; }
        public string Code { get; init; }
    }

    private static readonly Dictionary<string, HeldCable> StickPending = [];

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        if (blockSel?.Position == null)
            return false;

        // Клик по кабелю сажает панель в его клетку. Соседний воздух тут не проверяем: игрок стоит как раз в нём.
        if (Stacks && CableCell(world, blockSel, out var cablePos, out _, out _, out _))
        {
            if (byPlayer != null && !world.Claims.TryAccess(byPlayer, cablePos, EnumBlockAccessFlags.BuildOrBreak))
            {
                failureCode = "claimed";
                byPlayer.InventoryManager?.ActiveHotbarSlot?.MarkDirty();
                return false;
            }

            return DoPlaceBlock(world, byPlayer!, blockSel, itemstack);
        }

        if (HostedCell(world, blockSel, out var hostPos, out var hostEntity, out var hosted, out var hostedPrefer))
        {
            if (byPlayer != null && !world.Claims.TryAccess(byPlayer, hostPos, EnumBlockAccessFlags.BuildOrBreak))
            {
                failureCode = "claimed";
                byPlayer.InventoryManager?.ActiveHotbarSlot?.MarkDirty();
                return false;
            }

            return DoPlaceBlock(world, byPlayer!, blockSel, itemstack);
        }

        var selection = new Selection(blockSel);
        Facing facing;
        try
        {
            facing = FacingHelper.From(selection.Face, selection.Direction);
        }
        catch
        {
            return false;
        }

        facing = ResolveFacing(world, blockSel, facing);
        if (!CanStay(world, blockSel.Position, facing))
            return false;
        if (world.BlockAccessor.GetBlock(blockSel.Position) is BlockEStorageTerminal existing)
        {
            if (!Stacks || !existing.Stacks)
                return false;
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is IPanelStack host &&
                (host.Stack.Count >= 6 || PanelStack.FaceTaken(host, facing, this)))
                return false;
        }

        return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }

    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
    {
        if (Stacks && CableCell(world, blockSel, out _, out _, out _, out _))
            return TryStickOnCable(world, byPlayer, blockSel, byItemStack);

        if (HostedCell(world, blockSel, out var hostPos, out var hostEntity, out var hosted, out var hostedPrefer))
        {
            var mount = GuestMount(world, hostPos, hostEntity, hosted, hostedPrefer);
            return mount != Facing.None && hostEntity is IPanelStack seat && seat.Stack.TryAdd(world, hostEntity, this, mount);
        }

        var selection = new Selection(blockSel);
        Facing facing;
        try
        {
            facing = FacingHelper.From(selection.Face, selection.Direction);
        }
        catch
        {
            return false;
        }

        if (world.BlockAccessor.GetBlock(blockSel.Position) is BlockEStorageTerminal existing)
        {
            if (!Stacks || !existing.Stacks)
                return false;
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntity entity || entity is not IPanelStack host)
                return false;
            facing = ResolveFacing(world, blockSel, facing);
            if (!CanStay(world, blockSel.Position, facing) || host.Stack.Count >= 6 || PanelStack.FaceTaken(host, facing, this))
                return false;
            return host.Stack.TryAdd(world, entity, this, facing);
        }

        if (!base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack) ||
            !TryWriteFacing(world.BlockAccessor.GetBlockEntity(blockSel.Position), facing))
        {
            return false;
        }

        return true;
    }

    public override bool IsReplacableBy(Vintagestory.API.Common.Block block)
        => block is BlockEStorageCable || (block is BlockEStorageTerminal panel && panel.Stacks);

    /// <summary>
    /// Вторая панель целится в чужую жилу той же клетки, а не в плиту первой.
    /// </summary>
    public override bool DoPartialSelection(IWorldAccessor world, BlockPos pos) => true;

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        if (world.Side != EnumAppSide.Server)
            return;

        // Пока жила не перенесена на панель, грань ещё верхняя по умолчанию. Снимать по ней нельзя.
        if (StickPending.ContainsKey(StickKey(pos, world.Side)))
            return;

        for (var n = 0; n < 6; n++)
        {
            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntity host || host is not IPanelStack stack)
                return;
            if (world.BlockAccessor.GetBlock(pos) is not BlockEStorageTerminal hostBlock)
                return;
            if (!TryReadFacing(host, out var facing))
                return;

            if (!hostBlock.CanOccupy(world, pos, facing) && MountReady(world, pos, facing))
            {
                if (stack.Stack.Count > 1)
                {
                    stack.Stack.Promote(world, pos, null, 1f);
                    continue;
                }

                world.BlockAccessor.BreakBlock(pos, null);
                return;
            }

            var removed = false;
            var guests = stack.Stack.Guests;
            for (var i = guests.Count - 1; i >= 0; i--)
            {
                if (guests[i].Block is not BlockEStorageTerminal guestBlock)
                    continue;
                var guestFacing = PanelStack.FacingOf(guests[i]);
                if (guestBlock.CanOccupy(world, pos, guestFacing) || !MountReady(world, pos, guestFacing))
                    continue;
                stack.Stack.RemoveAt(world, pos, i, 1f);
                removed = true;
            }

            if (!removed)
                break;
        }

        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntity stayed || stayed is not IPanelHost panel)
            return;
        if (panel.Cable == Facing.None)
            return;

        var face = BlockFacing.FromVector(neibpos.X - pos.X, neibpos.Y - pos.Y, neibpos.Z - pos.Z);
        if (face == null || !BlockEStorageCable.NeighborReady(world, neibpos))
            return;

        var mask = FacingHelper.FromFace(face);
        if ((panel.Cable & mask) == Facing.None)
            return;
        if (BlockEStorageCable.Supported(world.BlockAccessor, pos, mask)
            || PanelStack.MountedAny(world, pos, face)
            || BlockEStorageCable.Continues(world, pos, face))
            return;

        DropHostCable(world, pos, stayed, panel, mask);
    }

    // Опора на границе чанка ещё без блока или без сущности. Снимать панель по такому ответу нельзя.
    private static bool MountReady(IWorldAccessor world, BlockPos pos, Facing facing)
    {
        var support = PanelStack.Support(facing);
        if (support == null)
            return true;
        return BlockEStorageCable.NeighborReady(world, pos.AddCopy(support));
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntity host || host is not IPanelStack stack || host is not IPanelHost panel)
        {
            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
            return;
        }

        var index = 0;
        if (byPlayer?.CurrentBlockSelection is { } sel && sel.Position != null && sel.Position.Equals(pos))
            index = sel.SelectionBoxIndex;

        var many = stack.Stack.Count > 1;
        var cable = panel.Cable != Facing.None;
        if (!many && !cable)
        {
            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
            return;
        }

        if (api is ICoreClientAPI)
            return;

        // Кабель в руке бьёт по жиле под интерфейсом: лишняя снимается, последняя уходит отрезком.
        if (cable && CableInHand(byPlayer)
            && byPlayer?.CurrentBlockSelection is { } aim
            && aim.Position != null && aim.Position.Equals(pos) && aim.HitPosition != null)
        {
            var covered = BlockEStorageCable.CoveredHit(world, panel, aim.HitPosition);
            var open = BlockEStorageCable.HostHit(world, panel, aim.HitPosition);
            if (open == Facing.None && covered != Facing.None)
            {
                TakeStrand(world, pos, host, panel, covered, dropQuantityMultiplier);
                return;
            }

            if (open != Facing.None)
            {
                DropHostCable(world, pos, host, panel, open, dropQuantityMultiplier);
                return;
            }
        }

        RemoveAimed(world, pos, host, stack, panel, index, byPlayer, dropQuantityMultiplier);
    }

    /// <summary>
    /// Прицел на жиле снимает только её. Прицел на панели снимает только панель.
    /// </summary>
    private void RemoveAimed(IWorldAccessor world, BlockPos pos, BlockEntity host, IPanelStack stack, IPanelHost panel, int index, IPlayer? byPlayer, float dropQuantityMultiplier)
    {
        var cable = panel.Cable != Facing.None;
        var panels = PanelBoxCount(stack);
        if (cable && index >= panels)
        {
            var selected = BlockEStorageCable.HostBoxFacing(world, panel, index - panels, true);
            if (selected != Facing.None)
                TakeStrand(world, pos, host, panel, selected, dropQuantityMultiplier);
            return;
        }

        if (stack.Stack.Count <= 1)
        {
            if (cable)
                PeelToCable(world, pos, host, panel, byPlayer, dropQuantityMultiplier);
            else
                base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
            return;
        }

        var panelIndex = PanelIndex(stack, index);
        if (panelIndex <= 0)
            stack.Stack.Promote(world, pos, byPlayer, dropQuantityMultiplier);
        else
            stack.Stack.RemoveAt(world, pos, panelIndex - 1, dropQuantityMultiplier);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        if (world.BlockAccessor.GetBlockEntity(pos) is IPanelHost panel && panel.Cable != Facing.None)
        {
            var count = BlockEStorageCable.HostStrands(panel.Cable, panel.CableLines);
            var cable = BlockEStorageCable.HostBlock(world, panel.CableCode);
            if (count > 0 && cable != null)
            {
                var merged = new ItemStack[(drops?.Length ?? 0) + 1];
                if (drops != null)
                    Array.Copy(drops, merged, drops.Length);
                merged[^1] = new ItemStack(cable, count);
                drops = merged;
            }
        }

        return WithGuestDrops(world, pos, drops, dropQuantityMultiplier);
    }

    private static ItemStack[] WithGuestDrops(IWorldAccessor world, BlockPos pos, ItemStack[]? drops, float dropQuantityMultiplier)
    {
        if (dropQuantityMultiplier <= 0 || world.BlockAccessor.GetBlockEntity(pos) is not IPanelStack stack || stack.Stack.Guests.Count == 0)
            return drops!;

        var extra = 0;
        foreach (var guest in stack.Stack.Guests)
        {
            if (guest.Block != null)
                extra++;
        }

        if (extra == 0)
            return drops ?? [];

        var merged = new ItemStack[(drops?.Length ?? 0) + extra];
        var at = 0;
        if (drops != null)
        {
            Array.Copy(drops, merged, drops.Length);
            at = drops.Length;
        }

        foreach (var guest in stack.Stack.Guests)
        {
            if (guest.Block != null)
                merged[at++] = new ItemStack(guest.Block);
        }

        return merged;
    }

    /// <summary>
    /// Снимается панель. Жилы остаются в клетке кабелем, а не выпадают.
    /// </summary>
    private void PeelToCable(IWorldAccessor world, BlockPos pos, BlockEntity host, IPanelHost panel, IPlayer? byPlayer, float dropQuantityMultiplier)
    {
        var connection = panel.Cable;
        var lines = new int[6];
        var source = panel.CableLines;
        for (var i = 0; i < lines.Length && i < source.Length; i++)
            lines[i] = source[i];

        var cable = BlockEStorageCable.HostBlock(world, panel.CableCode);
        if (cable == null || connection == Facing.None)
        {
            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
            return;
        }

        if (dropQuantityMultiplier > 0 && host.Block != null)
            world.SpawnItemEntity(new ItemStack(host.Block), pos.ToVec3d().Add(0.5, 0.5, 0.5));

        host.OnBlockBroken(byPlayer);
        BlockEntityEStorageCable.QueueRestore(pos, world.Side, connection, BlockEStorageCable.Pack(lines));
        world.BlockAccessor.SetBlock(cable.Id, pos);
        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityEStorageCable placed)
        {
            placed.ApplyRestore();
            placed.MarkDirty(true);
        }
        else
        {
            BlockEntityEStorageCable.ForgetRestore(pos, world.Side);
            var count = BlockEStorageCable.HostStrands(connection, lines);
            if (dropQuantityMultiplier > 0 && count > 0)
                world.SpawnItemEntity(new ItemStack(cable, count), pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }

        StorageAccess.DirtyTopology();
    }

    private static void DropHostCable(IWorldAccessor world, BlockPos pos, BlockEntity host, IPanelHost panel, Facing bits, float dropQuantityMultiplier = 1)
    {
        var removed = panel.Cable & bits;
        if (removed == Facing.None)
            return;

        var code = panel.CableCode;
        var count = BlockEStorageCable.HostStrands(removed, panel.CableLines);
        panel.Cable &= ~bits;
        BlockEStorageCable.ClearHostFaces(panel.Cable, panel.CableLines, removed);
        if (panel.Cable == Facing.None)
            panel.CableCode = "";
        host.MarkDirty(true);
        StorageAccess.DirtyTopology();
        var cable = BlockEStorageCable.HostBlock(world, code);
        if (dropQuantityMultiplier > 0 && count > 0 && cable != null)
            world.SpawnItemEntity(new ItemStack(cable, count), pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }

    private static void TakeStrand(IWorldAccessor world, BlockPos pos, BlockEntity host, IPanelHost panel, Facing selected, float dropQuantityMultiplier = 1)
    {
        var dropped = StorageAccess.LowerHostWire(panel.Cable, panel.CableLines, selected);
        if (dropped <= 0)
        {
            DropHostCable(world, pos, host, panel, selected, dropQuantityMultiplier);
            return;
        }

        var code = panel.CableCode;
        host.MarkDirty(true);
        StorageAccess.DirtyTopology();
        var cable = BlockEStorageCable.HostBlock(world, code);
        if (dropQuantityMultiplier > 0 && cable != null)
            world.SpawnItemEntity(new ItemStack(cable, dropped), pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        => MergedBoxes(blockAccessor, pos, false);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        var reveal = CableInHand();
        var boxes = reveal ? OpenBoxes(blockAccessor, pos) : MergedBoxes(blockAccessor, pos, true);
        if (api?.World == null
            || blockAccessor.GetBlockEntity(pos) is not IPanelHost panel
            || panel.Cable == Facing.None)
            return boxes;

        // Жилы всегда в прицеле, даже под панелью: слом бьёт по той коробке, на которую смотришь.
        var extra = BlockEStorageCable.HostBoxes(api.World, panel, true);
        if (extra.Length == 0)
            return boxes;

        var merged = new Cuboidf[boxes.Length + extra.Length];
        Array.Copy(boxes, merged, boxes.Length);
        Array.Copy(extra, 0, merged, boxes.Length, extra.Length);
        return merged;
    }

    // С кабелем в руке панели интерфейса и шины не перехватывают прицел: целится жила.
    private Cuboidf[] OpenBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        if (blockAccessor.GetBlockEntity(pos) is not IPanelStack stack || stack is not BlockEntity host)
            return MergedBoxes(blockAccessor, pos, true);

        var list = new List<Cuboidf>();
        AddOpen(host, list);
        foreach (var guest in stack.Stack.Guests)
            AddOpen(guest, list);
        return list.ToArray();
    }

    private static void AddOpen(BlockEntity panel, List<Cuboidf> list)
    {
        if (panel.Block?.FirstCodePart() is "estorageinterface" or "estorageimport" or "estorageexport")
            return;
        if (panel.Block is not BlockEStorageTerminal block)
            return;
        foreach (var box in block.BoxesFor(PanelStack.FacingOf(panel), true))
            list.Add(box.Clone());
    }

    private bool CableInHand()
        => api is ICoreClientAPI capi && CableInHand(capi.World?.Player);

    private static bool CableInHand(IPlayer? byPlayer)
        => byPlayer?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Block is BlockEStorageCable;

    public Cuboidf[] BoxesFor(Facing facing, bool selection)
    {
        var source = selection ? SelectionBoxes : CollisionBoxes;
        var cache = selection ? SelectionBoxesCache : CollisionBoxesCache;
        return CachedBoxes(facing, Code?.ToString() ?? "", source, cache);
    }

    private Cuboidf[] MergedBoxes(IBlockAccessor blockAccessor, BlockPos pos, bool selection)
    {
        var facing = Facing.UpNorth;
        var code = Code?.ToString() ?? "";
        var entity = blockAccessor.GetBlockEntity(pos);
        if (TryReadFacing(entity, out var read))
        {
            facing = read;
            code = entity?.Block?.Code?.ToString() ?? code;
        }

        var cache = selection ? SelectionBoxesCache : CollisionBoxesCache;
        var own = CachedBoxes(facing, code, selection ? SelectionBoxes : CollisionBoxes, cache);
        if (entity is not IPanelStack stack || stack.Stack.Guests.Count == 0)
            return own;

        var list = new List<Cuboidf>(own.Length + stack.Stack.Guests.Count);
        foreach (var box in own)
            list.Add(box.Clone());
        foreach (var guest in stack.Stack.Guests)
        {
            if (guest.Block is not BlockEStorageTerminal block)
                continue;
            foreach (var box in block.BoxesFor(PanelStack.FacingOf(guest), selection))
                list.Add(box.Clone());
        }

        return list.ToArray();
    }

    private static Cuboidf[] CachedBoxes(Facing facing, string code, Cuboidf[] sourceBoxes, Dictionary<(Facing, string), Cuboidf[]> cache)
    {
        if (sourceBoxes == null || sourceBoxes.Length == 0)
            return [];

        if (!cache.TryGetValue((facing, code), out var boxes))
        {
            boxes = (Cuboidf[])sourceBoxes.Clone();
            FacingRotations.ApplyRotations(boxes, facing);
            cache.TryAdd((facing, code), boxes);
        }

        return boxes;
    }

    public override void GetDecal(IWorldAccessor world, BlockPos pos, ITexPositionSource decalTexSource, ref MeshData decalModelData, ref MeshData blockModelData)
    {
        // Форма панели лежит горизонтально. Без того же поворота, что у модели, трещина остаётся на горизонтали.
        var facing = Facing.UpNorth;
        var entity = world.BlockAccessor.GetBlockEntity(pos);
        if (TryReadFacing(entity, out var read))
            facing = read;

        if (decalModelData != null)
            FacingRotations.ApplyRotations(decalModelData, facing);
        if (blockModelData != null)
        {
            blockModelData = blockModelData.Clone();
            FacingRotations.ApplyRotations(blockModelData, facing);
        }

        if (entity is not IPanelStack stack || world.Api is not ICoreClientAPI capi || decalModelData == null || blockModelData == null)
            return;

        foreach (var guest in stack.Stack.Guests)
        {
            if (guest.Block is not BlockEStorageTerminal)
                continue;
            if (!GuestDecal(capi, guest.Block, decalTexSource, out var solid, out var decal))
                continue;

            var guestFacing = PanelStack.FacingOf(guest);
            FacingRotations.ApplyRotations(solid, guestFacing);
            FacingRotations.ApplyRotations(decal, guestFacing);
            blockModelData.AddMeshData(solid);
            decalModelData.AddMeshData(decal);
        }
    }

    private static bool GuestDecal(ICoreClientAPI capi, Vintagestory.API.Common.Block block, ITexPositionSource tex, out MeshData solid, out MeshData decal)
    {
        solid = null!;
        decal = null!;
        if (block.Shape == null)
            return false;

        if (!GuestDecals.TryGetValue(block.Id, out var cached))
        {
            var body = capi.TesselatorManager.GetDefaultBlockMesh(block);
            try
            {
                capi.Tesselator.TesselateShape("estoragepanel", block.Code, block.Shape, out MeshData cracked, tex);
                capi.TesselatorManager.ThreadDispose();
                if (body == null || cracked == null || body.VerticesCount == 0 || body.VerticesCount != cracked.VerticesCount)
                    return false;
                cached = (body, cracked);
                GuestDecals[block.Id] = cached;
            }
            catch (Exception)
            {
                capi.TesselatorManager.ThreadDispose();
                return false;
            }
        }

        solid = cached.Solid.Clone();
        decal = cached.Decal.Clone();
        return solid.VerticesCount > 0 && solid.VerticesCount == decal.VerticesCount;
    }

    public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
    {
        base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);
        if (api is not ICoreClientAPI)
            return;

        var facing = Facing.UpNorth;
        var code = Code?.ToString() ?? "";
        var entity = api.World.BlockAccessor.GetBlockEntity(pos);
        if (TryReadFacing(entity, out var read))
        {
            facing = read;
            code = entity?.Block?.Code?.ToString() ?? code;
        }

        var cacheKey = (facing, code, sourceMesh.VerticesCount);
        if (!MeshCache.TryGetValue(cacheKey, out var meshData))
        {
            meshData = sourceMesh.Clone();
            FacingRotations.ApplyRotations(meshData, facing);
            MeshCache.TryAdd(cacheKey, meshData);
        }

        var drawn = meshData.Clone();
        if (entity is IPanelStack stack && api is ICoreClientAPI capi)
        {
            foreach (var guest in stack.Stack.Guests)
            {
                if (guest.Block == null)
                    continue;
                var extra = GuestMesh(capi, guest.Block, PanelStack.FacingOf(guest));
                if (extra != null)
                    drawn.AddMeshData(extra);
            }
        }

        sourceMesh = drawn;
        if (entity is IPanelHost panel && panel.Cable != Facing.None && api is ICoreClientAPI client)
            BlockEStorageCable.Append(client, pos, panel, ref sourceMesh);
    }

    private static MeshData? GuestMesh(ICoreClientAPI capi, Vintagestory.API.Common.Block block, Facing facing)
    {
        var key = (block.Id, (int)facing);
        if (!GuestMeshes.TryGetValue(key, out var mesh))
        {
            mesh = capi.TesselatorManager.GetDefaultBlockMesh(block)?.Clone();
            if (mesh == null || mesh.VerticesCount == 0)
                return null;
            FacingRotations.ApplyRotations(mesh, facing);
            GuestMeshes[key] = mesh;
        }

        return mesh.Clone();
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);
        MeshCache.Clear();
        GuestMeshes.Clear();
        GuestDecals.Clear();
        SelectionBoxesCache.Clear();
        CollisionBoxesCache.Clear();
    }

    public bool CanOccupy(IWorldAccessor world, BlockPos pos, Facing facing)
        => CanStay(world, pos, facing);

    /// <summary>
    /// Опора — жила в этой клетке на грани панели и стена под ней.
    /// Соседний кабель остаётся запасным путём, если панель встала в соседнюю клетку.
    /// </summary>
    protected virtual bool CanStay(IWorldAccessor world, BlockPos pos, Facing facing)
    {
        var support = PanelStack.Support(facing);
        if (support == null)
            return false;

        if (world.BlockAccessor.GetBlockEntity(pos) is IPanelHost panel
            && (panel.Cable & FacingHelper.FromFace(support)) != Facing.None
            && BlockEStorageCable.Supported(world.BlockAccessor, pos, facing))
            return true;

        var neighborPos = pos.AddCopy(support);
        if (world.BlockAccessor.GetBlock(neighborPos) is not BlockEStorageCable)
            return false;
        if (world.BlockAccessor.GetBlockEntity(neighborPos) is not BlockEntityEStorageCable cable)
            return false;

        var touched = FacingHelper.FromFace(support.Opposite);
        return touched != Facing.None && (cable.Connection & touched) != Facing.None;
    }

    private static string StickKey(BlockPos pos, EnumAppSide side)
        => pos.X + "/" + pos.Y + "/" + pos.Z + "/" + pos.dimension + "/" + (int)side;

    private static bool CableCell(IWorldAccessor world, BlockSelection blockSel, out BlockPos pos, out BlockEntityEStorageCable entity, out BlockEStorageCable cable, out Facing prefer)
    {
        pos = null!;
        entity = null!;
        cable = null!;
        prefer = Facing.None;
        if (blockSel?.Position == null || blockSel.Face == null)
            return false;

        // Клик по самой жиле. Номер хитбокса — эта грань, даже если в центре коробки налезают друг на друга.
        var aimed = blockSel.DidOffset ? blockSel.Position.AddCopy(blockSel.Face.Opposite) : blockSel.Position.Copy();
        if (ReadCable(world, aimed, out pos, out entity, out cable))
        {
            prefer = BlockEStorageCable.BoxFacing(entity.Connection, entity.Lines, cable.Id, blockSel.SelectionBoxIndex);
            return true;
        }

        // Клик по опоре. Жернова replaceable 600, поэтому прицел остаётся на них, а кабель лежит в соседней клетке.
        // У сплошного сундука прицел уже смещён в клетку кабеля, и первая проверка смотрит обратно на сундук.
        var cell = blockSel.DidOffset ? blockSel.Position.Copy() : blockSel.Position.AddCopy(blockSel.Face);
        var toward = FacingHelper.FromFace(blockSel.Face.Opposite);
        if (!ReadCable(world, cell, out pos, out entity, out cable) || (entity.Connection & toward) == Facing.None)
        {
            pos = null!;
            entity = null!;
            cable = null!;
            return false;
        }

        prefer = toward;
        return true;
    }

    /// <summary>
    /// Кабель уже впитан панелью. Вторая грань к другому сборщику сажается гостем, а не заменой клетки.
    /// </summary>
    private bool HostedCell(IWorldAccessor world, BlockSelection blockSel, out BlockPos pos, out BlockEntity host, out IPanelHost panel, out Facing prefer)
    {
        pos = null!;
        host = null!;
        panel = null!;
        prefer = Facing.None;
        if (!Stacks || blockSel?.Position == null || blockSel.Face == null)
            return false;

        var aimed = blockSel.DidOffset ? blockSel.Position.AddCopy(blockSel.Face.Opposite) : blockSel.Position.Copy();
        if (ReadHost(world, aimed, out pos, out host, out panel) && host is IPanelStack seated)
        {
            var boxes = PanelBoxCount(seated);
            if (blockSel.SelectionBoxIndex < boxes)
                return false;
            prefer = BlockEStorageCable.HostBoxFacing(world, panel, blockSel.SelectionBoxIndex - boxes, true);
            return prefer != Facing.None;
        }

        var cell = blockSel.DidOffset ? blockSel.Position.Copy() : blockSel.Position.AddCopy(blockSel.Face);
        var toward = FacingHelper.FromFace(blockSel.Face.Opposite);
        if (!ReadHost(world, cell, out pos, out host, out panel) || (panel.Cable & toward) == Facing.None)
            return false;

        prefer = toward;
        return true;
    }

    private Facing GuestMount(IWorldAccessor world, BlockPos pos, BlockEntity host, IPanelHost panel, Facing prefer)
    {
        if (host is not IPanelStack seat)
            return Facing.None;
        var mount = MountOf(world, pos, panel.Cable, prefer);
        if (mount == Facing.None || (prefer != Facing.None && (mount & prefer) == Facing.None))
            return Facing.None;
        if (seat.Stack.Count >= 6 || PanelStack.FaceTaken(seat, mount, this) || !CanStay(world, pos, mount))
            return Facing.None;
        return mount;
    }

    private static int PanelBoxCount(IPanelStack stack)
    {
        var count = 0;
        if (stack is BlockEntity host && host.Block is BlockEStorageTerminal block)
            count += BoxCount(block);
        foreach (var guest in stack.Stack.Guests)
        {
            if (guest.Block is BlockEStorageTerminal guestBlock)
                count += BoxCount(guestBlock);
        }

        return count;
    }

    /// <summary>0 — хозяин, дальше гости. Номер коробки, не номер жилы.</summary>
    private static int PanelIndex(IPanelStack stack, int boxIndex)
    {
        var cursor = 0;
        if (stack is BlockEntity host && host.Block is BlockEStorageTerminal block)
        {
            cursor = BoxCount(block);
            if (boxIndex < cursor)
                return 0;
        }

        var guestAt = 0;
        foreach (var guest in stack.Stack.Guests)
        {
            var count = guest.Block is BlockEStorageTerminal guestBlock ? BoxCount(guestBlock) : 1;
            if (boxIndex < cursor + count)
                return guestAt + 1;
            cursor += count;
            guestAt++;
        }

        return 0;
    }

    private static int BoxCount(BlockEStorageTerminal block)
        => block.SelectionBoxes == null || block.SelectionBoxes.Length == 0 ? 1 : block.SelectionBoxes.Length;

    private static bool ReadHost(IWorldAccessor world, BlockPos pos, out BlockPos hostPos, out BlockEntity host, out IPanelHost panel)
    {
        hostPos = null!;
        host = null!;
        panel = null!;
        if (world.BlockAccessor.GetBlock(pos) is not BlockEStorageTerminal)
            return false;
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntity entity || entity is not IPanelHost found || found.Cable == Facing.None)
            return false;

        hostPos = pos;
        host = entity;
        panel = found;
        return true;
    }

    private static bool ReadCable(IWorldAccessor world, BlockPos pos, out BlockPos cablePos, out BlockEntityEStorageCable entity, out BlockEStorageCable cable)
    {
        cablePos = null!;
        entity = null!;
        cable = null!;
        if (world.BlockAccessor.GetBlock(pos) is not BlockEStorageCable block)
            return false;
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageCable found || found.Connection == Facing.None)
            return false;

        cablePos = pos;
        entity = found;
        cable = block;
        return true;
    }

    /// <summary>Один бит жилы под прицелом. Чужая грань той же клетки не отменяет опору.</summary>
    private Facing MountOf(IWorldAccessor world, BlockPos pos, Facing connection, Facing hit)
    {
        Facing Solid(Facing bits)
        {
            foreach (var bit in StrandBits)
            {
                if ((bits & bit) == 0)
                    continue;
                if (!BlockEStorageCable.Supported(world.BlockAccessor, pos, bit))
                    continue;
                if (!MountsOn(world, pos, bit))
                    continue;
                if (world.BlockAccessor.GetBlockEntity(pos) is IPanelStack seated && PanelStack.FaceTaken(seated, bit, this))
                    continue;
                return bit;
            }

            return Facing.None;
        }

        if (hit != Facing.None)
        {
            var both = Facing.None;
            foreach (var bit in StrandBits)
            {
                if ((hit & bit) != 0 && (connection & bit) != 0)
                    both |= bit;
            }

            var chosen = Solid(both);
            if (chosen != Facing.None)
                return chosen;

            foreach (var face in FacingHelper.Faces(hit))
            {
                chosen = Solid(connection & FacingHelper.FromFace(face));
                if (chosen != Facing.None)
                    return chosen;
            }

            // Прицел уже на конкретной жиле. Другой сборщик в этой клетке не подменяет её.
            return Facing.None;
        }

        return Solid(connection);
    }

    /// <summary>Клик по чужой жиле в клетке панели сажает интерфейс или шину на эту грань.</summary>
    private Facing ResolveFacing(IWorldAccessor world, BlockSelection blockSel, Facing facing)
    {
        if (this is not (BlockEStorageInterface or BlockEStorageBus))
            return facing;
        if (blockSel.HitPosition == null)
            return facing;
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not IPanelStack stack || stack is not IPanelHost cableHost)
            return facing;
        if (cableHost.Cable == Facing.None || blockSel.SelectionBoxIndex < stack.Stack.Count)
            return facing;

        var hit = BlockEStorageCable.HostHit(world, cableHost, blockSel.HitPosition);
        var mount = MountOf(world, blockSel.Position, cableHost.Cable, hit);
        return mount == Facing.None ? facing : mount;
    }

    /// <summary>Терминал садится на любую жилу. Шина и интерфейс требуют сундук или машину под ней.</summary>
    protected virtual bool MountsOn(IWorldAccessor world, BlockPos pos, Facing facing) => true;

    protected static bool ContainerMount(IWorldAccessor world, BlockPos pos, Facing facing)
    {
        var support = PanelStack.Support(facing);
        if (support == null)
            return false;

        var neighborPos = pos.AddCopy(support);
        if (StorageAccess.IsPart(world.BlockAccessor.GetBlock(neighborPos)))
            return false;

        var entity = world.BlockAccessor.GetBlockEntity(neighborPos);
        return entity is BlockEntityContainer or IBlockEntityContainer;
    }

    private static void ApplyStick(IPanelHost entity, HeldCable held)
    {
        entity.Facing = held.Facing;
        entity.Cable = held.Cable;
        var lines = entity.CableLines;
        for (var n = 0; n < lines.Length; n++)
            lines[n] = (held.Lines >> (n * 3)) & 7;
        entity.CableCode = string.IsNullOrEmpty(held.Code) ? "estoragecable" : held.Code;
    }

    internal static void ClaimStick(BlockEntity entity)
    {
        if (entity is not IPanelHost host || entity.Api?.World == null || entity.Pos == null)
            return;
        if (!ReferenceEquals(entity.Api.World.BlockAccessor.GetBlockEntity(entity.Pos), entity))
            return;
        if (!StickPending.Remove(StickKey(entity.Pos, entity.Api.Side), out var held))
            return;
        ApplyStick(host, held);
    }

    /// <summary>
    /// Панель встаёт в клетку кабеля и забирает его жилы. SetBlock не ломает блок, поэтому кабель не выпадает.
    /// Предмет терминала списывает сама игра.
    /// </summary>
    private bool TryStickOnCable(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
    {
        if (!Stacks || !CableCell(world, blockSel, out var cablePos, out var entity, out var cable, out var prefer))
            return false;
        if (byPlayer != null && !world.Claims.TryAccess(byPlayer, cablePos, EnumBlockAccessFlags.BuildOrBreak))
            return false;

        var hit = prefer != Facing.None
            ? prefer
            : blockSel.HitPosition == null
                ? Facing.None
                : BlockEStorageCable.StrandHit(cable, entity.Connection, entity.Lines, blockSel.HitPosition);
        var mount = MountOf(world, cablePos, entity.Connection, hit);
        if (mount == Facing.None)
            return false;
        // Клик по жерновам сажает только жилу этой грани, а не другую жилу той же клетки.
        if (prefer != Facing.None && (mount & prefer) == Facing.None)
            return false;

        var key = StickKey(cablePos, world.Side);
        StickPending[key] = new HeldCable
        {
            Facing = mount,
            Cable = entity.Connection,
            Lines = entity.PackedLines(),
            Code = cable.Code?.Path ?? "estoragecable"
        };

        world.BlockAccessor.SetBlock(Id, cablePos, byItemStack);
        if (world.BlockAccessor.GetBlock(cablePos).Id != Id
            || world.BlockAccessor.GetBlockEntity(cablePos) is not BlockEntity placed
            || placed is not IPanelHost host)
        {
            StickPending.Remove(key);
            return false;
        }

        if (StickPending.Remove(key, out var left))
            ApplyStick(host, left);

        placed.MarkDirty(true);
        StorageAccess.DirtyTopology();
        return true;
    }

    private static bool TryReadFacing(BlockEntity? entity, out Facing facing)
    {
        switch (entity)
        {
            case BlockEntityEStorageTerminal terminal when terminal.Facing != Facing.None:
                facing = terminal.Facing;
                return true;
            case BlockEntityEStorageInterface panel when panel.Facing != Facing.None:
                facing = panel.Facing;
                return true;
            case BlockEntityEStorageBus bus when bus.Facing != Facing.None:
                facing = bus.Facing;
                return true;
            default:
                facing = Facing.UpNorth;
                return false;
        }
    }

    private static bool TryWriteFacing(BlockEntity? entity, Facing facing)
    {
        switch (entity)
        {
            case BlockEntityEStorageTerminal terminal:
                terminal.Facing = facing;
                terminal.MarkDirty(true);
                return true;
            case BlockEntityEStorageInterface panel:
                panel.Facing = facing;
                panel.MarkDirty(true);
                return true;
            case BlockEntityEStorageBus bus:
                bus.Facing = facing;
                bus.MarkDirty(true);
                return true;
            default:
                return false;
        }
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        // Присед с предметом в руке иначе отдаёт клик предмету, а не панели.
        PlacedPriorityInteract = true;
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        var inherited = base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        var stacks = BlockEStorageCable.WrenchStacks(world);
        if (stacks.Length == 0)
            return inherited ?? [];

        var list = new List<WorldInteraction>();
        if (inherited != null)
            list.AddRange(inherited);
        list.Add(new WorldInteraction
        {
            ActionLangCode = "electricalprogressivestorage:estorage-unclip",
            MouseButton = EnumMouseButton.Right,
            Itemstacks = stacks
        });
        return list.ToArray();
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel?.Position == null)
            return false;

        if (BlockEStorageCable.HoldingWrench(byPlayer))
        {
            if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
                return false;
            if (world.Side == EnumAppSide.Server)
                Unclip(world, byPlayer, blockSel);
            return true;
        }

        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not IPanelStack stack || stack is not BlockEntity host)
            return false;

        if (host is IPanelHost cableHost && cableHost.Cable != Facing.None
            && CableInHand(byPlayer) && blockSel.HitPosition != null
            && BlockEStorageCable.HostHit(world, cableHost, blockSel.HitPosition, true) != Facing.None)
            return false;

        var index = blockSel.SelectionBoxIndex;
        if (index >= stack.Stack.Count)
            return false;

        var panel = stack.Stack.At(host, index);
        // Кабель в руке наращивает жилу под интерфейсом или шиной, а не открывает их.
        if (panel is BlockEntityEStorageInterface or BlockEntityEStorageBus
            && byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Block is BlockEStorageCable)
            return false;
        if (panel is BlockEntityEStorageInterface face)
            return face.OnPlayerRightClick(byPlayer, blockSel);

        if (panel is BlockEntityEStorageBus bus)
            return bus.OnPlayerRightClick(byPlayer, blockSel);

        if (panel is not BlockEntityEStorageTerminal terminal)
            return false;

        var link = StorageAccess.Link(world, blockSel.Position);
        if (link != StorageLink.Online)
        {
            if (world.Api is ICoreClientAPI capi)
            {
                capi.TriggerIngameError(this, "estorage", Lang.Get(LinkKey(link)));
            }

            return true;
        }

        return terminal.OnPlayerRightClick(byPlayer, blockSel);
    }

    /// <summary>Ключ на ПКМ снимает коробку под прицелом и не открывает окно.</summary>
    private void Unclip(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        var pos = blockSel.Position;
        var drop = BlockEStorageCable.BreakDrop(byPlayer);
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntity host || host is not IPanelStack stack || host is not IPanelHost panel)
        {
            base.OnBlockBroken(world, pos, byPlayer, drop);
            return;
        }

        RemoveAimed(world, pos, host, stack, panel, blockSel.SelectionBoxIndex, byPlayer, drop);
    }

    public static string LinkKey(StorageLink link) => link switch
    {
        StorageLink.Conflict => "electricalprogressivestorage:estorage-conflict",
        StorageLink.NoPower => "electricalprogressivestorage:estorage-nopower",
        StorageLink.NoChannel => "electricalprogressivestorage:estorage-nochannel",
        StorageLink.NoController => "electricalprogressivestorage:estorage-nocontroller",
        _ => "electricalprogressivestorage:estorage-online"
    };
}

/// <summary>
/// Предметный терминал: впитанный кабель и окно сети.
/// </summary>
public class BlockEntityEStorageTerminal : BlockEntityOpenableContainer, IPanelHost, IPanelStack
{
    private readonly InventoryEStorageTerminal _inventory;
    public Facing Facing = Facing.UpNorth;
    public Facing Cable = Facing.None;
    public readonly int[] CableLines = new int[6];
    public string CableCode = "";
    public PanelStack Stack { get; } = new();
    public string Seat { get; set; } = "";

    Facing IPanelHost.Facing { get => Facing; set => Facing = value; }
    Facing IPanelHost.Cable { get => Cable; set => Cable = value; }
    int[] IPanelHost.CableLines => CableLines;
    string IPanelHost.CableCode { get => CableCode; set => CableCode = value; }
    private long _seenContent = -1;
    private bool _settled;
    private bool _pushed;

    public BlockEntityEStorageTerminal()
    {
        _inventory = CreateInventory();
    }

    protected virtual InventoryEStorageTerminal CreateInventory() => new();

    protected virtual bool Liquid => false;

    protected virtual bool Pattern => false;

    protected virtual bool Craft => false;

    public override InventoryBase Inventory => _inventory;

    public override string InventoryClassName => Liquid ? "estorageliquidterminal" : "estorageterminal";

    public virtual bool Holds(string inventoryId) => inventoryId == _inventory.InventoryID;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        PanelStack.ClaimPromote(this);
        BlockEStorageTerminal.ClaimStick(this);
        _inventory.Origin = Pos.Copy();
        _inventory.AfterRebuild = () =>
        {
            if (Api?.Side == EnumAppSide.Server)
                MarkDirty(false);
        };
        var prefix = Liquid ? "estorageliquidterminal-" : "estorageterminal-";
        _inventory.LateInitialize(prefix + Pos.X + "/" + Pos.Y + "/" + Pos.Z + Seat, api);
        Stack.SpawnPending(api.World, Pos);
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(_ => RefreshIfNeeded(), 500);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("facing", (int)Facing);
        tree.SetInt("cable", (int)Cable);
        tree.SetInt("cableLines", BlockEStorageCable.Pack(CableLines));
        tree.SetString("cableCode", CableCode ?? "");
        Stack.Write(tree);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        var raw = tree.GetInt("facing", (int)Facing.UpNorth);
        Facing = raw == 0 ? Facing.UpNorth : (Facing)raw;
        var cable = (Facing)tree.GetInt("cable");
        var packedLines = tree.GetInt("cableLines");
        var cableCode = tree.GetString("cableCode", "");
        if (cable != Facing.None && string.IsNullOrEmpty(cableCode))
            cableCode = "estoragecable";
        var hadCode = CableCode ?? "";
        if (Cable != Facing.None && string.IsNullOrEmpty(hadCode))
            hadCode = "estoragecable";
        if (cable != Cable || packedLines != BlockEStorageCable.Pack(CableLines) || cableCode != hadCode)
            StorageAccess.DirtyTopology();
        Cable = cable;
        for (var n = 0; n < CableLines.Length; n++)
            CableLines[n] = (packedLines >> (n * 3)) & 7;
        CableCode = cableCode;
        Stack.Read(tree, worldAccessForResolve, this);
    }

    private void RefreshIfNeeded()
    {
        if (Api.World == null)
            return;

        var scan = StorageAccess.GetScan(Api.World, Pos);
        var settled = scan.Pending.Count == 0;
        var generation = StorageAccess.ContentGeneration;
        if (_pushed && _seenContent == generation && _settled == settled)
            return;

        var becameSettled = settled && !_settled;
        _seenContent = generation;
        _settled = settled;
        var changed = _inventory.Rebuild();
        // Первый проход в Initialize слишком ранний: диски ещё без предмета, и клиент запоминает пустое окно.
        // Сам блок терминала от содержимого не зависит, перерисовка чанка мигает свечением соседей.
        if (changed || !_pushed || becameSettled)
        {
            MarkDirty(false);
            _pushed = true;
        }
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.World != null && StorageAccess.Link(Api.World, Pos) != StorageLink.Online)
            return false;

        // И сервер, и клиент собирают вид с дисков прямо сейчас. Иначе окно открывается пустым,
        // пока в терминал что-нибудь не положат.
        _inventory.Rebuild();

        if (Api.Side == EnumAppSide.Server)
        {
            MarkDirty(true);
            byPlayer.InventoryManager.OpenInventory(_inventory);
        }

        if (Api.Side == EnumAppSide.Client)
        {
            var title = Craft
                ? "electricalprogressivestorage:estorage-craft-title"
                : Pattern
                    ? "electricalprogressivestorage:estorage-pattern-title"
                    : Liquid
                        ? "electricalprogressivestorage:estorage-liquid-title"
                        : "electricalprogressivestorage:estorage-terminal-title";
            var liquid = Liquid;
            var pattern = Pattern;
            var craft = Craft;
            toggleInventoryDialogClient(byPlayer, () =>
                new GuiDialogEStorageTerminal(
                    Lang.Get(title),
                    Inventory,
                    Pos,
                    (ICoreClientAPI)Api,
                    liquid,
                    pattern,
                    craft));
        }

        return true;
    }

    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
    {
        if (Stack.Forward(this, fromPlayer, ref packetid, data))
            return;

        if (packetid == StorageAccess.OrderPacketId && Api.Side == EnumAppSide.Server && data is { Length: > 0 })
        {
            Order(fromPlayer, data);
            return;
        }

        if (Liquid && packetid == StorageAccess.LiquidPacketId && Api.Side == EnumAppSide.Server && Api.World != null && data is { Length: >= 8 })
        {
            var mouse = fromPlayer.InventoryManager?.MouseItemSlot;
            if (mouse != null)
            {
                var oneLitre = BitConverter.ToInt32(data, 0) != 0;
                var slot = BitConverter.ToInt32(data, 4);
                ItemStack? hovered = null;
                if (slot >= 0 && slot < _inventory.Count)
                    hovered = _inventory[slot].Itemstack;
                StorageAccess.TransferContainer(Api.World, Pos, fromPlayer, mouse, hovered, oneLitre);
                _inventory.Rebuild();
                MarkDirty(true);
            }

            return;
        }

        base.OnReceivedClientPacket(fromPlayer, packetid, data);
    }

    private void Order(IPlayer player, byte[] data)
    {
        var reply = new EStorageOrderReply();
        if (Api.World == null)
        {
            reply.Failed = true;
            reply.Text = Lang.Get("electricalprogressivestorage:estorage-order-none");
            EStorageOrderSync.Send(player, reply);
            return;
        }

        var link = StorageAccess.Link(Api.World, Pos);
        if (link != StorageLink.Online)
        {
            reply.Failed = true;
            reply.Text = Lang.Get(BlockEStorageTerminal.LinkKey(link));
            EStorageOrderSync.Send(player, reply);
            return;
        }

        var tree = new TreeAttribute();
        try
        {
            tree.FromBytes(data);
        }
        catch
        {
            reply.Failed = true;
            EStorageOrderSync.Send(player, reply);
            return;
        }

        var count = tree.GetInt("count");
        if (count <= 0 || tree["out"] is not ItemstackAttribute { value: { } proto })
        {
            reply.Failed = true;
            reply.Text = Lang.Get("electricalprogressivestorage:estorage-order-none");
            EStorageOrderSync.Send(player, reply);
            return;
        }

        proto.ResolveBlockOrItem(Api.World);
        proto.Attributes.RemoveAttribute(StorageAccess.CraftGhostKey);
        proto.Attributes.RemoveAttribute(StorageAccess.OrderKey);
        if (proto.Collectible == null)
        {
            reply.Failed = true;
            reply.Text = Lang.Get("electricalprogressivestorage:estorage-order-none");
            EStorageOrderSync.Send(player, reply);
            return;
        }

        var plan = CraftPlan.Build(Api.World, Pos, proto, count, player);
        reply.Text = plan.Text;
        reply.Bytes = plan.Bytes;
        reply.Cells = plan.Cells ?? [];
        reply.NoCpu = plan.NoCpu;
        reply.Failed = plan.Failed;
        if (tree.GetBool("start") && !plan.Failed && !plan.NoCpu)
        {
            if (CraftPlan.Place(Api.World, plan))
                reply.Started = true;
            else
                reply.NoCpu = true;
        }

        EStorageOrderSync.Send(player, reply);
    }

    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        Stack.ReleaseContents(byPlayer);
        _inventory.Rebuilding = true;
        for (var i = 0; i < _inventory.Count; i++)
            _inventory[i].Itemstack = null;
        _inventory.Rebuilding = false;
        base.OnBlockBroken(byPlayer);
    }

    public override void OnBlockRemoved()
    {
        if (!Stack.Keep)
            Stack.Discard();
        base.OnBlockRemoved();
        StorageAccess.DirtyTopology();
    }
}

/// <summary>
/// Терминал жидкостей на той же панели.
/// </summary>
public class BlockEntityEStorageLiquidTerminal : BlockEntityEStorageTerminal
{
    protected override InventoryEStorageTerminal CreateInventory() => new InventoryEStorageLiquid();

    protected override bool Liquid => true;
}

