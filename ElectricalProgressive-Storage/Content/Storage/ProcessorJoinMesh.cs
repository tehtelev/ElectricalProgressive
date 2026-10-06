using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Client.Tesselation;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage;

/// <summary>
/// Одиночный процессор — шина из estorageprocessor.json.
/// Сосед дотягивает тело до границы клетки. Угловые стойки остаются только снаружи.
/// </summary>
internal static class ProcessorJoinMesh
{
    private const int North = 1 << 0;
    private const int East = 1 << 1;
    private const int South = 1 << 2;
    private const int West = 1 << 3;
    private const int Up = 1 << 4;
    private const int Down = 1 << 5;
    private const int Sides = North | East | South | West;
    private const double Past = 16.06;
    private const double Before = -0.06;
    private const double ShoeLo = 1.4;
    private const double ShoeHi = 14.6;
    private const double ShoeY1 = 1.15;
    private const double StepLo = 2.2;
    private const double StepHi = 13.8;
    private const double StepY0 = 1.05;
    private const double StepY1 = 2.35;
    private const double CoreLo = 2.6;
    private const double CoreHi = 13.4;
    private const double CoreY0 = 2.2;
    private const double CoreY1 = 11.6;
    private const double FluteW = 0.62;
    private const double FluteY0 = 2.7;
    private const double FluteY1 = 11.3;
    private const double FluteNear0 = 2.25;
    private const double FluteNear1 = 2.8;
    private const double FluteFar0 = 13.2;
    private const double FluteFar1 = 13.75;
    private const double BandNear0 = 1.9;
    private const double BandNear1 = 2.45;
    private const double BandFar0 = 13.55;
    private const double BandFar1 = 14.1;
    private const double RibLo = 1.14;
    private const double RibHi = 1.76;
    private const double RibFarLo = 14.24;
    private const double RibFarHi = 14.86;
    private const double PostY0 = 0.9;
    private const double PostY1 = 12.15;

    private static readonly double[] FluteAt = { 3.84, 6.44, 9.04, 11.64 };
    private static readonly double[] BandY0 = { 4.85, 8.45 };
    private static readonly double[] BandY1 = { 5.5, 9.1 };
    private static readonly object Gate = new();
    private static readonly Dictionary<long, MeshData> Cache = new();
    private static Shape? _shape;

