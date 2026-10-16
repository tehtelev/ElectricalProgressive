using System.Collections.Generic;
using System.Text;
using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage;

public class BlockEntityEStorageCable : BlockEntity
{
    public const int MaxLines = 4;

    public Facing Connection { get; set; }

    public readonly int[] Lines = new int[6];

    public int LineCount(int faceIndex)
    {
        if ((uint)faceIndex >= 6)
            return 1;
        var count = Lines[faceIndex];
        if (count < 1)
            return 1;
        return count > MaxLines ? MaxLines : count;
    }

    public int LinesOf(Facing mask)
    {
        var min = MaxLines;
        var any = false;
        foreach (var face in FacingHelper.Faces(mask))
        {
            any = true;
            var count = LineCount(face.Index);
            if (count < min)
                min = count;
        }

        return any ? min : 1;
    }

    public int PackedLines()
    {
        var packed = 0;
        for (var i = 0; i < 6; i++)
            packed |= (Lines[i] & 7) << (i * 3);
        return packed;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("connection", (int)Connection);
        tree.SetInt("lines", PackedLines());
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        var next = (Facing)tree.GetInt("connection");
        var packed = tree.GetInt("lines", -1);
        var changed = next != Connection;
        Connection = next;
        if (packed < 0)
        {
            for (var i = 0; i < 6; i++)
            {
                if (Lines[i] != 1)
                    changed = true;
                Lines[i] = 1;
            }
        }
        else
        {
            for (var i = 0; i < 6; i++)
            {
                var count = (packed >> (i * 3)) & 7;
                if (Lines[i] != count)
                    changed = true;
                Lines[i] = count;
            }
        }

        if (changed)
            StorageAccess.DirtyTopology();
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        if (Api?.World == null || Connection == Facing.None)
            return;

        foreach (var (used, capacity) in StorageAccess.CableLoads(Api.World, Pos))
            dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-channels", used, capacity));
    }
}

public class BlockEStorageCable : BlockEStoragePart
{
    private readonly record struct CableKey(int BlockId, int Connection, int Lines, int Reach);
    private readonly record struct PanelBox(float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ);

    private static readonly object Gate = new();
    private static readonly Dictionary<int, MeshData?[]> Parts = new();
    private static readonly Dictionary<int, MeshData?[]> DecalParts = new();
    private static readonly Dictionary<CableKey, MeshData?> Meshes = new();
    private static readonly Dictionary<CableKey, Dictionary<Facing, Cuboidf[]>> Hits = new();
    private static readonly Dictionary<CableKey, Cuboidf[]> Flats = new();
    private static readonly Dictionary<(int BlockId, int Facing), PanelBox> PanelBoxes = new();

    private static readonly (float X, float Y, bool Half)[][] Strands =
    [
        [(0f, 0f, false)],
        [(0.6f / 16f, 0f, false), (-0.6f / 16f, 0f, false)],
        [(0.6f / 16f, 0f, false), (-0.6f / 16f, 0f, false), (0f, 0.95f / 16f, true)],
        [(0.6f / 16f, 0f, false), (-0.6f / 16f, 0f, false), (0.6f / 16f, 0.95f / 16f, true), (-0.6f / 16f, 0.95f / 16f, true)]
    ];

    // Жила и шестерня сидят на плоскости грани и наполовину выходят в соседнюю клетку.
    private static readonly Cuboidf PartBox = new(0.4625f, -0.05f, -0.05f, 0.5375f, 0.05f, 0.46875f);
    private static readonly Cuboidf FixBox = new(0.45625f, -0.05f, 0.45625f, 0.54375f, 0.05f, 0.54375f);
    private static readonly Vec3d BoxOrigin = new(0.5, 0.5, 0.5);
    private static readonly Vec3f MeshOrigin = new(0.5f, 0.5f, 0.5f);

    private readonly record struct Seg(Facing Bit, float Rx, float Ry, float Rz, float Tx, float Ty, float Tz);

