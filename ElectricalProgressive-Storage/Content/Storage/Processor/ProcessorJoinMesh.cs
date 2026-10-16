using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage.Processor;

/// <summary>
/// Оболочка крафт-куба. Прямая линия по одной оси — одна оболочка.
/// Угол L и блок, который в этот угол упирается, рисуются целым одиночным кубом.
/// </summary>
internal static class ProcessorJoinMesh
{
    private const int North = 1 << 0;
    private const int East = 1 << 1;
    private const int South = 1 << 2;
    private const int West = 1 << 3;
    private const int Up = 1 << 4;
    private const int Down = 1 << 5;
    private const int Ext = 34;
    private const double Frame = 3;

    private static readonly string[] Dirs = { "north", "east", "south", "west", "up", "down" };

    private static readonly (string Y, string X, string Z)[] Corners =
    {
        ("up", "east", "north"),
        ("up", "east", "south"),
        ("up", "west", "north"),
        ("up", "west", "south"),
        ("down", "east", "north"),
        ("down", "east", "south"),
        ("down", "west", "north"),
        ("down", "west", "south")
    };

    private static readonly (string Axis, string A, string B)[] Edges =
    {
        ("x", "down", "north"),
        ("x", "down", "south"),
        ("x", "up", "north"),
        ("x", "up", "south"),
        ("y", "west", "north"),
        ("y", "east", "north"),
        ("y", "west", "south"),
        ("y", "east", "south"),
        ("z", "west", "down"),
        ("z", "east", "down"),
        ("z", "west", "up"),
        ("z", "east", "up")
    };

    private static readonly object Gate = new();
    private static readonly Dictionary<long, MeshData> Cache = new();

    public static int Mask(Vintagestory.API.Common.Block[] blocks, int index, IBlockAccessor world, BlockPos pos)
    {
        var raw = 0;
        var axis = -1;
        for (var face = 0; face < 6; face++)
        {
            if (!IsProcessor(blocks, world, pos, index, face))
                continue;
            var a = Axis(face);
            if (axis >= 0 && a != axis)
                return 0;
            axis = a;
            raw |= 1 << face;
        }

        if (raw == 0)
            return 0;

        var mask = 0;
        for (var face = 0; face < 6; face++)
        {
            if ((raw & (1 << face)) == 0)
                continue;
            if (NeighborTurns(blocks, world, pos, index, face, axis))
                continue;
            mask |= 1 << face;
        }

        return mask;
    }

    /// <summary>Север/юг — 0, восток/запад — 1, верх/низ — 2.</summary>
    private static int Axis(int face)
    {
        return face switch
        {
            0 or 2 => 0,
            1 or 3 => 1,
            _ => 2
        };
    }

    private static bool NeighborTurns(Vintagestory.API.Common.Block[] blocks, IBlockAccessor world, BlockPos pos, int index, int face, int axis)
    {
        var npos = pos.AddCopy(BlockFacing.ALLFACES[face]);
        if (!TryStep(index, face, out var neighbor))
            return TurnsInWorld(world, npos, axis);

        for (var other = 0; other < 6; other++)
        {
            if (Axis(other) == axis)
                continue;
            if (IsProcessor(blocks, world, npos, neighbor, other))
                return true;
        }

        return false;
    }

    private static bool TurnsInWorld(IBlockAccessor world, BlockPos pos, int axis)
    {
        if (world == null || pos == null)
            return false;
        for (var other = 0; other < 6; other++)
        {
            if (Axis(other) == axis)
                continue;
            if (ProcessorCluster.IsProcessor(world.GetBlock(pos.AddCopy(BlockFacing.ALLFACES[other]))))
                return true;
        }

        return false;
    }

