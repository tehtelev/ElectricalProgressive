using ElectricalProgressive.Utils;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ESolenoid;

internal static class SolenoidMesh
{
    public static void Apply(ref MeshData sourceMesh, Facing facing, Dictionary<(Facing, int), MeshData> cache)
    {
        var key = (facing, sourceMesh.VerticesCount);
        if (!cache.TryGetValue(key, out var mesh))
        {
            mesh = sourceMesh.Clone();
            FacingRotations.ApplyRotations(mesh, facing);
            cache[key] = mesh;
        }

        sourceMesh = mesh;
    }
}
