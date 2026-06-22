using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content;

internal static class PipeMeshBuilder
{
    private const string ShapeDomain = "electricalprogressivetransport";
    private const string ShapePath = "shapes/block/itempipe";
    private const int MeshCacheVersion = 15;

    private const float PipeCenter = 0.5f;

    private static readonly Dictionary<string, Shape> ShapeCache = [];
    private static readonly Dictionary<long, MeshData> MeshCache = [];
    private static readonly Dictionary<int, MeshData> CenterCubeCache = [];

    // pipe_part.json = TubeBaseWest (0,0,0). Углы из cross.json TubeBase*.
    private static readonly (float rx, float ry, float rz)[] SideRotationsDeg =
    [
        (90f, -90f, 90f),    // north
        (-90f, 0f, 180f),    // east
        (-90f, 0f, 90f),     // south
        (0f, 0f, 0f),        // west
        (-90f, -90f, 180f),  // up
        (0f, 0f, 90f),       // down
    ];

    // inserter_head.json = Cube66 (north) из cross.json Base.
    private static readonly (float rx, float ry, float rz)[] InserterRotationsDeg =
    [
        (0f, 0f, 0f),        // north - Cube66
        (0f, 90f, 0f),       // east  - Cube68
        (0f, 180f, 0f),      // south - Cube70
        (0f, -90f, 0f),      // west  - Cube72
        (180f, 0f, 0f),      // up    - Cube74
        (180f, -90f, 0f),    // down  - Cube80
    ];

    public static MeshData Build(
        ICoreClientAPI api,
        Vintagestory.API.Common.Block block,
        BlockPos pos,
        bool[] connectedSides,
        bool[] connectedToInventory,
        bool useInserterHead)
    {
        if (connectedSides == null || connectedSides.Length < 6)
            return null;

        connectedToInventory ??= new bool[6];

        long cacheKey = BuildCacheKey(block.Id, pos, connectedSides, connectedToInventory, useInserterHead);
        if (MeshCache.TryGetValue(cacheKey, out MeshData cached))
            return cached;

        MeshData finalMesh = null;
        var origin = new Vec3f(PipeCenter, PipeCenter, PipeCenter);

        if (ShouldRenderCenterCube(connectedSides, connectedToInventory, useInserterHead))
        {
            MeshData centerMesh = GetCenterCube(api, block);
            if (centerMesh != null)
                AddMesh(ref finalMesh, centerMesh);
        }

        MeshData pipePartMesh = TesselateShape(api, block, $"{ShapePath}/pipe_part.json");
        if (pipePartMesh != null)
        {
            for (int i = 0; i < 6; i++)
            {
                if (!ShouldRenderArm(pos, i, connectedSides, connectedToInventory))
                    continue;

                var (rx, ry, rz) = SideRotationsDeg[i];
                AddMesh(ref finalMesh, RotateMesh(pipePartMesh.Clone(), rx, ry, rz, origin));
            }
        }

        if (useInserterHead)
        {
            MeshData inserterMesh = TesselateShape(api, block, $"{ShapePath}/inserter_head.json");
            if (inserterMesh != null)
            {
                for (int i = 0; i < 6; i++)
                {
                    if (!connectedSides[i] || !connectedToInventory[i])
                        continue;

                    var (rx, ry, rz) = InserterRotationsDeg[i];
                    AddMesh(ref finalMesh, RotateMesh(inserterMesh.Clone(), rx, ry, rz, origin));
                }
            }
        }

        MeshCache[cacheKey] = finalMesh;
        return finalMesh;
    }

    private static bool ShouldRenderArm(BlockPos pos, int sideIndex, bool[] connectedSides, bool[] connectedToInventory)
    {
        if (!connectedSides[sideIndex])
            return false;

        if (connectedToInventory[sideIndex])
            return true;

        BlockFacing facing = BlockFacing.ALLFACES[sideIndex];
        BlockPos neighborPos = pos.AddCopy(facing);
        return OwnsPipeConnection(pos, neighborPos);
    }

    private static bool OwnsPipeConnection(BlockPos pos, BlockPos neighborPos)
    {
        if (pos.X != neighborPos.X)
            return pos.X < neighborPos.X;
        if (pos.Y != neighborPos.Y)
            return pos.Y < neighborPos.Y;
        return pos.Z < neighborPos.Z;
    }

