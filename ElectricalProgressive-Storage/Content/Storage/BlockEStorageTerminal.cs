using System;
using System.Collections.Generic;
using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Storage;

public interface IPanelHost
{
    Facing Facing { get; set; }
    Facing Cable { get; set; }
    int[] CableLines { get; }
    string CableCode { get; set; }
}

public class BlockEStorageTerminal : BlockEStoragePart
{
    private static readonly Dictionary<(Facing, string, int), MeshData> MeshCache = [];
    private static readonly Dictionary<(int, int), MeshData> GuestMeshes = [];
    private static readonly Dictionary<(Facing, string), Cuboidf[]> SelectionBoxesCache = [];
    private static readonly Dictionary<(Facing, string), Cuboidf[]> CollisionBoxesCache = [];

    /// <summary>Панель, интерфейс и шина делят клетку: по одной на грань.</summary>
    public virtual bool Stacks => true;

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
        if (Stacks && CableCell(world, blockSel, out var cablePos, out _, out _))
        {
            if (byPlayer != null && !world.Claims.TryAccess(byPlayer, cablePos, EnumBlockAccessFlags.BuildOrBreak))
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
        if (Stacks && CableCell(world, blockSel, out _, out _, out _))
            return TryStickOnCable(world, byPlayer, blockSel, byItemStack);

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

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        if (world.Side != EnumAppSide.Server)
            return;

        for (var n = 0; n < 6; n++)
        {
            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntity host || host is not IPanelStack stack)
                return;
            if (world.BlockAccessor.GetBlock(pos) is not BlockEStorageTerminal hostBlock)
                return;
            if (!TryReadFacing(host, out var facing))
                return;

            if (!hostBlock.CanOccupy(world, pos, facing))
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
                if (guestBlock.CanOccupy(world, pos, PanelStack.FacingOf(guests[i])))
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
        if (face == null)
            return;

        var mask = FacingHelper.FromFace(face);
        if ((panel.Cable & mask) == Facing.None)
            return;
        if (BlockEStorageCable.Supported(world.BlockAccessor, pos, mask) || PanelStack.MountedAny(world, pos, face))
            return;

        DropHostCable(world, pos, stayed, panel, mask);
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

        if (cable && index >= stack.Stack.Count)
        {
            var selected = Facing.None;
            if (byPlayer?.CurrentBlockSelection is { } hit && hit.Position != null && hit.Position.Equals(pos) && hit.HitPosition != null)
                selected = BlockEStorageCable.HostHit(world, panel, hit.HitPosition);
            if (selected != Facing.None)
            {
                DropHostCable(world, pos, host, panel, selected);
                return;
            }

            if (!many)
                base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
            return;
        }

        if (!many)
        {
            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
            return;
        }

        if (index <= 0)
            stack.Stack.Promote(world, pos, byPlayer, dropQuantityMultiplier);
        else
            stack.Stack.RemoveAt(world, pos, index - 1, dropQuantityMultiplier);
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

    private static void DropHostCable(IWorldAccessor world, BlockPos pos, BlockEntity host, IPanelHost panel, Facing bits)
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
        if (count > 0 && cable != null)
            world.SpawnItemEntity(new ItemStack(cable, count), pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        => MergedBoxes(blockAccessor, pos, false);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        var boxes = MergedBoxes(blockAccessor, pos, true);
        if (api?.World == null
            || blockAccessor.GetBlockEntity(pos) is not IPanelHost panel
            || panel.Cable == Facing.None)
            return boxes;

        var extra = BlockEStorageCable.HostBoxes(api.World, panel);
        if (extra.Length == 0)
            return boxes;

        var merged = new Cuboidf[boxes.Length + extra.Length];
        Array.Copy(boxes, merged, boxes.Length);
        Array.Copy(extra, 0, merged, boxes.Length, extra.Length);
        return merged;
    }

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

    private static bool CableCell(IWorldAccessor world, BlockSelection blockSel, out BlockPos pos, out BlockEntityEStorageCable entity, out BlockEStorageCable cable)
    {
        pos = null!;
        entity = null!;
        cable = null!;
        if (blockSel?.Position == null || blockSel.Face == null)
            return false;

        var aimed = blockSel.DidOffset ? blockSel.Position.AddCopy(blockSel.Face.Opposite) : blockSel.Position.Copy();
        if (world.BlockAccessor.GetBlock(aimed) is not BlockEStorageCable block)
            return false;
        if (world.BlockAccessor.GetBlockEntity(aimed) is not BlockEntityEStorageCable found || found.Connection == Facing.None)
            return false;

        pos = aimed;
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
        if (!Stacks || !CableCell(world, blockSel, out var cablePos, out var entity, out var cable))
            return false;
        if (byPlayer != null && !world.Claims.TryAccess(byPlayer, cablePos, EnumBlockAccessFlags.BuildOrBreak))
            return false;

        var hit = blockSel.HitPosition == null
            ? Facing.None
            : BlockEStorageCable.StrandHit(cable, entity.Connection, entity.Lines, blockSel.HitPosition);
        var mount = MountOf(world, cablePos, entity.Connection, hit);
        if (mount == Facing.None)
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

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel == null || !world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not IPanelStack stack || stack is not BlockEntity host)
            return false;

        var index = blockSel.SelectionBoxIndex;
        if (index >= stack.Stack.Count)
            return false;

        var panel = stack.Stack.At(host, index);
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

    public static string LinkKey(StorageLink link) => link switch
    {
        StorageLink.Conflict => "electricalprogressivestorage:estorage-conflict",
        StorageLink.NoPower => "electricalprogressivestorage:estorage-nopower",
        StorageLink.NoChannel => "electricalprogressivestorage:estorage-nochannel",
        StorageLink.NoController => "electricalprogressivestorage:estorage-nocontroller",
        _ => "electricalprogressivestorage:estorage-online"
    };
}

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
        Cable = (Facing)tree.GetInt("cable");
        var packedLines = tree.GetInt("cableLines");
        for (var n = 0; n < CableLines.Length; n++)
            CableLines[n] = (packedLines >> (n * 3)) & 7;
        CableCode = tree.GetString("cableCode", "");
        if (Cable != Facing.None && string.IsNullOrEmpty(CableCode))
            CableCode = "estoragecable";
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

        if (!Liquid && !Pattern && packetid == StorageAccess.OrderPacketId && Api.Side == EnumAppSide.Server && data is { Length: > 0 })
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

public class BlockEntityEStorageLiquidTerminal : BlockEntityEStorageTerminal
{
    protected override InventoryEStorageTerminal CreateInventory() => new InventoryEStorageLiquid();

    protected override bool Liquid => true;
}
