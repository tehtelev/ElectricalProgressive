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
    private readonly record struct CableKey(int BlockId, int Connection, int Lines);

    private static readonly object Gate = new();
    private static readonly Dictionary<int, MeshData?[]> Parts = new();
    private static readonly Dictionary<CableKey, MeshData?> Meshes = new();
    private static readonly Dictionary<CableKey, Dictionary<Facing, Cuboidf[]>> Hits = new();
    private static readonly Dictionary<CableKey, Cuboidf[]> Flats = new();

    private static readonly (float X, float Y, bool Half)[][] Strands =
    [
        [(0f, 0f, false)],
        [(0.6f / 16f, 0f, false), (-0.6f / 16f, 0f, false)],
        [(0.6f / 16f, 0f, false), (-0.6f / 16f, 0f, false), (0f, 0.95f / 16f, true)],
        [(0.6f / 16f, 0f, false), (-0.6f / 16f, 0f, false), (0.6f / 16f, 0.95f / 16f, true), (-0.6f / 16f, 0.95f / 16f, true)]
    ];

    private static readonly Cuboidf PartBox = new(0.4625f, 0f, 0f, 0.5375f, 0.0375f, 0.46875f);
    private static readonly Cuboidf FixBox = new(0.45625f, 0f, 0.45625f, 0.54375f, 0.04375f, 0.534375f);
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

        var solid = MyMiniLib.CheckSolidFace(world.BlockAccessor, blockSel.Position, facing);
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

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityEStorageInterface panel)
        {
            if (!solid || SharesSupport(panel.Facing, facing))
                return false;
            return AttachTo(panel, facing, byPlayer, byItemStack);
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
            return;
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageCable entity || entity.Connection == Facing.None)
            return;

        var face = BlockFacing.FromVector(neibpos.X - pos.X, neibpos.Y - pos.Y, neibpos.Z - pos.Z);
        if (face == null)
            return;

        var mask = FacingHelper.FromFace(face);
        if (MyMiniLib.CheckSolidFace(world.BlockAccessor, pos, mask))
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

    public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
    {
        if (api is not ICoreClientAPI capi
            || capi.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageCable entity
            || entity.Connection == Facing.None)
        {
            base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);
            return;
        }

        var key = new CableKey(Id, (int)entity.Connection, entity.PackedLines());
        MeshData? mesh;
        lock (Gate)
        {
            if (!Meshes.TryGetValue(key, out mesh))
            {
                mesh = BuildMesh(capi, entity.Connection, entity.Lines);
                Meshes[key] = mesh;
            }
        }

        if (mesh == null || mesh.VerticesCount == 0)
        {
            base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);
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

    private Cuboidf[] BoxesFor(int blockId, Facing connection, int[] lines)
    {
        var key = new CableKey(blockId, (int)connection, Pack(lines));
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

    private static Facing Hit(Facing connection, int[] lines, int blockId, Vec3d hit)
    {
        var selected = Facing.None;
        foreach (var pair in HitMap(new CableKey(blockId, (int)connection, Pack(lines)), connection, lines))
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
        foreach (var (all, fx, fy, fz, segs) in Layout)
        {
            if ((connection & all) == 0)
                continue;

            var count = LinesOn(all, lines);
            AddBox(map, all, FixBoxFor(count).RotatedCopy(fx, fy, fz, BoxOrigin));
            var part = PartBoxFor(count);
            foreach (var seg in segs)
            {
                if ((connection & seg.Bit) == 0)
                    continue;
                AddBox(map, seg.Bit, part.Clone().RotatedCopy(seg.Rx, seg.Ry, seg.Rz, BoxOrigin));
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

    private MeshData? BuildMesh(ICoreClientAPI capi, Facing connection, int[] lines)
    {
        var parts = PartsFor(capi);
        var part = parts[0];
        var dot = parts[1];
        var fix = parts[2];
        if (part == null)
            return null;

        MeshData? built = null;
        foreach (var (all, fx, fy, fz, segs) in Layout)
        {
            if ((connection & all) == 0)
                continue;

            var count = LinesOn(all, lines);
            var strands = Strands[count - 1];
            if (fix != null)
                AddJoint(ref built, fix, count, fx, fy, fz, 0f, 0f, 0f);

            foreach (var seg in segs)
            {
                if ((connection & seg.Bit) == 0)
                    continue;

                foreach (var strand in strands)
                    AddStrand(ref built, part, strand.X, strand.Y, strand.Half, seg.Rx, seg.Ry, seg.Rz, 0f, 0f, 0f);
                if (dot != null)
                    AddJoint(ref built, dot, count, seg.Rx, seg.Ry, seg.Rz, seg.Tx, seg.Ty, seg.Tz);
            }
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
        var y2 = lines >= 3 ? 0.08125f : 0.0375f;
        return new Cuboidf(0.425f, 0f, 0f, 0.575f, y2, 0.46875f);
    }

    private static Cuboidf FixBoxFor(int lines)
    {
        if (lines <= 1)
            return FixBox.Clone();
        var y2 = lines >= 3 ? 0.0875f : 0.04375f;
        return new Cuboidf(0.41875f, 0f, 0.41875f, 0.58125f, y2, 0.58125f);
    }

    private static void AddStrand(ref MeshData? built, MeshData piece, float ox, float oy, bool half, float rx, float ry, float rz, float tx, float ty, float tz)
    {
        var mesh = piece.Clone();
        if (half)
            mesh.Scale(new Vec3f(0.5f, 0f, 0.5f), 1f, 0.5f, 1f);
        if (ox != 0f || oy != 0f)
            mesh.Translate(ox, oy, 0f);
        var deg = GameMath.DEG2RAD;
        mesh.Rotate(MeshOrigin, rx * deg, ry * deg, rz * deg);
        if (tx != 0f || ty != 0f || tz != 0f)
            mesh.Translate(tx, ty, tz);
        AddMesh(ref built, mesh);
    }

    private static void AddJoint(ref MeshData? built, MeshData piece, int lines, float rx, float ry, float rz, float tx, float ty, float tz)
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

        var deg = GameMath.DEG2RAD;
        mesh.Rotate(MeshOrigin, rx * deg, ry * deg, rz * deg);
        if (tx != 0f || ty != 0f || tz != 0f)
            mesh.Translate(tx, ty, tz);
        AddMesh(ref built, mesh);
    }

    /// <summary>
    /// Жила в той же клетке, что и интерфейс: блок не меняется, рисуется кабель.
    /// </summary>
    public static void Append(ICoreClientAPI capi, BlockEntityEStorageInterface panel, ref MeshData sourceMesh)
    {
        if (panel.Cable == Facing.None || sourceMesh == null)
            return;

        var cable = HostBlock(capi.World, panel.CableCode);
        var mesh = cable?.HostMesh(capi, panel.Cable, panel.CableLines);
        if (mesh == null || mesh.VerticesCount == 0)
            return;

        var combined = sourceMesh.Clone();
        combined.AddMeshData(mesh);
        sourceMesh = combined;
    }

    public MeshData? HostMesh(ICoreClientAPI capi, Facing connection, int[] lines)
    {
        if (connection == Facing.None)
            return null;

        var key = new CableKey(Id, (int)connection, Pack(lines));
        MeshData? mesh;
        lock (Gate)
        {
            if (!Meshes.TryGetValue(key, out mesh))
            {
                mesh = BuildMesh(capi, connection, lines);
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

    public static Cuboidf[] HostBoxes(IWorldAccessor world, BlockEntityEStorageInterface panel)
    {
        var cable = HostBlock(world, panel.CableCode);
        if (cable == null || panel.Cable == Facing.None)
            return [];
        return cable.BoxesFor(cable.Id, panel.Cable, panel.CableLines);
    }

    public static Facing HostHit(IWorldAccessor world, BlockEntityEStorageInterface panel, Vec3d hit)
    {
        var cable = HostBlock(world, panel.CableCode);
        if (cable == null || panel.Cable == Facing.None)
            return Facing.None;
        return Hit(panel.Cable, panel.CableLines, cable.Id, hit);
    }

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

    private bool AttachTo(BlockEntityEStorageInterface panel, Facing facing, IPlayer byPlayer, ItemStack byItemStack)
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
        panel.MarkDirty(true);
        StorageAccess.DirtyTopology();
        return true;
    }

    private static bool SharesSupport(Facing panelFacing, Facing cableFacing)
    {
        BlockFacing? support = null;
        foreach (var face in FacingHelper.Faces(panelFacing))
        {
            support = face;
            break;
        }

        if (support == null)
            return false;

        foreach (var face in FacingHelper.Faces(cableFacing))
            return face == support;

        return false;
    }
}
