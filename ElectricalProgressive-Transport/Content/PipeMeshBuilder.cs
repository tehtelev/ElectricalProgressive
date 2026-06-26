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
    private const int MeshCacheVersion = 24;

    private const float PipeCenter = 0.5f;

    // pipe_part.json (TubeBaseWest): от центра блока до внешнего конца ~1.005.
    private const float PipeArmFullReach = 1.0051f;
    private const float PipeArmInventoryMaxReach = 1.5f;

    private static readonly Dictionary<string, Shape> ShapeCache = [];
    private static readonly Dictionary<long, MeshData> MeshCache = [];
    private static readonly Dictionary<int, MeshData> CenterCubeCache = [];

    private static readonly (float rx, float ry, float rz)[] StraightRotationsDeg =
    [
        (0f, 90f, 0f),  // north-south
        (0f, 0f, 0f),   // east-west
        (0f, 0f, 90f),  // up-down
    ];

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

        long cacheKey = BuildCacheKey(api, block.Id, pos, connectedSides, connectedToInventory, useInserterHead);
        if (MeshCache.TryGetValue(cacheKey, out MeshData cached))
            return cached;

        MeshData finalMesh = null;
        var origin = new Vec3f(PipeCenter, PipeCenter, PipeCenter);
        int straightAxis = GetStraightAxis(connectedSides, connectedToInventory, useInserterHead);

        if (straightAxis >= 0)
        {
            MeshData straightMesh = TesselateShape(api, block, $"{ShapePath}/straight.json");
            if (straightMesh != null)
            {
                var (rx, ry, rz) = StraightRotationsDeg[straightAxis];
                AddMesh(ref finalMesh, RotateMesh(straightMesh, rx, ry, rz, origin));
            }

            MeshCache[cacheKey] = finalMesh;
            return finalMesh;
        }

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
                if (!ShouldRenderArm(i, connectedSides))
                    continue;

                MeshData armMesh = pipePartMesh.Clone();
                if (connectedToInventory[i])
                {
                    BlockPos neighborPos = pos.AddCopy(BlockFacing.ALLFACES[i]);
                    float reach = PipeNeighborMeshClipper.GetReachAlongFacing(
                        api,
                        pos,
                        i,
                        neighborPos,
                        PipeArmInventoryMaxReach);
                    ScaleArmToReach(armMesh, reach);
                }

                var (rx, ry, rz) = SideRotationsDeg[i];
                AddMesh(ref finalMesh, RotateMesh(armMesh, rx, ry, rz, origin));
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

    private static bool ShouldRenderArm(int sideIndex, bool[] connectedSides)
        => connectedSides[sideIndex];

    private static void ScaleArmToReach(MeshData mesh, float targetReach)
    {
        float currentReach = GetArmReachAlongScaleAxis(mesh);
        if (currentReach <= 0f)
            currentReach = PipeArmFullReach;

        float scale = targetReach / currentReach;
        mesh.Scale(new Vec3f(PipeCenter, PipeCenter, PipeCenter), scale, 1f, 1f);
    }

    private static float GetArmReachAlongScaleAxis(MeshData mesh)
    {
        if (mesh?.xyz == null || mesh.VerticesCount <= 0)
            return 0f;

        float reach = 0f;
        int vertexValues = Math.Min(mesh.xyz.Length, mesh.VerticesCount * 3);

        for (int i = 0; i < vertexValues; i += 3)
            reach = Math.Max(reach, Math.Abs(mesh.xyz[i] - PipeCenter));

        return reach;
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
        ICoreClientAPI api,
        int blockId,
        BlockPos pos,
        bool[] connectedSides,
        bool[] connectedToInventory,
        bool useInserterHead)
    {
        long key = ((long)MeshCacheVersion << 32) | (uint)blockId;
        long reachHash = 0;

        for (int i = 0; i < 6; i++)
        {
            if (ShouldRenderArm(i, connectedSides))
                key ^= 1L << (i + 1);
            if (connectedToInventory[i])
            {
                key ^= 1L << (i + 7);

                BlockPos neighborPos = pos.AddCopy(BlockFacing.ALLFACES[i]);
                float reach = PipeNeighborMeshClipper.GetReachAlongFacing(api, pos, i, neighborPos, PipeArmInventoryMaxReach);
                reachHash ^= (long)(reach * 512) * (i + 1);
            }
        }

        key ^= reachHash;

        if (useInserterHead)
            key ^= 1L << 13;

        if (ShouldRenderCenterCube(connectedSides, connectedToInventory, useInserterHead))
            key ^= 1L << 14;

        int straightAxis = GetStraightAxis(connectedSides, connectedToInventory, useInserterHead);
        if (straightAxis >= 0)
            key ^= 1L << (15 + straightAxis);

        return key;
    }

    /// <summary>
    /// Предметные и жидкостные трубы всегда с кубом. Обычные — на углах без противоположных пар.
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

        if (!hasInventoryConnection && pipeCount == 1)
            return false;

        if (!hasInventoryConnection && pipeCount >= 3 && HasOppositePair(connectedSides))
            return false;

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

    private static bool HasOppositePair(bool[] connectedSides)
    {
        return connectedSides[0] && connectedSides[2]
            || connectedSides[1] && connectedSides[3]
            || connectedSides[4] && connectedSides[5];
    }

    private static int GetStraightAxis(bool[] connectedSides, bool[] connectedToInventory, bool useInserterHead)
    {
        if (useInserterHead)
            return -1;

        int sideA = -1;
        int sideB = -1;
        int pipeCount = 0;

        for (int i = 0; i < 6; i++)
        {
            if (!connectedSides[i])
                continue;

            if (connectedToInventory[i])
                return -1;

            if (pipeCount == 0)
                sideA = i;
            else
                sideB = i;
            pipeCount++;
        }

        if (pipeCount == 1)
            return GetAxisForSide(sideA);

        if (pipeCount != 2 || !AreOppositeSides(sideA, sideB))
            return -1;

        return GetAxisForSide(sideA);
    }

    private static int GetAxisForSide(int side)
    {
        return side switch
        {
            0 or 2 => 0,
            1 or 3 => 1,
            4 or 5 => 2,
            _ => -1
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
