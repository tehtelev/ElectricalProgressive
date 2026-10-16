using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Client.Tesselation;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage.Controller;

/// <summary>
/// Куб контроллера AE2. Одиночный блок и короткая линия остаются json-оболочкой.
/// Колонна — только когда соседи с обеих сторон ровно одной оси.
/// Две такие оси дают чередование inside_a / inside_b.
/// </summary>
internal static class ControllerMesh
{
    private const int North = 1 << 0;
    private const int East = 1 << 1;
    private const int South = 1 << 2;
    private const int West = 1 << 3;
    private const int Up = 1 << 4;
    private const int Down = 1 << 5;

    private enum Kind { Block, ColumnX, ColumnY, ColumnZ, InsideA, InsideB }

    private readonly record struct Paint(string Face, string Texture, int Rotation, float U0, float V0, float U1, float V1);

    // cube_column, затем поворот как у бревна: column_z = x 90, column_x = x 90 и y 90.
    private static readonly Paint[] ColumnY =
    {
        new("north", "#column", 0, 0, 0, 16, 16),
        new("east", "#column", 0, 0, 0, 16, 16),
        new("south", "#column", 0, 0, 0, 16, 16),
        new("west", "#column", 0, 0, 0, 16, 16),
        new("up", "#face", 0, 0, 0, 16, 16),
        new("down", "#face", 0, 0, 0, 16, 16)
    };

    private static readonly Paint[] ColumnZ =
    {
        new("north", "#face", 0, 16, 16, 0, 0),
        new("east", "#column", 90, 0, 0, 16, 16),
        new("south", "#face", 0, 0, 0, 16, 16),
        new("west", "#column", 90, 16, 16, 0, 0),
        new("up", "#column", 0, 0, 0, 16, 16),
        new("down", "#column", 0, 16, 16, 0, 0)
    };

    private static readonly Paint[] ColumnX =
    {
        new("north", "#column", 90, 0, 0, 16, 16),
        new("east", "#face", 0, 0, 0, 16, 16),
        new("south", "#column", 90, 16, 16, 0, 0),
        new("west", "#face", 0, 16, 16, 0, 0),
        new("up", "#column", 90, 16, 16, 0, 0),
        new("down", "#column", 90, 16, 16, 0, 0)
    };

    private static readonly string[] FaceCodes = { "north", "east", "south", "west", "up", "down" };

    private static readonly Paint[] FaceAll =
    {
        new("north", "#face", 0, 0, 0, 16, 16),
        new("east", "#face", 0, 0, 0, 16, 16),
        new("south", "#face", 0, 0, 0, 16, 16),
        new("west", "#face", 0, 0, 0, 16, 16),
        new("up", "#face", 0, 0, 0, 16, 16),
        new("down", "#face", 0, 0, 0, 16, 16)
    };

    private static readonly object Gate = new();
    private static readonly Dictionary<long, MeshData> Cache = new();
    private static readonly Dictionary<long, MeshData> ShellCovers = new();

    public static int Mask(Vintagestory.API.Common.Block[] blocks, int index)
    {
        var mask = 0;
        foreach (var face in BlockFacing.ALLFACES)
        {
            var at = index + TileSideEnum.MoveIndex[face.Index];
            if ((uint)at >= (uint)blocks.Length)
                continue;
            var block = blocks[at];
            if (block?.Code != null && block.FirstCodePart() == "estoragecontroller")
                mask |= 1 << face.Index;
        }

        return mask;
    }

    public static int DriveMask(Vintagestory.API.Common.Block[] blocks, int index)
    {
        var mask = 0;
        foreach (var face in BlockFacing.ALLFACES)
        {
            var at = index + TileSideEnum.MoveIndex[face.Index];
            if ((uint)at >= (uint)blocks.Length)
                continue;
            if (blocks[at] is BlockEStorageDrive)
                mask |= 1 << face.Index;
        }

        return mask;
    }