    private static MeshData RotateMesh(MeshData mesh, float rxDeg, float ryDeg, float rzDeg, Vec3f origin)
    {
        if (rxDeg == 0f && ryDeg == 0f && rzDeg == 0f)
            return mesh;

        Matrixf matrix = new Matrixf()
            .Translate(origin.X, origin.Y, origin.Z);

        if (rxDeg != 0f)
            matrix.RotateXDeg(rxDeg);
        if (ryDeg != 0f)
            matrix.RotateYDeg(ryDeg);
        if (rzDeg != 0f)
            matrix.RotateZDeg(rzDeg);

        matrix.Translate(-origin.X, -origin.Y, -origin.Z);
        mesh.MatrixTransform(matrix.Values);
        return mesh;
    }

    private static long BuildCacheKey(
        int blockId,
        BlockPos pos,
        bool[] connectedSides,
        bool[] connectedToInventory,
        bool useInserterHead)
    {
        long key = ((long)MeshCacheVersion << 32) | (uint)blockId;

        for (int i = 0; i < 6; i++)
        {
            if (ShouldRenderArm(pos, i, connectedSides, connectedToInventory))
                key ^= 1L << (i + 1);
            if (connectedToInventory[i])
                key ^= 1L << (i + 7);
        }

        if (useInserterHead)
            key ^= 1L << 13;

        if (ShouldRenderCenterCube(connectedSides, connectedToInventory, useInserterHead))
            key ^= 1L << 14;

        return key;
    }

    /// <summary>
    /// Предметные и жидкостные трубы всегда с кубом. Обычные — только на концах, углах и развилках.
    /// </summary>
    private static bool ShouldRenderCenterCube(bool[] connectedSides, bool[] connectedToInventory, bool useInserterHead)
    {
        if (useInserterHead)
            return true;

        int pipeSideA = -1;
        int pipeSideB = -1;
        int pipeCount = 0;
        bool hasInventoryConnection = false;

        for (int i = 0; i < 6; i++)
        {
            if (!connectedSides[i])
                continue;

            if (connectedToInventory[i])
            {
                hasInventoryConnection = true;
                continue;
            }

            if (pipeCount == 0)
                pipeSideA = i;
            else
                pipeSideB = i;
            pipeCount++;
        }

        if (pipeCount == 2 && !hasInventoryConnection && AreOppositeSides(pipeSideA, pipeSideB))
            return false;

        return true;
    }

    private static bool AreOppositeSides(int sideA, int sideB)
    {
        if (sideA < 0 || sideB < 0)
            return false;

        if (sideA > sideB)
            (sideA, sideB) = (sideB, sideA);

        return sideA switch
        {
            0 => sideB == 2,
            1 => sideB == 3,
            4 => sideB == 5,
            _ => false
        };
    }

    private static MeshData GetCenterCube(ICoreClientAPI api, Vintagestory.API.Common.Block block)
    {
        if (!CenterCubeCache.TryGetValue(block.Id, out MeshData center))
        {
            center = TesselateShape(api, block, $"{ShapePath}/center.json");
            if (center != null)
                CenterCubeCache[block.Id] = center;
        }

        return center?.Clone();
    }

    private static MeshData TesselateShape(ICoreClientAPI api, Vintagestory.API.Common.Block block, string shapePath)
    {
        Shape shape = GetShape(api, shapePath);
        if (shape == null)
            return null;

        api.Tesselator.TesselateShape(block, shape, out MeshData mesh);
        return mesh;
    }

    private static Shape GetShape(ICoreClientAPI api, string shapePath)
    {
        string key = $"{ShapeDomain}:{shapePath}";
        if (!ShapeCache.TryGetValue(key, out Shape shape))
        {
            shape = Shape.TryGet(api, key);
            if (shape != null)
                ShapeCache[key] = shape;
        }

        return shape;
    }

    private static void AddMesh(ref MeshData target, MeshData part)
    {
        if (part == null)
            return;

        if (target == null || target.VerticesCount == 0)
            target = part;
        else
            target.AddMeshData(part);
    }
}