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
    private const int MeshCacheVersion = 34;

    private const float PipeCenter = 0.5f;

    // pipe_part.json (TubeBaseWest): от центра блока до внешнего конца ~1.005.
    private const float PipeArmFullReach = 1.0051f;
    private const float PipeArmInventoryMaxReach = 1.5f;

    private static readonly Dictionary<string, Shape> ShapeCache = [];
    private static readonly Dictionary<long, MeshData> MeshCache = [];
    // key: (blockId << 1) | lodBit
    private static readonly Dictionary<int, MeshData> CenterCubeCache = [];
    private static readonly Dictionary<int, MeshData> PipePartBaseCache = [];
    private static readonly Dictionary<int, MeshData> StraightBaseCache = [];
    // key: (blockId << 4) | (side << 1) | lodBit
    private static readonly Dictionary<long, MeshData> RotatedArmCache = [];
    // key: (blockId << 3) | (axis << 1) | lodBit
    private static readonly Dictionary<long, MeshData> StraightRotatedCache = [];
    private static readonly Dictionary<int, MeshData> InserterBaseCache = [];
    private static readonly Dictionary<long, MeshData> RotatedInserterCache = []; // (blockId << 3) | side
    private static bool inserterHeadShapeMissing;

    private static readonly Vec3f Origin = new(PipeCenter, PipeCenter, PipeCenter);

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
        (0f, 0f, 0f),        // north
        (0f, 90f, 0f),       // east
        (0f, 180f, 0f),      // south
        (0f, -90f, 0f),      // west
        (180f, 0f, 0f),      // up
        (180f, -90f, 0f),    // down
    ];

    /// <summary>
    /// Same pattern as EP cables/connectors: engine dual-pass with shape vs lod2shape.
    /// sourceMesh is already the correct LOD mesh; VerticesCount differs between near and far.
    /// </summary>
    public static bool IsLod2Pass(ICoreClientAPI api, Vintagestory.API.Common.Block block, MeshData sourceMesh)
    {
        if (api == null || block == null || sourceMesh == null)
            return false;

        EnsureLod2Mesh(api, block);

        MeshData lod2Mesh = block.Lod2Mesh;
        if (lod2Mesh == null || lod2Mesh.VerticesCount <= 0)
            return false;

        // Engine may pass Lod2Mesh (or an alternate with the same topology).
        if (ReferenceEquals(sourceMesh, lod2Mesh))
            return true;

        MeshData nearMesh = api.TesselatorManager.GetDefaultBlockMesh(block);
        if (nearMesh != null && ReferenceEquals(sourceMesh, nearMesh))
            return false;

        // EP cable-dot style: VerticesCount differs for LOD0 and LOD2.
        int farVerts = lod2Mesh.VerticesCount;
        int nearVerts = nearMesh?.VerticesCount ?? -1;
        if (nearVerts > 0 && nearVerts == farVerts)
            return false;

        return sourceMesh.VerticesCount == farVerts;
    }

    /// <summary>
    /// Ensure lod2shape/Lod2Mesh exist so the engine runs the far pass (like other EP blocks).
    /// </summary>
    public static void EnsureLod2Mesh(ICoreClientAPI api, Vintagestory.API.Common.Block block)
    {
        if (api == null || block == null || block.Lod2Mesh != null)
            return;

        if (block.Lod2Shape == null)
        {
            block.Lod2Shape = new CompositeShape
            {
                Base = new AssetLocation(ShapeDomain, "block/itempipe/center-lod2")
            };
        }

        Shape shape = GetShape(api, $"{ShapePath}/center-lod2.json");
        if (shape == null)
            return;

        api.Tesselator.TesselateShape(block, shape, out MeshData mesh);
        if (mesh != null && mesh.VerticesCount > 0)
            block.Lod2Mesh = mesh;
    }

    /// <summary>
    /// Tessellate heavy pipe parts once per block type so first place/transform is not a hitch.
    /// Prewarms both near and lod2 part caches (EP dual-pass).
    /// </summary>
    public static void Prewarm(ICoreClientAPI api, Vintagestory.API.Common.Block block)
    {
        if (api == null || block == null)
            return;

        EnsureLod2Mesh(api, block);

        for (int lod = 0; lod <= 1; lod++)
        {
            bool isLod2 = lod == 1;
            GetCenterCubeSource(api, block, isLod2);
            GetPipePartBase(api, block, isLod2);
            GetStraightBase(api, block, isLod2);

            for (int side = 0; side < 6; side++)
                GetRotatedArmSource(api, block, side, isLod2);

            for (int axis = 0; axis < 3; axis++)
                GetStraightRotatedSource(api, block, axis, isLod2);
        }

        GetInserterBase(api, block);
    }

    public static MeshData Build(
        ICoreClientAPI api,
        Vintagestory.API.Common.Block block,
        BlockPos pos,
        bool[] connectedSides,
        bool[] connectedToInventory,
        bool useInserterHead,
        bool isLod2 = false)
    {
        if (connectedSides == null || connectedSides.Length < 6)
            return null;

        connectedToInventory ??= new bool[6];

        long cacheKey = BuildCacheKey(api, block.Id, pos, connectedSides, connectedToInventory, useInserterHead, isLod2);
        if (MeshCache.TryGetValue(cacheKey, out MeshData cached))
            return cached;

        MeshData finalMesh = null;
        int straightAxis = GetStraightAxis(connectedSides, connectedToInventory, useInserterHead);

        if (straightAxis >= 0)
        {
            // Clone once into owned mesh — cache sources must not be mutated.
            MeshData straightMesh = GetStraightRotatedSource(api, block, straightAxis, isLod2);
            if (straightMesh != null)
                finalMesh = straightMesh.Clone();

            MeshCache[cacheKey] = finalMesh;
            return finalMesh;
        }

        if (ShouldRenderCenterCube(connectedSides, connectedToInventory, useInserterHead))
            AppendCachedPart(ref finalMesh, GetCenterCubeSource(api, block, isLod2));

        MeshData pipePartBase = GetPipePartBase(api, block, isLod2);
        if (pipePartBase != null)
        {
            for (int i = 0; i < 6; i++)
            {
                if (!ShouldRenderArm(i, connectedSides))
                    continue;

                if (connectedToInventory[i])
                {
                    // Inventory reach is position-dependent: scale unrotated west arm, then rotate.
                    MeshData armMesh = pipePartBase.Clone();
                    BlockPos neighborPos = pos.AddCopy(BlockFacing.ALLFACES[i]);
                    float reach = PipeNeighborMeshClipper.GetReachAlongFacing(
                        api,
                        pos,
                        i,
                        neighborPos,
                        PipeArmInventoryMaxReach);
                    ScaleArmToReach(armMesh, reach);
                    var (rx, ry, rz) = SideRotationsDeg[i];
                    AppendOwnedPart(ref finalMesh, RotateMesh(armMesh, rx, ry, rz, Origin));
                }
                else
                {
                    // Shared rotated arm — append without mutating cache.
                    AppendCachedPart(ref finalMesh, GetRotatedArmSource(api, block, i, isLod2));
                }
            }
        }

        // Inserter heads are small; keep full detail at all LODs (shape is optional).
        if (useInserterHead && !inserterHeadShapeMissing)
        {
            for (int i = 0; i < 6; i++)
            {
                if (!connectedSides[i] || !connectedToInventory[i])
                    continue;

                AppendCachedPart(ref finalMesh, GetRotatedInserterSource(api, block, i));
            }
        }

        MeshCache[cacheKey] = finalMesh;
        return finalMesh;
    }

    private static int PartCacheKey(int blockId, bool isLod2)
        => (blockId << 1) | (isLod2 ? 1 : 0);

    private static bool ShouldRenderArm(int sideIndex, bool[] connectedSides)
        => connectedSides[sideIndex];

    private static void ScaleArmToReach(MeshData mesh, float targetReach)
    {
        float currentReach = GetArmReachAlongScaleAxis(mesh);
        if (currentReach <= 0f)
            currentReach = PipeArmFullReach;

        float scale = targetReach / currentReach;
        mesh.Scale(Origin, scale, 1f, 1f);
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
        if (mesh == null)
            return null;

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
        bool useInserterHead,
        bool isLod2)
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

        if (isLod2)
            key ^= 1L << 18;

        return key;
    }

    /// <summary>
    /// Filter/insertion pipes always keep the center cube.
    /// Normal pipes skip it on pure straight runs and some multi-way opposite pairs.
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

    /// <summary>
    /// Pure pipe runs (1 end or opposite pair, no inventory) use straight.json — normal pipes only.
    /// Filter/insertion pipes never use straight: they always build center + arms.
    /// </summary>
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

            if (connectedToInventory != null && connectedToInventory[i])
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

    private static MeshData GetCenterCubeSource(ICoreClientAPI api, Vintagestory.API.Common.Block block, bool isLod2)
    {
        int key = PartCacheKey(block.Id, isLod2);
        if (!CenterCubeCache.TryGetValue(key, out MeshData center))
        {
            string path = isLod2 ? $"{ShapePath}/center-lod2.json" : $"{ShapePath}/center.json";
            center = TesselateShape(api, block, path);
            // Fall back to full detail if LOD asset missing.
            if (center == null && isLod2)
                center = TesselateShape(api, block, $"{ShapePath}/center.json");
            if (center != null)
                CenterCubeCache[key] = center;
        }

        return center;
    }

    private static MeshData GetPipePartBase(ICoreClientAPI api, Vintagestory.API.Common.Block block, bool isLod2)
    {
        int key = PartCacheKey(block.Id, isLod2);
        if (!PipePartBaseCache.TryGetValue(key, out MeshData part))
        {
            string path = isLod2 ? $"{ShapePath}/pipe_part-lod2.json" : $"{ShapePath}/pipe_part.json";
            part = TesselateShape(api, block, path);
            if (part == null && isLod2)
                part = TesselateShape(api, block, $"{ShapePath}/pipe_part.json");
            if (part != null)
                PipePartBaseCache[key] = part;
        }

        return part;
    }

    private static MeshData GetStraightBase(ICoreClientAPI api, Vintagestory.API.Common.Block block, bool isLod2)
    {
        int key = PartCacheKey(block.Id, isLod2);
        if (!StraightBaseCache.TryGetValue(key, out MeshData straight))
        {
            string path = isLod2 ? $"{ShapePath}/straight-lod2.json" : $"{ShapePath}/straight.json";
            straight = TesselateShape(api, block, path);
            if (straight == null && isLod2)
                straight = TesselateShape(api, block, $"{ShapePath}/straight.json");
            if (straight != null)
                StraightBaseCache[key] = straight;
        }

        return straight;
    }

    private static MeshData GetRotatedArmSource(ICoreClientAPI api, Vintagestory.API.Common.Block block, int side, bool isLod2)
    {
        long key = ((long)block.Id << 4) | ((long)side << 1) | (uint)(isLod2 ? 1 : 0);
        if (RotatedArmCache.TryGetValue(key, out MeshData cached))
            return cached;

        MeshData basePart = GetPipePartBase(api, block, isLod2);
        if (basePart == null)
            return null;

        var (rx, ry, rz) = SideRotationsDeg[side];
        MeshData rotated = RotateMesh(basePart.Clone(), rx, ry, rz, Origin);
        RotatedArmCache[key] = rotated;
        return rotated;
    }

    private static MeshData GetStraightRotatedSource(ICoreClientAPI api, Vintagestory.API.Common.Block block, int axis, bool isLod2)
    {
        long key = ((long)block.Id << 3) | ((long)axis << 1) | (uint)(isLod2 ? 1 : 0);
        if (StraightRotatedCache.TryGetValue(key, out MeshData cached))
            return cached;

        MeshData baseStraight = GetStraightBase(api, block, isLod2);
        if (baseStraight == null)
            return null;

        var (rx, ry, rz) = StraightRotationsDeg[axis];
        MeshData rotated = RotateMesh(baseStraight.Clone(), rx, ry, rz, Origin);
        StraightRotatedCache[key] = rotated;
        return rotated;
    }

    private static MeshData GetInserterBase(ICoreClientAPI api, Vintagestory.API.Common.Block block)
    {
        if (inserterHeadShapeMissing)
            return null;

        if (InserterBaseCache.TryGetValue(block.Id, out MeshData head))
            return head;

        if (GetShape(api, $"{ShapePath}/inserter_head.json") == null)
        {
            inserterHeadShapeMissing = true;
            return null;
        }

        head = TesselateShape(api, block, $"{ShapePath}/inserter_head.json");
        if (head != null)
            InserterBaseCache[block.Id] = head;
        return head;
    }

    private static MeshData GetRotatedInserterSource(ICoreClientAPI api, Vintagestory.API.Common.Block block, int side)
    {
        long key = ((long)block.Id << 3) | (uint)side;
        if (RotatedInserterCache.TryGetValue(key, out MeshData cached))
            return cached;

        MeshData baseHead = GetInserterBase(api, block);
        if (baseHead == null)
            return null;

        var (rx, ry, rz) = InserterRotationsDeg[side];
        MeshData rotated = RotateMesh(baseHead.Clone(), rx, ry, rz, Origin);
        RotatedInserterCache[key] = rotated;
        return rotated;
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
            // Cache misses too so missing shapes (inserter_head) are not re-queried every frame.
            ShapeCache[key] = shape;
        }

        return shape;
    }

    /// <summary>Append a shared cached part (clone only for the first piece).</summary>
    private static void AppendCachedPart(ref MeshData target, MeshData cachedPart)
    {
        if (cachedPart == null)
            return;

        if (target == null || target.VerticesCount == 0)
            target = cachedPart.Clone();
        else
            target.AddMeshData(cachedPart);
    }

    /// <summary>Append a mesh we already own (no extra clone).</summary>
    private static void AppendOwnedPart(ref MeshData target, MeshData ownedPart)
    {
        if (ownedPart == null)
            return;

        if (target == null || target.VerticesCount == 0)
            target = ownedPart;
        else
            target.AddMeshData(ownedPart);
    }
}