    public static int Mask(Vintagestory.API.Common.Block[] blocks, int index)
    {
        var mask = 0;
        foreach (var face in BlockFacing.ALLFACES)
        {
            var at = index + TileSideEnum.MoveIndex[face.Index];
            if ((uint)at >= (uint)blocks.Length)
                continue;
            if (ProcessorCluster.IsProcessor(blocks[at]))
                mask |= 1 << face.Index;
        }

        return mask;
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

    private static MeshData? Build(ICoreClientAPI capi, Vintagestory.API.Common.Block block, int mask)
    {
        _shape ??= Shape.TryGet(capi, "electricalprogressivestorage:shapes/block/estorageprocessor.json");
        if (_shape?.Elements == null)
            return null;

        var shape = _shape.Clone();
        var kept = new List<ShapeElement>(shape.Elements.Length);
        foreach (var element in shape.Elements)
        {
            if (element?.From == null || element.To == null || element.Name == null)
                continue;
            if (!Keep(element.Name, mask))
                continue;
            kept.Add(element);
        }

        AddJoints(kept, mask);
        shape.Elements = kept.ToArray();
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

    private static bool Keep(string name, int mask)
    {
        if (name is "shoe" or "step")
            return (mask & Down) == 0;
        if (name == "core")
            return true;
        if (name.StartsWith("fluteN") || name.StartsWith("bandN"))
            return (mask & North) == 0;
        if (name.StartsWith("fluteE") || name.StartsWith("bandE"))
            return (mask & East) == 0;
        if (name.StartsWith("fluteS") || name.StartsWith("bandS"))
            return (mask & South) == 0;
        if (name.StartsWith("fluteW") || name.StartsWith("bandW"))
            return (mask & West) == 0;
        if (name.StartsWith("postNW"))
            return (mask & (North | West)) == 0;
        if (name.StartsWith("postNE"))
            return (mask & (North | East)) == 0;
        if (name.StartsWith("postSW"))
            return (mask & (South | West)) == 0;
        if (name.StartsWith("postSE"))
            return (mask & (South | East)) == 0;
        if (name is "collar" or "button")
            return (mask & (Sides | Up)) == 0;
        return true;
    }

    private static void AddJoints(List<ShapeElement> kept, int mask)
    {
        if (mask == 0)
            return;

        if ((mask & East) != 0)
            ExtendEast(kept, mask);
        if ((mask & West) != 0)
            ExtendWest(kept, mask);
        if ((mask & South) != 0)
            ExtendSouth(kept, mask);
        if ((mask & North) != 0)
            ExtendNorth(kept, mask);
        if ((mask & Up) != 0)
            ExtendUp(kept, mask);
        if ((mask & Down) != 0)
            ExtendDown(kept, mask);
    }

    private static void ExtendEast(List<ShapeElement> kept, int mask)
    {
        kept.Add(Box(CoreHi, CoreY0, CoreLo, Past, CoreY1, CoreHi, "#metal"));
        if ((mask & Down) == 0)
        {
            kept.Add(Box(ShoeHi, 0, ShoeLo, Past, ShoeY1, ShoeHi, "#steel"));
            kept.Add(Box(StepHi, StepY0, StepLo, Past, StepY1, StepHi, "#steel"));
        }

        if ((mask & North) == 0)
            SideX(kept, RibFarLo, RibFarHi, CoreHi, Past, FluteNear0, FluteNear1, BandNear0, BandNear1);
        if ((mask & South) == 0)
            SideX(kept, RibFarLo, RibFarHi, CoreHi, Past, FluteFar0, FluteFar1, BandFar0, BandFar1);
    }

    private static void ExtendWest(List<ShapeElement> kept, int mask)
    {
        kept.Add(Box(Before, CoreY0, CoreLo, CoreLo, CoreY1, CoreHi, "#metal"));
        if ((mask & Down) == 0)
        {
            kept.Add(Box(Before, 0, ShoeLo, ShoeLo, ShoeY1, ShoeHi, "#steel"));
            kept.Add(Box(Before, StepY0, StepLo, StepLo, StepY1, StepHi, "#steel"));
        }

        if ((mask & North) == 0)
            SideX(kept, RibLo, RibHi, Before, CoreLo, FluteNear0, FluteNear1, BandNear0, BandNear1);
        if ((mask & South) == 0)
            SideX(kept, RibLo, RibHi, Before, CoreLo, FluteFar0, FluteFar1, BandFar0, BandFar1);
    }

    private static void ExtendSouth(List<ShapeElement> kept, int mask)
    {
        var x0 = (mask & West) != 0 ? Before : CoreLo;
        var x1 = (mask & East) != 0 ? Past : CoreHi;
        kept.Add(Box(x0, CoreY0, CoreHi, x1, CoreY1, Past, "#metal"));
        if ((mask & Down) == 0)
        {
            var sx0 = (mask & West) != 0 ? Before : ShoeLo;
            var sx1 = (mask & East) != 0 ? Past : ShoeHi;
            var tx0 = (mask & West) != 0 ? Before : StepLo;
            var tx1 = (mask & East) != 0 ? Past : StepHi;
            kept.Add(Box(sx0, 0, ShoeHi, sx1, ShoeY1, Past, "#steel"));
            kept.Add(Box(tx0, StepY0, StepHi, tx1, StepY1, Past, "#steel"));
        }

        if ((mask & East) == 0)
            SideZ(kept, FluteFar0, FluteFar1, BandFar0, BandFar1, RibFarLo, RibFarHi, CoreHi, Past);
        if ((mask & West) == 0)
            SideZ(kept, FluteNear0, FluteNear1, BandNear0, BandNear1, RibFarLo, RibFarHi, CoreHi, Past);
    }

    private static void ExtendNorth(List<ShapeElement> kept, int mask)
    {
        var x0 = (mask & West) != 0 ? Before : CoreLo;
        var x1 = (mask & East) != 0 ? Past : CoreHi;
        kept.Add(Box(x0, CoreY0, Before, x1, CoreY1, CoreLo, "#metal"));
        if ((mask & Down) == 0)
        {
            var sx0 = (mask & West) != 0 ? Before : ShoeLo;
            var sx1 = (mask & East) != 0 ? Past : ShoeHi;
            var tx0 = (mask & West) != 0 ? Before : StepLo;
            var tx1 = (mask & East) != 0 ? Past : StepHi;
            kept.Add(Box(sx0, 0, Before, sx1, ShoeY1, ShoeLo, "#steel"));
            kept.Add(Box(tx0, StepY0, Before, tx1, StepY1, StepLo, "#steel"));
        }

        if ((mask & East) == 0)
            SideZ(kept, FluteFar0, FluteFar1, BandFar0, BandFar1, RibLo, RibHi, Before, CoreLo);
        if ((mask & West) == 0)
            SideZ(kept, FluteNear0, FluteNear1, BandNear0, BandNear1, RibLo, RibHi, Before, CoreLo);
    }

    private static void SideX(List<ShapeElement> kept, double ribX0, double ribX1, double bandX0, double bandX1, double z0, double z1, double bandZ0, double bandZ1)
    {
        kept.Add(Box(ribX0, FluteY0, z0, ribX1, FluteY1, z1, "#steel"));
        for (var i = 0; i < BandY0.Length; i++)
            kept.Add(Box(bandX0, BandY0[i], bandZ0, bandX1, BandY1[i], bandZ1, "#brass"));
    }

    private static void SideZ(List<ShapeElement> kept, double x0, double x1, double bandX0, double bandX1, double ribZ0, double ribZ1, double bandZ0, double bandZ1)
    {
        kept.Add(Box(x0, FluteY0, ribZ0, x1, FluteY1, ribZ1, "#steel"));
        for (var i = 0; i < BandY0.Length; i++)
            kept.Add(Box(bandX0, BandY0[i], bandZ0, bandX1, BandY1[i], bandZ1, "#brass"));
    }

    private static void ExtendUp(List<ShapeElement> kept, int mask)
    {
        var x0 = (mask & West) != 0 ? Before : CoreLo;
        var x1 = (mask & East) != 0 ? Past : CoreHi;
        var z0 = (mask & North) != 0 ? Before : CoreLo;
        var z1 = (mask & South) != 0 ? Past : CoreHi;
        kept.Add(Box(x0, CoreY1, z0, x1, Past, z1, "#metal"));
        FlutesY(kept, mask, FluteY1, Past);
        Posts(kept, mask, PostY1, Past);
    }

    private static void ExtendDown(List<ShapeElement> kept, int mask)
    {
        var x0 = (mask & West) != 0 ? Before : CoreLo;
        var x1 = (mask & East) != 0 ? Past : CoreHi;
        var z0 = (mask & North) != 0 ? Before : CoreLo;
        var z1 = (mask & South) != 0 ? Past : CoreHi;
        kept.Add(Box(x0, Before, z0, x1, CoreY0, z1, "#metal"));
        FlutesY(kept, mask, Before, FluteY0);
        Posts(kept, mask, Before, PostY0);
    }

    private static void FlutesY(List<ShapeElement> kept, int mask, double y0, double y1)
    {
        if ((mask & North) == 0)
        {
            foreach (var x in FluteAt)
                kept.Add(Box(x, y0, FluteNear0, x + FluteW, y1, FluteNear1, "#steel"));
            if ((mask & East) != 0)
                kept.Add(Box(RibFarLo, y0, FluteNear0, RibFarHi, y1, FluteNear1, "#steel"));
            if ((mask & West) != 0)
                kept.Add(Box(RibLo, y0, FluteNear0, RibHi, y1, FluteNear1, "#steel"));
        }

        if ((mask & South) == 0)
        {
            foreach (var x in FluteAt)
                kept.Add(Box(x, y0, FluteFar0, x + FluteW, y1, FluteFar1, "#steel"));
            if ((mask & East) != 0)
                kept.Add(Box(RibFarLo, y0, FluteFar0, RibFarHi, y1, FluteFar1, "#steel"));
            if ((mask & West) != 0)
                kept.Add(Box(RibLo, y0, FluteFar0, RibHi, y1, FluteFar1, "#steel"));
        }

        if ((mask & West) == 0)
        {
            foreach (var z in FluteAt)
                kept.Add(Box(FluteNear0, y0, z, FluteNear1, y1, z + FluteW, "#steel"));
            if ((mask & South) != 0)
                kept.Add(Box(FluteNear0, y0, RibFarLo, FluteNear1, y1, RibFarHi, "#steel"));
            if ((mask & North) != 0)
                kept.Add(Box(FluteNear0, y0, RibLo, FluteNear1, y1, RibHi, "#steel"));
        }

        if ((mask & East) == 0)
        {
            foreach (var z in FluteAt)
                kept.Add(Box(FluteFar0, y0, z, FluteFar1, y1, z + FluteW, "#steel"));
            if ((mask & South) != 0)
                kept.Add(Box(FluteFar0, y0, RibFarLo, FluteFar1, y1, RibFarHi, "#steel"));
            if ((mask & North) != 0)
                kept.Add(Box(FluteFar0, y0, RibLo, FluteFar1, y1, RibHi, "#steel"));
        }
    }

    private static void Posts(List<ShapeElement> kept, int mask, double y0, double y1)
    {
        if ((mask & (North | West)) == 0)
            kept.Add(Box(1.35, y0, 1.35, 2.75, y1, 2.75, "#steel"));
        if ((mask & (North | East)) == 0)
            kept.Add(Box(13.25, y0, 1.35, 14.65, y1, 2.75, "#steel"));
        if ((mask & (South | West)) == 0)
            kept.Add(Box(1.35, y0, 13.25, 2.75, y1, 14.65, "#steel"));
        if ((mask & (South | East)) == 0)
            kept.Add(Box(13.25, y0, 13.25, 14.65, y1, 14.65, "#steel"));
    }

    private static ShapeElement Box(double x0, double y0, double z0, double x1, double y1, double z1, string texture)
    {
        var hide = 0;
        if (x0 <= 0)
            hide |= West;
        if (x1 >= 16)
            hide |= East;
        if (z0 <= 0)
            hide |= North;
        if (z1 >= 16)
            hide |= South;
        if (y0 <= 0)
            hide |= Down;
        if (y1 >= 16)
            hide |= Up;

        var element = new ShapeElement
        {
            Name = "join",
            From = new[] { x0, y0, z0 },
            To = new[] { x1, y1, z1 },
            Shade = true,
#pragma warning disable CS0618
            Faces = new Dictionary<string, ShapeElementFace>(),
#pragma warning restore CS0618
            FacesResolved = new ShapeElementFace[6]
        };
        element.ScaleX = 1;
        element.ScaleY = 1;
        element.ScaleZ = 1;
        var dx = (float)(x1 - x0);
        var dy = (float)(y1 - y0);
        var dz = (float)(z1 - z0);
        AddFace(element, "north", North, dx, dy, hide, texture);
        AddFace(element, "east", East, dz, dy, hide, texture);
        AddFace(element, "south", South, dx, dy, hide, texture);
        AddFace(element, "west", West, dz, dy, hide, texture);
        AddFace(element, "up", Up, dx, dz, hide, texture);
        AddFace(element, "down", Down, dx, dz, hide, texture);
        element.TrimTextureNamesAndResolveFaces();
        return element;
    }

    private static void AddFace(ShapeElement element, string code, int bit, float u, float v, int hide, string texture)
    {
#pragma warning disable CS0618
        if ((hide & bit) != 0 || u <= 0 || v <= 0 || element.Faces == null)
            return;

        element.Faces[code] = new ShapeElementFace
        {
            Texture = texture,
            Uv = new[] { 4f, 4f, 4f + u, 4f + v },
            Enabled = true,
            ReflectiveMode = texture is "#brass" or "#bronze" ? (EnumReflectiveMode)3 : (EnumReflectiveMode)0
        };
#pragma warning restore CS0618
    }
}