    /// <summary>
    /// Одиночный блок, один сосед и линия из двух остаются оболочкой: центр грани утоплен.
    /// </summary>
    public static bool InsetShell(IBlockAccessor world, BlockPos pos)
    {
        var mask = 0;
        foreach (var face in BlockFacing.ALLFACES)
        {
            var block = world.GetBlock(pos.AddCopy(face));
            if (block != null && block.FirstCodePart() == "estoragecontroller")
                mask |= 1 << face.Index;
        }

        return Classify(mask, 0) == Kind.Block;
    }

    public static MeshData? For(ICoreClientAPI capi, Vintagestory.API.Common.Block block, int mask, int drives, int parity)
    {
        var key = ((long)block.Id << 24) | ((uint)mask << 8) | ((uint)drives << 1) | (uint)(parity & 1);
        MeshData? mesh;
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out mesh))
                return mesh;

            mesh = Build(capi, block, mask, drives, parity);
            if (mesh != null)
                Cache[key] = mesh;
        }

        return mesh;
    }

    public static MeshData? ShellCover(ICoreClientAPI capi, Vintagestory.API.Common.Block block, int drives)
    {
        if (drives == 0)
            return null;

        var key = ((long)block.Id << 8) | (uint)drives;
        MeshData? mesh;
        lock (Gate)
        {
            if (ShellCovers.TryGetValue(key, out mesh))
                return mesh;

            mesh = Tesselate(capi, block, Covers(drives, FaceAll, false, 0));
            if (mesh != null)
                ShellCovers[key] = mesh;
        }

        return mesh;
    }

    private static Kind Classify(int mask, int parity)
    {
        var xx = (mask & East) != 0 && (mask & West) != 0;
        var yy = (mask & Up) != 0 && (mask & Down) != 0;
        var zz = (mask & North) != 0 && (mask & South) != 0;
        var axes = (xx ? 1 : 0) + (yy ? 1 : 0) + (zz ? 1 : 0);
        if (axes >= 2)
            return (parity & 1) == 0 ? Kind.InsideA : Kind.InsideB;
        if (xx)
            return Kind.ColumnX;
        if (yy)
            return Kind.ColumnY;
        if (zz)
            return Kind.ColumnZ;
        return Kind.Block;
    }

    private static MeshData? Build(ICoreClientAPI capi, Vintagestory.API.Common.Block block, int mask, int drives, int parity)
    {
        var kind = Classify(mask, parity);
        if (kind == Kind.Block)
            return null;

        var online = block.Variant != null && block.Variant.ContainsKey("state") && block.Variant["state"] == "online";
        var paints = kind switch
        {
            Kind.ColumnX => ColumnX,
            Kind.ColumnY => ColumnY,
            Kind.ColumnZ => ColumnZ,
            Kind.InsideA => InsidePaints("#insidea"),
            _ => InsidePaints("#insideb")
        };
        var elements = new List<ShapeElement>
        {
            kind is Kind.ColumnX or Kind.ColumnY or Kind.ColumnZ ? Column(paints, online) : Inside(paints[0].Texture, online)
        };
        // Привод врёт, что окно глухое, и грань куба на этой стороне выбрасывается.
        // Пластина на полвокселя внутри остаётся и закрывает вид сквозь контроллер.
        elements.AddRange(Covers(drives, paints, online && kind is Kind.ColumnX or Kind.ColumnY or Kind.ColumnZ, online ? 255 : 0));
        return Tesselate(capi, block, elements);
    }

    private static Paint[] InsidePaints(string texture)
    {
        var paints = new Paint[FaceCodes.Length];
        for (var i = 0; i < FaceCodes.Length; i++)
            paints[i] = new Paint(FaceCodes[i], texture, 0, 0, 0, 16, 16);
        return paints;
    }

    private static List<ShapeElement> Covers(int drives, Paint[] paints, bool swapOnlineFace, int glow)
    {
        var covers = new List<ShapeElement>();
        if (drives == 0)
            return covers;

        foreach (var paint in paints)
        {
            var bit = BitOf(paint.Face);
            if (bit == 0 || (drives & bit) == 0)
                continue;
            var texture = swapOnlineFace && paint.Texture == "#face" ? "#column" : paint.Texture;
            covers.Add(Cover(paint.Face, texture, paint.Rotation, paint.U0, paint.V0, paint.U1, paint.V1, glow));
        }

        return covers;
    }

    private static int BitOf(string face)
        => face switch
        {
            "north" => North,
            "east" => East,
            "south" => South,
            "west" => West,
            "up" => Up,
            "down" => Down,
            _ => 0
        };

    private static ShapeElement Cover(string face, string texture, int rotation, float u0, float v0, float u1, float v1, int glow)
    {
        const double a = 0.5;
        const double b = 1.0;
        var (x1, y1, z1, x2, y2, z2) = face switch
        {
            "north" => (0d, 0d, a, 16d, 16d, b),
            "south" => (0d, 0d, 16d - b, 16d, 16d, 16d - a),
            "west" => (a, 0d, 0d, b, 16d, 16d),
            "east" => (16d - b, 0d, 0d, 16d - a, 16d, 16d),
            "down" => (0d, a, 0d, 16d, b, 16d),
            _ => (0d, 16d - b, 0d, 16d, 16d - a, 16d)
        };
        var element = NewElement("cover", x1, y1, z1, x2, y2, z2);
        AddFace(element, face, texture, rotation, u0, v0, u1, v1, glow);
        element.TrimTextureNamesAndResolveFaces();
        return element;
    }

    private static MeshData? Tesselate(ICoreClientAPI capi, Vintagestory.API.Common.Block block, List<ShapeElement> elements)
    {
        if (elements.Count == 0)
            return null;

        var shape = new Shape
        {
            TextureWidth = 16,
            TextureHeight = 16,
            Textures = new Dictionary<string, AssetLocation>(),
            Elements = elements.ToArray()
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

    private static ShapeElement Column(Paint[] paints, bool online)
    {
        var element = NewElement("column");
        foreach (var paint in paints)
        {
            var texture = online && paint.Texture == "#face" ? "#column" : paint.Texture;
            AddFace(element, paint.Face, texture, paint.Rotation, paint.U0, paint.V0, paint.U1, paint.V1, online ? 255 : 0);
        }

        element.TrimTextureNamesAndResolveFaces();
        return element;
    }

    private static ShapeElement Inside(string texture, bool online)
    {
        var element = NewElement("inside");
        foreach (var face in FaceCodes)
            AddFace(element, face, texture, 0, 0, 0, 16, 16, online ? 255 : 0);
        element.TrimTextureNamesAndResolveFaces();
        return element;
    }

    private static ShapeElement NewElement(string name, double x1 = 0, double y1 = 0, double z1 = 0, double x2 = 16, double y2 = 16, double z2 = 16)
    {
        var element = new ShapeElement
        {
            Name = name,
            From = new[] { x1, y1, z1 },
            To = new[] { x2, y2, z2 },
            Shade = true,
#pragma warning disable CS0618
            Faces = new Dictionary<string, ShapeElementFace>(),
#pragma warning restore CS0618
            FacesResolved = new ShapeElementFace[6]
        };
        element.ScaleX = 1;
        element.ScaleY = 1;
        element.ScaleZ = 1;
        return element;
    }

    private static void AddFace(ShapeElement element, string code, string texture, int rotation, float u0, float v0, float u1, float v1, int glow)
    {
#pragma warning disable CS0618
        if (element.Faces == null)
            return;
        element.Faces[code] = new ShapeElementFace
        {
            Texture = texture,
            Uv = new[] { u0, v0, u1, v1 },
            Rotation = rotation,
            Enabled = true,
            Glow = glow,
            ReflectiveMode = (EnumReflectiveMode)0
        };
#pragma warning restore CS0618
    }
}
