using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content;

internal static class PipeNeighborMeshClipper
{
    private const float MinReach = 0.15f;
    private const float RayEpsilon = 0.001f;
    /// <summary>Slight push into the surface so the arm sits flush (not a hair short of the model).</summary>
    private const float SurfaceBias = 0.02f;

    private static readonly Dictionary<long, MeshData> NeighborMeshCache = [];

    public static float GetReachAlongFacing(
        ICoreClientAPI api,
        BlockPos pipePos,
        int sideIndex,
        BlockPos neighborPos,
        float maxReach)
    {
        BlockFacing facing = BlockFacing.ALLFACES[sideIndex];
        var rayOrigin = new Vec3d(
            pipePos.X + 0.5 - neighborPos.X,
            pipePos.Y + 0.5 - neighborPos.Y,
            pipePos.Z + 0.5 - neighborPos.Z);
        var rayDir = new Vec3d(facing.Normali.X, facing.Normali.Y, facing.Normali.Z);

        // Prefer selection/collision boxes: open baskets (reed/cattail) have holes in the mesh
        // so a center ray misses the rim and either fails short or hits the floor.
        float closest = GetReachFromBlockBoxes(api, neighborPos, rayOrigin, rayDir);

        if (closest == float.MaxValue)
        {
            MeshData mesh = GetRenderedBlockMesh(api, neighborPos);
            if (mesh?.xyz != null && mesh.VerticesCount > 0)
            {
                closest = GetReachFromMesh(mesh, rayOrigin, rayDir);
            }
        }

        if (closest == float.MaxValue)
            return GameMath.Clamp(0.5f + SurfaceBias, MinReach, maxReach);

        // Sit flush against the hit surface rather than stopping short.
        closest += SurfaceBias;
        return GameMath.Clamp(closest, MinReach, maxReach);
    }

    /// <summary>
    /// Ray vs selection boxes (fallback collision). Boxes match the placed model silhouette.
    /// </summary>
    private static float GetReachFromBlockBoxes(
        ICoreClientAPI api,
        BlockPos neighborPos,
        Vec3d rayOrigin,
        Vec3d rayDir)
    {
        Vintagestory.API.Common.Block block = api.World.BlockAccessor.GetBlock(neighborPos);
        if (block == null || block.Id == 0)
            return float.MaxValue;

        Cuboidf[] boxes = null;
        try
        {
            boxes = block.GetSelectionBoxes(api.World.BlockAccessor, neighborPos);
        }
        catch
        {
            // ignore BE selection failures
        }

        if (boxes == null || boxes.Length == 0)
        {
            try
            {
                boxes = block.GetCollisionBoxes(api.World.BlockAccessor, neighborPos);
            }
            catch
            {
                return float.MaxValue;
            }
        }

        if (boxes == null || boxes.Length == 0)
            return float.MaxValue;

        float closest = float.MaxValue;
        for (int i = 0; i < boxes.Length; i++)
        {
            Cuboidf box = boxes[i];
            if (box == null)
                continue;

            if (TryRayAabbIntersect(
                    rayOrigin,
                    rayDir,
                    box.X1, box.Y1, box.Z1,
                    box.X2, box.Y2, box.Z2,
                    out double dist)
                && dist > RayEpsilon
                && dist < closest)
            {
                closest = (float)dist;
            }
        }

        return closest;
    }

    private static float GetReachFromMesh(MeshData mesh, Vec3d rayOrigin, Vec3d rayDir)
    {
        float closest = float.MaxValue;
        float[] xyz = mesh.xyz;
        int[] indices = mesh.Indices;
        int indexCount = mesh.IndicesCount;

        if (indices != null && indexCount >= 3)
        {
            for (int i = 0; i <= indexCount - 3; i += 3)
            {
                int i0 = indices[i] * 3;
                int i1 = indices[i + 1] * 3;
                int i2 = indices[i + 2] * 3;

                if (i2 + 2 >= xyz.Length)
                    continue;

                var v0 = new Vec3d(xyz[i0], xyz[i0 + 1], xyz[i0 + 2]);
                var v1 = new Vec3d(xyz[i1], xyz[i1 + 1], xyz[i1 + 2]);
                var v2 = new Vec3d(xyz[i2], xyz[i2 + 1], xyz[i2 + 2]);

                if (TryRayTriangleIntersect(rayOrigin, rayDir, v0, v1, v2, out double dist)
                    && dist > RayEpsilon
                    && dist < closest)
                {
                    closest = (float)dist;
                }
            }
        }

        if (closest == float.MaxValue)
            closest = GetReachFromVertexBounds(xyz, mesh.VerticesCount, rayOrigin, rayDir);

        return closest;
    }