    private static bool IsProcessor(Vintagestory.API.Common.Block[] blocks, IBlockAccessor world, BlockPos pos, int index, int face)
    {
        if (TryStep(index, face, out var at))
            return (uint)at < (uint)blocks.Length && ProcessorCluster.IsProcessor(blocks[at]);
        if (world == null || pos == null)
            return false;
        return ProcessorCluster.IsProcessor(world.GetBlock(pos.AddCopy(BlockFacing.ALLFACES[face])));
    }

    private static bool TryStep(int index, int face, out int at)
    {
        var x = index % Ext;
        var z = (index / Ext) % Ext;
        var y = index / (Ext * Ext);
        var n = BlockFacing.ALLFACES[face].Normali;
        x += n.X;
        y += n.Y;
        z += n.Z;
        if ((uint)x >= Ext || (uint)y >= Ext || (uint)z >= Ext)
        {
            at = 0;
            return false;
        }

        at = x + Ext * (z + Ext * y);
        return true;
    }

    public static MeshData? For(ICoreClientAPI capi, Vintagestory.API.Common.Block block, int mask)
    {
        var key = ((long)block.Id << 8) | (uint)mask;
        MeshData? mesh;
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out mesh))
                return mesh;

            mesh = Build(capi, block, mask);
            if (mesh != null)
                Cache[key] = mesh;
        }

        return mesh;
    }

    private static int Bit(string dir)
    {
        return dir switch
        {
            "north" => North,
            "east" => East,
            "south" => South,
            "west" => West,
            "up" => Up,
            _ => Down
        };
    }

    private static string Opposite(string dir)
    {
        return dir switch
        {
            "north" => "south",
            "south" => "north",
            "east" => "west",
            "west" => "east",
            "up" => "down",
            _ => "up"
        };
    }

    private static bool On(int mask, string dir)
    {
        return (mask & Bit(dir)) != 0;
    }

    private static MeshData? Build(ICoreClientAPI capi, Vintagestory.API.Common.Block block, int mask)
    {
        var kept = new List<ShapeElement>();
        AddCorners(kept, mask);
        AddEdges(kept, mask);
        var open = AddPanels(kept, mask);
        if (open)
            AddCore(kept);

        if (kept.Count == 0)
            return new MeshData();

        var shape = new Shape
        {
            TextureWidth = 16,
            TextureHeight = 16,
            Textures = new Dictionary<string, AssetLocation>(),
            Elements = kept.ToArray()
        };
        try
        {
            capi.Tesselator.TesselateShape(block, shape, out var mesh);
            capi.TesselatorManager.ThreadDispose();
            return mesh;
        }
        catch
        {
            return null;
        }
    }

    private static void AddCorners(List<ShapeElement> kept, int mask)
    {
        foreach (var (y, x, z) in Corners)
        {
            if (Links(mask, y, x, z) != 0)
                continue;

            double x1 = 0, y1 = 0, z1 = 0, x2 = 16, y2 = 16, z2 = 16;
            FrameSlab(x, ref x1, ref y1, ref z1, ref x2, ref y2, ref z2);
            FrameSlab(y, ref x1, ref y1, ref z1, ref x2, ref y2, ref z2);
            FrameSlab(z, ref x1, ref y1, ref z1, ref x2, ref y2, ref z2);
            AddElement(kept, "corner", x1, y1, z1, x2, y2, z2, true,
                (y, "#ring", 0),
                (x, "#ring", 0),
                (z, "#ring", 0));
        }
    }

    private static void AddEdges(List<ShapeElement> kept, int mask)
    {
        foreach (var (axis, a, b) in Edges)
        {
            var aOn = On(mask, a);
            var bOn = On(mask, b);

            double x1 = 0, y1 = 0, z1 = 0, x2 = 16, y2 = 16, z2 = 16;
            FrameSlab(a, ref x1, ref y1, ref z1, ref x2, ref y2, ref z2);
            FrameSlab(b, ref x1, ref y1, ref z1, ref x2, ref y2, ref z2);
            var paintA = Stripe(a, b);
            var paintB = Stripe(b, a);
            if (aOn && bOn)
            {
                SpanFull(axis, ref x1, ref y1, ref z1, ref x2, ref y2, ref z2);
                AddPost(kept, axis, a, b, mask, x1, y1, z1, x2, y2, z2, paintA, paintB);
                continue;
            }

            if (aOn || bOn)
                continue;

            Span(axis, ref x1, ref y1, ref z1, ref x2, ref y2, ref z2);
            Reach(axis, mask, ref x1, ref y1, ref z1, ref x2, ref y2, ref z2);
            AddElement(kept, "edge", x1, y1, z1, x2, y2, z2, true,
                (a, paintA, 0),
                (b, paintB, 0),
                (Opposite(a), paintA, 0),
                (Opposite(b), paintB, 0));
        }
    }

    private static void AddPost(List<ShapeElement> kept, string axis, string a, string b, int mask, double x1, double y1, double z1, double x2, double y2, double z2, string paintA, string paintB)
    {
        var faces = new List<(string Face, string Texture, int Glow)>
        {
            (Opposite(a), paintA, 0),
            (Opposite(b), paintB, 0)
        };
        var low = axis switch
        {
            "x" => "west",
            "y" => "down",
            _ => "north"
        };
        var high = axis switch
        {
            "x" => "east",
            "y" => "up",
            _ => "south"
        };
        if (!On(mask, low))
            faces.Add((low, "#ring", 0));
        if (!On(mask, high))
            faces.Add((high, "#ring", 0));
        AddElement(kept, "post", x1, y1, z1, x2, y2, z2, true, faces.ToArray());
    }

    private static bool AddPanels(List<ShapeElement> kept, int mask)
    {
        var open = false;
        foreach (var side in Dirs)
        {
            if (On(mask, side))
                continue;
            open = true;
            var x1 = On(mask, "west") ? 0d : Frame;
            var x2 = On(mask, "east") ? 16d : 16 - Frame;
            var y1 = On(mask, "down") ? 0d : Frame;
            var y2 = On(mask, "up") ? 16d : 16 - Frame;
            var z1 = On(mask, "north") ? 0d : Frame;
            var z2 = On(mask, "south") ? 16d : 16 - Frame;
            PanelSlab(side, ref x1, ref y1, ref z1, ref x2, ref y2, ref z2);
            AddElement(kept, "inner", x1, y1, z1, x2, y2, z2, false, (side, "#metal", 255));
        }

        return open;
    }

    private static void AddCore(List<ShapeElement> kept)
    {
        AddElement(kept, "core", Frame, Frame, Frame, 16 - Frame, 16 - Frame, 16 - Frame, true,
            ("north", "#metal", 0),
            ("east", "#metal", 0),
            ("south", "#metal", 0),
            ("west", "#metal", 0),
            ("up", "#metal", 0),
            ("down", "#metal", 0));
    }

    private static string Stripe(string side, string edge)
    {
        var ver = side is not ("up" or "down") && edge is "north" or "east" or "west" or "south"
            || side is "up" or "down" && edge is "east" or "west";
        return ver ? "#ver" : "#hor";
    }

    private static void FrameSlab(string dir, ref double x1, ref double y1, ref double z1, ref double x2, ref double y2, ref double z2)
    {
        switch (dir)
        {
            case "west": x1 = 0; x2 = Frame; break;
            case "east": x1 = 16 - Frame; x2 = 16; break;
            case "down": y1 = 0; y2 = Frame; break;
            case "up": y1 = 16 - Frame; y2 = 16; break;
            case "north": z1 = 0; z2 = Frame; break;
            default: z1 = 16 - Frame; z2 = 16; break;
        }
    }

    private static int Links(int mask, string a, string b, string c)
        => (On(mask, a) ? 1 : 0) + (On(mask, b) ? 1 : 0) + (On(mask, c) ? 1 : 0);

    private static void SpanFull(string axis, ref double x1, ref double y1, ref double z1, ref double x2, ref double y2, ref double z2)
    {
        switch (axis)
        {
            case "x": x1 = 0; x2 = 16; break;
            case "y": y1 = 0; y2 = 16; break;
            default: z1 = 0; z2 = 16; break;
        }
    }

    private static void Reach(string axis, int mask, ref double x1, ref double y1, ref double z1, ref double x2, ref double y2, ref double z2)
    {
        switch (axis)
        {
            case "x":
                if (On(mask, "west")) x1 = 0;
                if (On(mask, "east")) x2 = 16;
                break;
            case "y":
                if (On(mask, "down")) y1 = 0;
                if (On(mask, "up")) y2 = 16;
                break;
            default:
                if (On(mask, "north")) z1 = 0;
                if (On(mask, "south")) z2 = 16;
                break;
        }
    }

    private static void Span(string axis, ref double x1, ref double y1, ref double z1, ref double x2, ref double y2, ref double z2)
    {
        switch (axis)
        {
            case "x": x1 = Frame; x2 = 16 - Frame; break;
            case "y": y1 = Frame; y2 = 16 - Frame; break;
            default: z1 = Frame; z2 = 16 - Frame; break;
        }
    }

    private static void PanelSlab(string side, ref double x1, ref double y1, ref double z1, ref double x2, ref double y2, ref double z2)
    {
        switch (side)
        {
            case "north": z1 = 1; z2 = Frame; break;
            case "south": z1 = 16 - Frame; z2 = 15; break;
            case "west": x1 = 1; x2 = Frame; break;
            case "east": x1 = 16 - Frame; x2 = 15; break;
            case "down": y1 = 1; y2 = Frame; break;
            default: y1 = 16 - Frame; y2 = 15; break;
        }
    }

    private static void AddElement(List<ShapeElement> kept, string name, double x1, double y1, double z1, double x2, double y2, double z2, bool shade, params (string Face, string Texture, int Glow)[] faces)
    {
        if (faces.Length == 0 || x2 - x1 <= 0.001 || y2 - y1 <= 0.001 || z2 - z1 <= 0.001)
            return;
        var element = new ShapeElement
        {
            Name = name,
            From = new[] { Math.Round(x1, 4), Math.Round(y1, 4), Math.Round(z1, 4) },
            To = new[] { Math.Round(x2, 4), Math.Round(y2, 4), Math.Round(z2, 4) },
            Shade = shade,
#pragma warning disable CS0618
            Faces = new Dictionary<string, ShapeElementFace>(),
#pragma warning restore CS0618
            FacesResolved = new ShapeElementFace[6]
        };
        element.ScaleX = 1;
        element.ScaleY = 1;
        element.ScaleZ = 1;
#pragma warning disable CS0618
        foreach (var (face, texture, glow) in faces)
        {
            element.Faces![face] = new ShapeElementFace
            {
                Texture = texture,
                Uv = Uv(face, x1, y1, z1, x2, y2, z2),
                Enabled = true,
                Glow = glow,
                ReflectiveMode = (EnumReflectiveMode)0
            };
        }
#pragma warning restore CS0618
        element.TrimTextureNamesAndResolveFaces();
        kept.Add(element);
    }

    private static float[] Uv(string face, double x1, double y1, double z1, double x2, double y2, double z2)
    {
        return face switch
        {
            "north" => new[] { (float)(16 - x2), (float)(16 - y2), (float)(16 - x1), (float)(16 - y1) },
            "south" => new[] { (float)x1, (float)(16 - y2), (float)x2, (float)(16 - y1) },
            "east" => new[] { (float)(16 - z2), (float)(16 - y2), (float)(16 - z1), (float)(16 - y1) },
            "west" => new[] { (float)z1, (float)(16 - y2), (float)z2, (float)(16 - y1) },
            "up" => new[] { (float)x1, (float)z1, (float)x2, (float)z2 },
            _ => new[] { (float)x1, (float)(16 - z2), (float)x2, (float)(16 - z1) }
        };
    }
}