    private static readonly (Facing All, float Fx, float Fy, float Fz, Seg[] Segs)[] Layout =
    [
        (Facing.NorthAll, 90f, 0f, 0f,
        [
            new(Facing.NorthEast, 90f, 270f, 0f, 0.5f, 0f, 0f),
            new(Facing.NorthWest, 90f, 90f, 0f, -0.5f, 0f, 0f),
            new(Facing.NorthUp, 90f, 0f, 0f, 0f, 0.5f, 0f),
            new(Facing.NorthDown, 90f, 180f, 0f, 0f, -0.5f, 0f)
        ]),
        (Facing.EastAll, 0f, 0f, 90f,
        [
            new(Facing.EastNorth, 0f, 0f, 90f, 0f, 0f, -0.5f),
            new(Facing.EastSouth, 180f, 0f, 90f, 0f, 0f, 0.5f),
            new(Facing.EastUp, 90f, 0f, 90f, 0f, 0.5f, 0f),
            new(Facing.EastDown, 270f, 0f, 90f, 0f, -0.5f, 0f)
        ]),
        (Facing.SouthAll, 270f, 0f, 0f,
        [
            new(Facing.SouthEast, 270f, 270f, 0f, 0.5f, 0f, 0f),
            new(Facing.SouthWest, 270f, 90f, 0f, -0.5f, 0f, 0f),
            new(Facing.SouthUp, 270f, 180f, 0f, 0f, 0.5f, 0f),
            new(Facing.SouthDown, 270f, 0f, 0f, 0f, -0.5f, 0f)
        ]),
        (Facing.WestAll, 0f, 0f, 270f,
        [
            new(Facing.WestNorth, 0f, 0f, 270f, 0f, 0f, -0.5f),
            new(Facing.WestSouth, 180f, 0f, 270f, 0f, 0f, 0.5f),
            new(Facing.WestUp, 90f, 0f, 270f, 0f, 0.5f, 0f),
            new(Facing.WestDown, 270f, 0f, 270f, 0f, -0.5f, 0f)
        ]),
        (Facing.UpAll, 0f, 0f, 180f,
        [
            new(Facing.UpNorth, 0f, 0f, 180f, 0f, 0f, -0.5f),
            new(Facing.UpEast, 0f, 270f, 180f, 0.5f, 0f, 0f),
            new(Facing.UpSouth, 0f, 180f, 180f, 0f, 0f, 0.5f),
            new(Facing.UpWest, 0f, 90f, 180f, -0.5f, 0f, 0f)
        ]),
        (Facing.DownAll, 0f, 0f, 0f,
        [
            new(Facing.DownNorth, 0f, 0f, 0f, 0f, 0f, -0.5f),
            new(Facing.DownSouth, 0f, 180f, 0f, 0f, 0f, 0.5f),
            new(Facing.DownEast, 0f, 270f, 0f, 0.5f, 0f, 0f),
            new(Facing.DownWest, 0f, 90f, 0f, -0.5f, 0f, 0f)
        ])
    ];

    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);
        lock (Gate)
        {
            Parts.Clear();
            Meshes.Clear();
            Hits.Clear();
            Flats.Clear();
            PanelBoxes.Clear();
        }
    }

    public override bool IsReplacableBy(Vintagestory.API.Common.Block block)
        => base.IsReplacableBy(block) || (block is BlockEStorageCable && block.Code?.Path == Code?.Path);

    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
    {
        if (blockSel == null)
            return false;

        var facing = new Selection(blockSel).Facing;
        if (facing == Facing.None)
            return false;

        var solid = Supported(world.BlockAccessor, blockSel.Position, facing);
        var here = world.BlockAccessor.GetBlock(blockSel.Position);
        if (here is BlockEStorageCable)
        {
            if (!solid)
                return false;
            if (here.Code?.Path != Code?.Path)
                return false;
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityEStorageCable entity || entity.Connection == Facing.None)
                return false;

            var face = FaceOf(facing);
            if (face == null)
                return false;

            var onFace = entity.Connection & FacingHelper.FromFace(face);
            var lines = onFace == Facing.None ? 0 : entity.LineCount(face.Index);
            if ((entity.Connection & facing) != 0)
            {
                if (FirstCodePart() != "estoragecable" || lines >= BlockEntityEStorageCable.MaxLines)
                {
                    if (lines >= BlockEntityEStorageCable.MaxLines && api is ICoreClientAPI full)
                        full.TriggerIngameError(this, "estoragecable", Lang.Get("electricalprogressivestorage:estorage-cable-full"));
                    return false;
                }

                if (!Pay(byPlayer, byItemStack, FacingHelper.Count(onFace)))
                    return false;

                entity.Lines[face.Index] = lines + 1;
                entity.MarkDirty(true);
                StorageAccess.DirtyTopology();
                return true;
            }

            var cost = lines == 0 ? 1 : lines;
            if (FirstCodePart() == "estoragecable" && !Pay(byPlayer, byItemStack, cost))
                return false;

            if (lines == 0)
                entity.Lines[face.Index] = 1;
            entity.Connection |= facing;
            entity.MarkDirty(true);
            StorageAccess.DirtyTopology();
            return true;
        }

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is IPanelHost panel and BlockEntity host)
        {
            if (here is BlockEStorageTerminal panelBlock && !panelBlock.Stacks)
                return false;
            var blocked = host is IPanelStack stack
                ? PanelStack.BlocksCable(stack, facing)
                : SharesSupport(panel.Facing, facing);
            if (!solid || blocked)
                return false;
            return AttachTo(host, panel, facing, byPlayer, byItemStack);
        }

        if (!solid)
            return false;

        if (!base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack))
            return false;

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityEStorageCable placed)
        {
            placed.Connection = facing;
            var face = FaceOf(facing);
            if (face != null)
                placed.Lines[face.Index] = 1;
            placed.MarkDirty(true);
        }

        StorageAccess.DirtyTopology();
        return true;
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        if (api is ICoreClientAPI)
            return;

        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageCable entity || entity.Connection == Facing.None)
        {
            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
            return;
        }

        var selected = Facing.None;
        if (byPlayer?.CurrentBlockSelection is { } sel && sel.Position.Equals(pos))
            selected = Hit(entity.Connection, entity.Lines, Id, sel.HitPosition);

        var remain = entity.Connection & ~selected;
        var removed = entity.Connection & selected;
        if (removed == Facing.None || remain == Facing.None)
        {
            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
            return;
        }

        var count = StrandCount(removed, entity.Lines);
        entity.Connection = remain;
        ClearEmptyFaces(entity, removed);
        entity.MarkDirty(true);
        StorageAccess.DirtyTopology();
        if (count > 0)
            world.SpawnItemEntity(new ItemStack(this, count), pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityEStorageCable entity && entity.Connection != Facing.None)
        {
            var count = StrandCount(entity.Connection, entity.Lines);
            if (count > 0)
                return [new ItemStack(this, count)];
        }

        return base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        if (world.Side != EnumAppSide.Server)
        {
            world.BlockAccessor.MarkBlockDirty(pos);
            return;
        }
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageCable entity || entity.Connection == Facing.None)
            return;

        var face = BlockFacing.FromVector(neibpos.X - pos.X, neibpos.Y - pos.Y, neibpos.Z - pos.Z);
        if (face == null)
            return;

        var mask = FacingHelper.FromFace(face);
        if (Supported(world.BlockAccessor, pos, mask))
            return;

        var removed = entity.Connection & mask;
        if (removed == Facing.None)
            return;

        if ((entity.Connection & ~mask) == Facing.None)
        {
            world.BlockAccessor.BreakBlock(pos, null);
            return;
        }

        var count = StrandCount(removed, entity.Lines);
        entity.Connection &= ~mask;
        ClearEmptyFaces(entity, removed);
        entity.MarkDirty(true);
        StorageAccess.DirtyTopology();
        if (count > 0)
            world.SpawnItemEntity(new ItemStack(this, count), pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        => Boxes(blockAccessor, pos) ?? base.GetSelectionBoxes(blockAccessor, pos);

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        => Boxes(blockAccessor, pos) ?? base.GetCollisionBoxes(blockAccessor, pos);

    public override void GetDecal(IWorldAccessor world, BlockPos pos, ITexPositionSource decalTexSource, ref MeshData decalModelData, ref MeshData blockModelData)
    {
        base.GetDecal(world, pos, decalTexSource, ref decalModelData, ref blockModelData);
        if (world.Api is not ICoreClientAPI capi
            || capi.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageCable entity
            || entity.Connection == Facing.None)
        {
            // Форма блока — жила вдоль Z. Без подмены трещина лежит поперёк поставленного провода.
            decalModelData = decalModelData.EmptyClone();
            return;
        }

        var decal = BuildMesh(capi, pos, entity.Connection, entity.Lines, DecalPartsFor(capi, decalTexSource));
        var solid = HostMesh(capi, pos, entity.Connection, entity.Lines);
        if (decal == null || solid == null || decal.VerticesCount == 0 || decal.VerticesCount != solid.VerticesCount)
        {
            decalModelData = decalModelData.EmptyClone();
            return;
        }

        decalModelData = decal;
        blockModelData = solid;
    }

    public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
    {
        if (api is not ICoreClientAPI capi
            || capi.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageCable entity
            || entity.Connection == Facing.None)
        {
            // Входной меш — общий кэш формы блока. Clear() на нём гасит модель у всех проводов.
            sourceMesh = sourceMesh.EmptyClone();
            return;
        }

        var reach = ReachOf(capi, pos, entity.Connection);
        var key = new CableKey(Id, (int)entity.Connection, entity.PackedLines(), reach);
        MeshData? mesh;
        lock (Gate)
        {
            if (!Meshes.TryGetValue(key, out mesh))
            {
                mesh = BuildMesh(capi, pos, entity.Connection, entity.Lines);
                Meshes[key] = mesh;
            }
        }

        if (mesh == null || mesh.VerticesCount == 0)
        {
            sourceMesh = sourceMesh.EmptyClone();
            return;
        }

        sourceMesh = mesh.Clone();
        base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);
    }

    private Cuboidf[]? Boxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        if (blockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageCable entity || entity.Connection == Facing.None)
            return null;

        return BoxesFor(blockAccessor.GetBlock(pos).Id, entity.Connection, entity.Lines);
    }

    private Cuboidf[] BoxesFor(int blockId, Facing connection, int[] lines, int seats = 0)
    {
        // У хитбоксов четвёртое поле — маска панелей. У меша там другой хэш, словари разные.
        var key = new CableKey(blockId, (int)connection, Pack(lines), seats);
        lock (Gate)
        {
            if (Flats.TryGetValue(key, out var cached))
                return cached;

            var list = new List<Cuboidf>();
            foreach (var pair in HitMap(key, connection, lines))
                list.AddRange(pair.Value);
            var flat = list.ToArray();
            Flats[key] = flat;
            return flat;
        }
    }

    private static Facing Hit(Facing connection, int[] lines, int blockId, Vec3d hit, int seats = 0)
    {
        var selected = Facing.None;
        foreach (var pair in HitMap(new CableKey(blockId, (int)connection, Pack(lines), seats), connection, lines))
        {
            foreach (var box in pair.Value)
            {
                if (box.Clone().OmniGrowBy(0.01f).Contains(hit.X, hit.Y, hit.Z))
                    selected |= pair.Key;
            }
        }

        return selected;
    }

    private static Dictionary<Facing, Cuboidf[]> HitMap(CableKey key, Facing connection, int[] lines)
    {
        lock (Gate)
        {
            if (Hits.TryGetValue(key, out var cached))
                return cached;
        }

        var map = new Dictionary<Facing, Cuboidf[]>();
        var seats = key.Reach;
        foreach (var (all, fx, fy, fz, segs) in Layout)
        {
            if ((connection & all) == 0)
                continue;

            var count = LinesOn(all, lines);
            var face = FaceOf(all);
            var kind = face == null ? 0 : KindOn(seats, face.Index);
            // Жила на шине и интерфейсе без своей обводки: наведение показывает только панель.
            if (kind != 0)
                continue;

            AddBox(map, all, FixBoxFor(count).RotatedCopy(fx, fy, fz, BoxOrigin));
            var part = PartBoxFor(count);
            foreach (var seg in segs)
            {
                if ((connection & seg.Bit) == 0)
                    continue;
                AddStrandBoxes(map, seg.Bit, part, seg.Rx, seg.Ry, seg.Rz);
            }
        }

        lock (Gate)
            Hits[key] = map;
        return map;
    }

    private static void AddBox(Dictionary<Facing, Cuboidf[]> map, Facing key, Cuboidf box)
    {
        if (map.TryGetValue(key, out var have))
        {
            var grown = new Cuboidf[have.Length + 1];
            have.CopyTo(grown, 0);
            grown[^1] = box;
            map[key] = grown;
            return;
        }

        map[key] = [box];
    }

    private MeshData? BuildMesh(ICoreClientAPI capi, BlockPos pos, Facing connection, int[] lines, MeshData?[]? parts = null)
    {
        parts ??= PartsFor(capi);
        var part = parts[0];
        var dot = parts[1];
        var fix = parts[2];
        if (part == null)
            return null;

        MeshData? built = null;
        var seats = SeatPack(capi.World, pos);
        foreach (var (all, fx, fy, fz, segs) in Layout)
        {
            if ((connection & all) == 0)
                continue;

            var count = LinesOn(all, lines);
            var strands = Strands[count - 1];
            var face = FaceOf(all);
            var kind = face == null ? 0 : KindOn(seats, face.Index);
            var before = built?.VerticesCount ?? 0;
            if (fix != null)
                AddJoint(ref built, fix, count, fx, fy, fz, 0f, 0f, 0f, RearOf(kind));

            foreach (var seg in segs)
            {
                if ((connection & seg.Bit) == 0)
                    continue;

                var extra = ExtraToPanel(capi, pos, seg);
                foreach (var strand in strands)
                    AddStrand(ref built, part, strand.X, strand.Y, strand.Half, seg.Rx, seg.Ry, seg.Rz, 0f, 0f, 0f, extra, kind);
                if (dot != null)
                    AddJoint(ref built, dot, count, seg.Rx, seg.Ry, seg.Rz, Push(seg.Tx, extra), Push(seg.Ty, extra), Push(seg.Tz, extra), 0f);
            }

            if (face != null && built != null && NeighborPull(capi, pos, face))
                PullInside(built, before, face);
        }

        return built;
    }

    private MeshData?[] PartsFor(ICoreClientAPI capi)
    {
        if (Parts.TryGetValue(Id, out var found))
            return found;

        var loaded = new[]
        {
            LoadShape(capi, "part"),
            LoadShape(capi, "dot"),
            LoadShape(capi, "fix")
        };
        Parts[Id] = loaded;
        return loaded;
    }

    private MeshData? LoadShape(ICoreClientAPI capi, string name)
    {
        var shape = Vintagestory.API.Common.Shape.TryGet(capi, "electricalprogressivestorage:shapes/block/estoragecable/" + name + ".json");
        if (shape == null)
            return null;

        capi.Tesselator.TesselateShape(this, shape, out var mesh);
        capi.TesselatorManager.ThreadDispose();
        return mesh;
    }

    private MeshData?[] DecalPartsFor(ICoreClientAPI capi, ITexPositionSource tex)
    {
        lock (Gate)
        {
            if (DecalParts.TryGetValue(Id, out var found))
                return found;
        }

        var loaded = new MeshData?[]
        {
            LoadDecalShape(capi, tex, "part"),
            LoadDecalShape(capi, tex, "dot"),
            LoadDecalShape(capi, tex, "fix")
        };
        lock (Gate)
            DecalParts[Id] = loaded;
        return loaded;
    }

    private static MeshData? LoadDecalShape(ICoreClientAPI capi, ITexPositionSource tex, string name)
    {
        var shape = Vintagestory.API.Common.Shape.TryGet(capi, "electricalprogressivestorage:shapes/block/estoragecable/" + name + ".json");
        if (shape == null)
            return null;

        // decalTexSource уже с isDecalUv: координаты трещины, не атлас кварца.
        capi.Tesselator.TesselateShape("estoragecable", shape, out var mesh, tex);
        capi.TesselatorManager.ThreadDispose();
        return mesh;
    }

    private static void AddMesh(ref MeshData? target, MeshData? piece)
    {
        if (piece == null)
            return;
        if (target == null)
            target = piece;
        else
            target.AddMeshData(piece);
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
    {
        if (FirstCodePart() != "estoragecable")
            return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);

        var list = new List<WorldInteraction>
        {
            new()
            {
                ActionLangCode = "electricalprogressivestorage:estorage-thicken",
                HotKeyCode = "shift",
                MouseButton = EnumMouseButton.Right
            }
        };
        var inherited = base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        if (inherited != null)
            list.AddRange(inherited);
        return list.ToArray();
    }

    private bool Pay(IPlayer byPlayer, ItemStack stack, int cost)
    {
        if (cost <= 1 || byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative)
            return true;
        if (stack == null || stack.StackSize < cost)
        {
            if (api is ICoreClientAPI capi)
                capi.TriggerIngameError(this, "estoragecable", Lang.Get("electricalprogressivestorage:estorage-cable-short"));
            return false;
        }

        stack.StackSize -= cost - 1;
        return true;
    }

    private static int LinesOn(Facing faceMask, int[] lines)
    {
        foreach (var face in FacingHelper.Faces(faceMask))
        {
            var count = lines[face.Index];
            if (count < 1)
                return 1;
            return count > BlockEntityEStorageCable.MaxLines ? BlockEntityEStorageCable.MaxLines : count;
        }

        return 1;
    }

    private static BlockFacing? FaceOf(Facing mask)
    {
        foreach (var face in FacingHelper.Faces(mask))
            return face;
        return null;
    }

    private static int StrandCount(Facing bits, int[] lines)
    {
        var count = 0;
        foreach (var face in FacingHelper.Faces(bits))
        {
            var onFace = bits & FacingHelper.FromFace(face);
            count += FacingHelper.Count(onFace) * CountOn(lines, face.Index);
        }

        return count;
    }

    private static int CountOn(int[] lines, int faceIndex)
    {
        if ((uint)faceIndex >= 6)
            return 1;
        var count = lines[faceIndex];
        if (count < 1)
            return 1;
        return count > BlockEntityEStorageCable.MaxLines ? BlockEntityEStorageCable.MaxLines : count;
    }

    private static void ClearEmptyFaces(BlockEntityEStorageCable entity, Facing removed)
        => ClearHostFaces(entity.Connection, entity.Lines, removed);

    private static Cuboidf PartBoxFor(int lines)
    {
        if (lines <= 1)
            return PartBox.Clone();
        var y2 = lines >= 3 ? 0.09f : 0.05f;
        return new Cuboidf(0.425f, -0.05f, -0.05f, 0.575f, y2, 0.46875f);
    }

    private static Cuboidf FixBoxFor(int lines)
    {
        if (lines <= 1)
            return FixBox.Clone();
        var y2 = lines >= 3 ? 0.095f : 0.05f;
        return new Cuboidf(0.41875f, -0.05f, 0.41875f, 0.58125f, y2, 0.58125f);
    }

    /// <summary>
    /// На сколько удлинить жилу за грань клетки, чтобы она дошла до края панели.
    /// Клетка с панелью на другой грани жилу не удлиняет: конец должен попасть в саму панель.
    /// </summary>
    private static float ExtraToPanel(ICoreClientAPI capi, BlockPos pos, Seg seg)
    {
        var dx = seg.Tx > 0.1f ? 1 : seg.Tx < -0.1f ? -1 : 0;
        var dy = seg.Ty > 0.1f ? 1 : seg.Ty < -0.1f ? -1 : 0;
        var dz = seg.Tz > 0.1f ? 1 : seg.Tz < -0.1f ? -1 : 0;
        if ((dx == 0 && dy == 0 && dz == 0) || !StrandEnd(seg, out var x, out var y, out var z))
            return 0f;

        var neighbor = pos.AddCopy(dx, dy, dz);
        var toward = BlockFacing.FromVector(-dx, -dy, -dz);
        if (toward == null)
            return 0f;
        var panel = PanelStack.OnSupport(capi.World, neighbor, toward);
        if (panel?.Block == null)
            return 0f;

        var box = PanelOf(capi, panel.Block, PanelStack.FacingOf(panel));
        var inset = dx > 0 ? box.MinX
            : dx < 0 ? 1f - box.MaxX
            : dy > 0 ? box.MinY
            : dy < 0 ? 1f - box.MaxY
            : dz > 0 ? box.MinZ
            : 1f - box.MaxZ;
        if (inset < 0.02f || inset > 0.22f)
            return 0f;

        var hx = x - dx;
        var hy = y - dy;
        var hz = z - dz;
        const float slack = 0.05f;
        var hits = dx != 0
            ? InPanel(hy, box.MinY, box.MaxY, slack) && InPanel(hz, box.MinZ, box.MaxZ, slack)
            : dy != 0
                ? InPanel(hx, box.MinX, box.MaxX, slack) && InPanel(hz, box.MinZ, box.MaxZ, slack)
                : InPanel(hx, box.MinX, box.MaxX, slack) && InPanel(hy, box.MinY, box.MaxY, slack);
        if (!hits)
            return 0f;
        return inset + 0.01f;
    }

    private static bool InPanel(float value, float min, float max, float slack)
        => value >= min - slack && value <= max + slack;

    private static int ReachOf(ICoreClientAPI capi, BlockPos pos, Facing connection)
    {
        var hash = 0;
        var n = 0;
        foreach (var (all, _, _, _, segs) in Layout)
        {
            var pull = false;
            var face = FaceOf(all);
            if (face != null)
                pull = NeighborPull(capi, pos, face);
            foreach (var seg in segs)
            {
                if ((connection & seg.Bit) == 0)
                    continue;
                var extra = ExtraToPanel(capi, pos, seg);
                if (extra > 0f)
                    hash ^= (int)(extra * 512f) * (n + 3);
                if (pull)
                    hash ^= 17 * (n + 5);
                n++;
            }
        }

        // Панель в этой клетке меняет изгиб жилы. Иначе голый провод и провод под шиной делят один меш.
        hash ^= SeatPack(capi.World, pos) << 12;
        return hash;
    }

    private static bool NeighborPull(ICoreClientAPI capi, BlockPos pos, BlockFacing face)
    {
        var at = pos.AddCopy(face);
        var neighbor = capi.World.BlockAccessor.GetBlock(at);
        if (neighbor is BlockEStorageDrive)
            return true;
        return neighbor is BlockEStorageController && ControllerMesh.InsetShell(capi.World.BlockAccessor, at);
    }

    /// <summary>
    /// Жила наполовину уходит в соседнюю клетку. Окно привода и утопленная панель
    /// контроллера эту половину не закрывают, поэтому грань сдвигается целиком.
    /// </summary>
    private static void PullInside(MeshData mesh, int fromVertex, BlockFacing face)
    {
        var xyz = mesh.xyz;
        if (xyz == null || fromVertex >= mesh.VerticesCount)
            return;
        var start = fromVertex * 3;
        var count = mesh.VerticesCount * 3;
        if (count > xyz.Length)
            count = xyz.Length;
        var x = face.Normali.X;
        var y = face.Normali.Y;
        var z = face.Normali.Z;
        var axis = x != 0 ? 0 : y != 0 ? 1 : 2;
        var sign = x + y + z;
        const float keep = 0.02f;
        var limit = sign > 0 ? 1f - keep : keep;
        var over = 0f;
        for (var i = start + axis; i + 2 < count; i += 3)
        {
            var past = sign > 0 ? xyz[i] - limit : limit - xyz[i];
            if (past > over)
                over = past;
        }

        if (over <= 0f)
            return;
        var shift = sign * over;
        for (var i = start + axis; i + 2 < count; i += 3)
            xyz[i] -= shift;
    }

    private static PanelBox PanelOf(ICoreClientAPI capi, Vintagestory.API.Common.Block block, Facing facing)
    {
        if (facing == Facing.None)
            facing = Facing.UpNorth;

        var key = (block.Id, (int)facing);
        lock (Gate)
        {
            if (PanelBoxes.TryGetValue(key, out var cached))
                return cached;
        }

        var mesh = capi.TesselatorManager.GetDefaultBlockMesh(block)?.Clone();
        if (mesh?.xyz == null || mesh.VerticesCount == 0)
            return default;

        FacingRotations.ApplyRotations(mesh, facing);
        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var minZ = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        var maxZ = float.MinValue;
        var count = mesh.VerticesCount * 3;
        if (count > mesh.xyz.Length)
            count = mesh.xyz.Length;
        for (var i = 0; i + 2 < count; i += 3)
        {
            var x = mesh.xyz[i];
            var y = mesh.xyz[i + 1];
            var z = mesh.xyz[i + 2];
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (z < minZ) minZ = z;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
            if (z > maxZ) maxZ = z;
        }

        var box = new PanelBox(minX, minY, minZ, maxX, maxY, maxZ);
        lock (Gate)
            PanelBoxes[key] = box;
        return box;
    }

    private static float Push(float value, float extra)
    {
        if (extra <= 0f)
            return value;
        if (value > 0.1f)
            return value + extra;
        if (value < -0.1f)
            return value - extra;
        return value;
    }

    private static void AddStrand(ref MeshData? built, MeshData piece, float ox, float oy, bool half, float rx, float ry, float rz, float tx, float ty, float tz, float extra, int kind)
    {
        if (kind != 0)
        {
            AddHugged(ref built, piece, ox, oy, half, rx, ry, rz, extra, kind);
            return;
        }

        var mesh = Prepared(piece, ox, oy, half, extra);
        var deg = GameMath.DEG2RAD;
        mesh.Rotate(MeshOrigin, rx * deg, ry * deg, rz * deg);
        if (tx != 0f || ty != 0f || tz != 0f)
            mesh.Translate(tx, ty, tz);
        AddMesh(ref built, mesh);
    }

    /// <summary>
    /// Шина: один прямой кусок до воротника. Интерфейс тонкий и широкий, поэтому
    /// жила поднимается снаружи плиты и дальше идёт по её задней стороне.
    /// </summary>
    private static void AddHugged(ref MeshData? built, MeshData piece, float ox, float oy, bool half, float rx, float ry, float rz, float extra, int kind)
    {
        if (kind != 1)
        {
            var mesh = Prepared(piece, ox, oy, half, extra);
            LiftToRear(mesh, RearOf(kind));
            Finish(ref built, mesh, rx, ry, rz);
            return;
        }

        var outer = Prepared(piece, ox, oy, half, extra);
        if (!StrandZ(outer, out var zLo, out var zHi))
            return;
        const float rim = 0.125f;
        const float slab = 2f / 16f;
        const float collar = 3f / 16f;
        if (rim <= zLo + 0.02f || rim >= zHi - 0.02f)
        {
            LiftToRear(outer, collar);
            Finish(ref built, outer, rx, ry, rz);
            return;
        }

        var inner = Prepared(piece, ox, oy, half, extra);
        ClipLift(outer, zLo, rim, 0f, slab);
        ClipLift(inner, rim, zHi, slab, collar);
        Finish(ref built, outer, rx, ry, rz);
        Finish(ref built, inner, rx, ry, rz);
    }

    private static void ClipLift(MeshData mesh, float z0, float z1, float y0, float y1)
    {
        if (!StrandZ(mesh, out var zLo, out var zHi))
            return;
        var span = zHi - zLo;
        if (span < 0.0001f || z1 - z0 < 0.01f)
            return;
        mesh.Scale(new Vec3f(0.5f, 0f, zLo), 1f, 1f, (z1 - z0) / span);
        if (z0 != zLo)
            mesh.Translate(0f, 0f, z0 - zLo);
        var xyz = mesh.xyz;
        if (xyz == null)
            return;
        var count = mesh.VerticesCount * 3;
        if (count > xyz.Length)
            count = xyz.Length;
        var rise = z1 - z0;
        for (var i = 0; i + 2 < count; i += 3)
            xyz[i + 1] += y0 + (y1 - y0) * (xyz[i + 2] - z0) / rise;
    }

    private static bool StrandZ(MeshData mesh, out float zLo, out float zHi)
    {
        zLo = float.MaxValue;
        zHi = float.MinValue;
        var xyz = mesh.xyz;
        if (xyz == null || mesh.VerticesCount == 0)
            return false;
        var count = mesh.VerticesCount * 3;
        if (count > xyz.Length)
            count = xyz.Length;
        for (var i = 2; i < count; i += 3)
        {
            var z = xyz[i];
            if (z < zLo)
                zLo = z;
            if (z > zHi)
                zHi = z;
        }

        return zHi > zLo + 0.0001f;
    }

    private static void LiftToRear(MeshData mesh, float rear)
    {
        var xyz = mesh.xyz;
        if (xyz == null || rear == 0f || mesh.VerticesCount == 0)
            return;
        var count = mesh.VerticesCount * 3;
        if (count > xyz.Length)
            count = xyz.Length;
        var zLo = float.MaxValue;
        var zHi = float.MinValue;
        for (var i = 2; i < count; i += 3)
        {
            var z = xyz[i];
            if (z < zLo)
                zLo = z;
            if (z > zHi)
                zHi = z;
        }

        var span = zHi - zLo;
        if (span < 0.0001f)
            return;
        for (var i = 0; i + 2 < count; i += 3)
            xyz[i + 1] += rear * (xyz[i + 2] - zLo) / span;
    }

    private static MeshData Prepared(MeshData piece, float ox, float oy, bool half, float extra)
    {
        var mesh = piece.Clone();
        if (half)
            mesh.Scale(new Vec3f(0.5f, 0f, 0.5f), 1f, 0.5f, 1f);
        if (ox != 0f || oy != 0f)
            mesh.Translate(ox, oy, 0f);
        if (extra > 0f)
            mesh.Scale(new Vec3f(0.5f, 0f, 7.5f / 16f), 1f, 1f, 1f + extra * 2f);
        return mesh;
    }

    private static void Finish(ref MeshData? built, MeshData mesh, float rx, float ry, float rz)
    {
        var deg = GameMath.DEG2RAD;
        mesh.Rotate(MeshOrigin, rx * deg, ry * deg, rz * deg);
        AddMesh(ref built, mesh);
    }

    private static void AddJoint(ref MeshData? built, MeshData piece, int lines, float rx, float ry, float rz, float tx, float ty, float tz, float lift)
    {
        var mesh = piece.Clone();
        if (lines >= 2)
        {
            var wide = 2.6f / 1.4f;
            var tall = lines >= 3 ? 2.1f / 1.4f : 1f;
            mesh.Scale(new Vec3f(0.5f, 0f, 0.5f), wide, tall, wide);
            if (lines >= 3)
                mesh.Translate(0f, 0.35f / 16f, 0f);
        }

        if (lift != 0f)
            mesh.Translate(0f, lift, 0f);
        var deg = GameMath.DEG2RAD;
        mesh.Rotate(MeshOrigin, rx * deg, ry * deg, rz * deg);
        if (tx != 0f || ty != 0f || tz != 0f)
            mesh.Translate(tx, ty, tz);
        AddMesh(ref built, mesh);
    }

    private static float RearOf(int kind) => kind switch
    {
        1 => 3f / 16f,
        2 or 3 => 5f / 16f,
        _ => 0f
    };

    private static int KindOn(int seats, int faceIndex)
    {
        if ((uint)faceIndex >= 6)
            return 0;
        return (seats >> (faceIndex * 2)) & 3;
    }

    private static int SeatPack(IWorldAccessor world, BlockPos pos)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is not IPanelStack stack || stack is not BlockEntity host)
            return 0;
        var pack = PackSeat(host);
        foreach (var guest in stack.Stack.Guests)
            pack |= PackSeat(guest);
        return pack;
    }

    private static int PackSeat(BlockEntity entity)
    {
        var kind = entity.Block?.FirstCodePart() switch
        {
            "estorageinterface" => 1,
            "estorageimport" => 2,
            "estorageexport" => 3,
            _ => 0
        };
        if (kind == 0)
            return 0;
        var face = PanelStack.Support(PanelStack.FacingOf(entity));
        if (face == null)
            return 0;
        return kind << (face.Index * 2);
    }

    private static void AddStrandBoxes(Dictionary<Facing, Cuboidf[]> map, Facing bit, Cuboidf part, float rx, float ry, float rz)
        => AddBox(map, bit, part.Clone().RotatedCopy(rx, ry, rz, BoxOrigin));

    /// <summary>
    /// Жила в той же клетке, что и панель: блок не меняется, рисуется кабель.
    /// </summary>
    public static void Append(ICoreClientAPI capi, BlockPos pos, IPanelHost panel, ref MeshData sourceMesh)
    {
        if (panel.Cable == Facing.None || sourceMesh == null)
            return;

        var cable = HostBlock(capi.World, panel.CableCode);
        var mesh = cable?.HostMesh(capi, pos, panel.Cable, panel.CableLines);
        if (mesh == null || mesh.VerticesCount == 0)
            return;

        var combined = sourceMesh.Clone();
        combined.AddMeshData(mesh);
        sourceMesh = combined;
    }

    public MeshData? HostMesh(ICoreClientAPI capi, BlockPos pos, Facing connection, int[] lines)
    {
        if (connection == Facing.None || pos == null)
            return null;

        var reach = ReachOf(capi, pos, connection);
        var key = new CableKey(Id, (int)connection, Pack(lines), reach);
        MeshData? mesh;
        lock (Gate)
        {
            if (!Meshes.TryGetValue(key, out mesh))
            {
                mesh = BuildMesh(capi, pos, connection, lines);
                Meshes[key] = mesh;
            }
        }

        return mesh?.Clone();
    }

    public static int Pack(int[] lines)
    {
        var packed = 0;
        for (var i = 0; i < 6; i++)
            packed |= ((i < lines.Length ? lines[i] : 0) & 7) << (i * 3);
        return packed;
    }

    public static Cuboidf[] HostBoxes(IWorldAccessor world, IPanelHost panel)
    {
        var cable = HostBlock(world, panel.CableCode);
        if (cable == null || panel.Cable == Facing.None)
            return [];
        var seats = panel is BlockEntity entity ? SeatPack(world, entity.Pos) : 0;
        return cable.BoxesFor(cable.Id, panel.Cable, panel.CableLines, seats);
    }

    public static Facing HostHit(IWorldAccessor world, IPanelHost panel, Vec3d hit)
    {
        var cable = HostBlock(world, panel.CableCode);
        if (cable == null || panel.Cable == Facing.None)
            return Facing.None;
        var seats = panel is BlockEntity entity ? SeatPack(world, entity.Pos) : 0;
        return Hit(panel.Cable, panel.CableLines, cable.Id, hit, seats);
    }

    /// <summary>Жила под прицелом. Ключ центра — маска грани, её надо сузить до одного бита.</summary>
    public static Facing StrandHit(BlockEStorageCable cable, Facing connection, int[] lines, Vec3d hit)
        => cable == null || hit == null || connection == Facing.None
            ? Facing.None
            : Hit(connection, lines, cable.Id, hit);

    public static int HostStrands(Facing bits, int[] lines) => StrandCount(bits, lines);

    public static void ClearHostFaces(Facing connection, int[] lines, Facing removed)
    {
        foreach (var face in FacingHelper.Faces(removed))
        {
            if ((connection & FacingHelper.FromFace(face)) == 0)
                lines[face.Index] = 0;
        }
    }

    public static BlockEStorageCable? HostBlock(IWorldAccessor world, string code)
    {
        if (string.IsNullOrEmpty(code))
            code = "estoragecable";
        return world.GetBlock(new AssetLocation("electricalprogressivestorage:" + code)) as BlockEStorageCable;
    }

    private bool AttachTo(BlockEntity host, IPanelHost panel, Facing facing, IPlayer byPlayer, ItemStack byItemStack)
    {
        var code = FirstCodePart();
        if (panel.Cable != Facing.None && panel.CableCode != code)
            return false;

        var face = FaceOf(facing);
        if (face == null)
            return false;

        var onFace = panel.Cable & FacingHelper.FromFace(face);
        var lines = onFace == Facing.None ? 0 : CountOn(panel.CableLines, face.Index);
        if ((panel.Cable & facing) != 0)
        {
            if (code != "estoragecable" || lines >= BlockEntityEStorageCable.MaxLines)
            {
                if (lines >= BlockEntityEStorageCable.MaxLines && api is ICoreClientAPI full)
                    full.TriggerIngameError(this, "estoragecable", Lang.Get("electricalprogressivestorage:estorage-cable-full"));
                return false;
            }

            if (!Pay(byPlayer, byItemStack, FacingHelper.Count(onFace)))
                return false;

            panel.CableLines[face.Index] = lines + 1;
        }
        else
        {
            var cost = lines == 0 ? 1 : lines;
            if (code == "estoragecable" && !Pay(byPlayer, byItemStack, cost))
                return false;

            if (lines == 0)
                panel.CableLines[face.Index] = 1;
            panel.Cable |= facing;
        }

        if (string.IsNullOrEmpty(panel.CableCode))
            panel.CableCode = code;
        host.MarkDirty(true);
        StorageAccess.DirtyTopology();
        return true;
    }

    private static bool SharesSupport(Facing panelFacing, Facing cableFacing)
    {
        var support = SupportOf(panelFacing);
        if (support == null)
            return false;

        foreach (var face in FacingHelper.Faces(cableFacing))
            return face == support;

        return false;
    }

    /// <summary>
    /// Сплошная грань или коллизия соседа, которая доходит до этой грани. Жернова держатся вторым путём.
    /// </summary>
    internal static bool Supported(IBlockAccessor accessor, BlockPos pos, Facing facing)
    {
        if (MyMiniLib.CheckSolidFace(accessor, pos, facing))
            return true;

        BlockFacing? support = null;
        foreach (var face in FacingHelper.Faces(facing))
        {
            support = face;
            break;
        }

        if (support == null)
            return false;

        var neighborPos = pos.AddCopy(support);
        var boxes = accessor.GetBlock(neighborPos)?.GetCollisionBoxes(accessor, neighborPos);
        if (boxes == null)
            return false;

        var touched = support.Opposite;
        const float edge = 0.001f;
        foreach (var box in boxes)
        {
            if (box != null && Reaches(box, touched, edge))
                return true;
        }

        return false;
    }

    private static bool Reaches(Cuboidf box, BlockFacing side, float edge)
    {
        var overlapsX = box.X2 > edge && box.X1 < 1f - edge;
        var overlapsY = box.Y2 > edge && box.Y1 < 1f - edge;
        var overlapsZ = box.Z2 > edge && box.Z1 < 1f - edge;
        if (side == BlockFacing.UP)
            return box.Y2 >= 1f - edge && overlapsX && overlapsZ;
        if (side == BlockFacing.DOWN)
            return box.Y1 <= edge && overlapsX && overlapsZ;
        if (side == BlockFacing.NORTH)
            return box.Z1 <= edge && overlapsX && overlapsY;
        if (side == BlockFacing.SOUTH)
            return box.Z2 >= 1f - edge && overlapsX && overlapsY;
        if (side == BlockFacing.EAST)
            return box.X2 >= 1f - edge && overlapsY && overlapsZ;
        if (side == BlockFacing.WEST)
            return box.X1 <= edge && overlapsY && overlapsZ;
        return false;
    }

    /// <summary>
    /// Конец жилы лежит на опорной грани панели. Жила, которая только смотрит в клетку, панель не касается.
    /// </summary>
    public static bool MeetsPanel(BlockPos cablePos, Facing component, BlockPos panelPos, Facing panelFacing)
    {
        if (component == Facing.None || cablePos == null || panelPos == null || cablePos.dimension != panelPos.dimension)
            return false;

        var support = SupportOf(panelFacing);
        if (support == null)
            return false;

        foreach (var (_, _, _, _, segs) in Layout)
        {
            foreach (var seg in segs)
            {
                if ((component & seg.Bit) == 0)
                    continue;
                if (EndOnPanel(cablePos, seg, support, panelPos))
                    return true;
            }
        }

        return false;
    }

    private static BlockFacing? SupportOf(Facing panelFacing)
    {
        foreach (var face in FacingHelper.Faces(panelFacing))
            return face;
        return null;
    }

    public static bool MountedOn(Facing panelFacing, BlockFacing face)
    {
        var support = SupportOf(panelFacing);
        return support != null && face != null && support == face;
    }

    private static bool StrandEnd(Seg seg, out float x, out float y, out float z)
    {
        x = 0.5f;
        y = 0.5f;
        z = 0.5f;
        if (FaceOf(seg.Bit) is not { } face)
            return false;

        if (face == BlockFacing.NORTH)
            z = 0f;
        else if (face == BlockFacing.SOUTH)
            z = 1f;
        else if (face == BlockFacing.EAST)
            x = 1f;
        else if (face == BlockFacing.WEST)
            x = 0f;
        else if (face == BlockFacing.UP)
            y = 1f;
        else
            y = 0f;

        x += seg.Tx;
        y += seg.Ty;
        z += seg.Tz;
        return true;
    }

    private static bool EndOnPanel(BlockPos cablePos, Seg seg, BlockFacing support, BlockPos panelPos)
    {
        if (!StrandEnd(seg, out var x, out var y, out var z))
            return false;

        var wx = cablePos.X + x;
        var wy = cablePos.Y + y;
        var wz = cablePos.Z + z;
        const float slack = 0.02f;
        float along, u, v;
        if (support == BlockFacing.NORTH)
        {
            along = wz - panelPos.Z;
            u = wx - panelPos.X;
            v = wy - panelPos.Y;
        }
        else if (support == BlockFacing.SOUTH)
        {
            along = wz - (panelPos.Z + 1);
            u = wx - panelPos.X;
            v = wy - panelPos.Y;
        }
        else if (support == BlockFacing.EAST)
        {
            along = wx - (panelPos.X + 1);
            u = wy - panelPos.Y;
            v = wz - panelPos.Z;
        }
        else if (support == BlockFacing.WEST)
        {
            along = wx - panelPos.X;
            u = wy - panelPos.Y;
            v = wz - panelPos.Z;
        }
        else if (support == BlockFacing.UP)
        {
            along = wy - (panelPos.Y + 1);
            u = wx - panelPos.X;
            v = wz - panelPos.Z;
        }
        else
        {
            along = wy - panelPos.Y;
            u = wx - panelPos.X;
            v = wz - panelPos.Z;
        }

        return along <= slack && along >= -slack && u >= -slack && u <= 1f + slack && v >= -slack && v <= 1f + slack;
    }
}