    private static MeshData GetRenderedBlockMesh(ICoreClientAPI api, BlockPos neighborPos)
    {
        Vintagestory.API.Common.Block block = api.World.BlockAccessor.GetBlock(neighborPos);
        if (block == null || block.Id == 0)
            return null;

        long cacheKey = BuildNeighborMeshCacheKey(api, neighborPos, block);
        if (NeighborMeshCache.TryGetValue(cacheKey, out MeshData cached))
            return cached;

        MeshData mesh = api.TesselatorManager.GetDefaultBlockMesh(block)?.Clone();
        if (mesh == null)
        {
            api.Tesselator.TesselateBlock(block, out mesh);
            api.TesselatorManager.ThreadDispose();
        }

        if (mesh == null)
            return null;

        int[] lightRgbs = null;
        block.OnJsonTesselation(ref mesh, ref lightRgbs, neighborPos, null, 0);

        NeighborMeshCache[cacheKey] = mesh;
        return mesh;
    }

    private static long BuildNeighborMeshCacheKey(ICoreClientAPI api, BlockPos pos, Vintagestory.API.Common.Block block)
    {
        long key = block.Id;
        BlockEntity be = api.World.BlockAccessor.GetBlockEntity(pos);
        if (be != null)
            key ^= ((long)be.GetHashCode() << 32);

        key ^= ((long)pos.X << 48) ^ ((long)pos.Y << 32) ^ (uint)pos.Z;
        return key;
    }

    private static float GetReachFromVertexBounds(float[] xyz, int vertexCount, Vec3d rayOrigin, Vec3d rayDir)
    {
        float closest = float.MaxValue;

        for (int v = 0; v < vertexCount; v++)
        {
            int idx = v * 3;
            if (idx + 2 >= xyz.Length)
                break;

            double vx = xyz[idx] - rayOrigin.X;
            double vy = xyz[idx + 1] - rayOrigin.Y;
            double vz = xyz[idx + 2] - rayOrigin.Z;
            double t = vx * rayDir.X + vy * rayDir.Y + vz * rayDir.Z;

            if (t > RayEpsilon && t < closest)
                closest = (float)t;
        }

        return closest;
    }

    /// <summary>Slab-method ray vs axis-aligned box (block-local coords).</summary>
    private static bool TryRayAabbIntersect(
        Vec3d origin,
        Vec3d dir,
        float minX, float minY, float minZ,
        float maxX, float maxY, float maxZ,
        out double distance)
    {
        distance = 0;

        double tMin = double.NegativeInfinity;
        double tMax = double.PositiveInfinity;

        if (!ClipSlab(origin.X, dir.X, minX, maxX, ref tMin, ref tMax))
            return false;
        if (!ClipSlab(origin.Y, dir.Y, minY, maxY, ref tMin, ref tMax))
            return false;
        if (!ClipSlab(origin.Z, dir.Z, minZ, maxZ, ref tMin, ref tMax))
            return false;

        if (tMax < RayEpsilon)
            return false;

        // Entering face distance (or 0 if origin is inside the box).
        double tHit = tMin >= RayEpsilon ? tMin : tMax;
        if (tHit < RayEpsilon)
            return false;

        distance = tHit;
        return true;
    }

    private static bool ClipSlab(double origin, double dir, float min, float max, ref double tMin, ref double tMax)
    {
        if (Math.Abs(dir) < 1e-12)
        {
            // Parallel to slab — miss if outside.
            return origin >= min && origin <= max;
        }

        double inv = 1.0 / dir;
        double t1 = (min - origin) * inv;
        double t2 = (max - origin) * inv;
        if (t1 > t2)
            (t1, t2) = (t2, t1);

        tMin = Math.Max(tMin, t1);
        tMax = Math.Min(tMax, t2);
        return tMin <= tMax;
    }

    private static bool TryRayTriangleIntersect(
        Vec3d origin,
        Vec3d dir,
        Vec3d v0,
        Vec3d v1,
        Vec3d v2,
        out double distance)
    {
        distance = 0;

        double edge1x = v1.X - v0.X;
        double edge1y = v1.Y - v0.Y;
        double edge1z = v1.Z - v0.Z;
        double edge2x = v2.X - v0.X;
        double edge2y = v2.Y - v0.Y;
        double edge2z = v2.Z - v0.Z;

        double px = dir.Y * edge2z - dir.Z * edge2y;
        double py = dir.Z * edge2x - dir.X * edge2z;
        double pz = dir.X * edge2y - dir.Y * edge2x;

        double det = edge1x * px + edge1y * py + edge1z * pz;
        if (Math.Abs(det) < 1e-8)
            return false;

        double invDet = 1.0 / det;
        double tx = origin.X - v0.X;
        double ty = origin.Y - v0.Y;
        double tz = origin.Z - v0.Z;

        double u = (tx * px + ty * py + tz * pz) * invDet;
        if (u < 0 || u > 1)
            return false;

        double qx = ty * edge1z - tz * edge1y;
        double qy = tz * edge1x - tx * edge1z;
        double qz = tx * edge1y - ty * edge1x;

        double v = (dir.X * qx + dir.Y * qy + dir.Z * qz) * invDet;
        if (v < 0 || u + v > 1)
            return false;

        double t = (edge2x * qx + edge2y * qy + edge2z * qz) * invDet;
        if (t < RayEpsilon)
            return false;

        distance = t;
        return true;
    }
}
