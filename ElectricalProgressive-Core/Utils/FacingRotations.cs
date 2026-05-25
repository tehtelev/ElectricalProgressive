using Cairo.Freetype;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using static HarmonyLib.Code;

namespace ElectricalProgressive.Utils;

public static class FacingRotations
{
    // Словарь вращений 
    public static readonly Dictionary<Facing, Vec3f> Rotations = new()
    {
        { Facing.NorthEast, new Vec3f(90f, 270f, 0f) },
        { Facing.NorthWest, new Vec3f(90f, 90f, 0f) },
        { Facing.NorthUp,   new Vec3f(90f, 0f, 0f) },
        { Facing.NorthDown, new Vec3f(90f, 180f, 0f) },

        { Facing.EastNorth, new Vec3f(0f, 0f, 90f) },
        { Facing.EastSouth, new Vec3f(180f, 0f, 90f) },
        { Facing.EastUp,    new Vec3f(90f, 0f, 90f) },
        { Facing.EastDown,  new Vec3f(270f, 0f, 90f) },

        { Facing.SouthEast, new Vec3f(90f, 270f, 180f) },
        { Facing.SouthWest, new Vec3f(90f, 90f, 180f) },
        { Facing.SouthUp,   new Vec3f(90f, 0f, 180f) },
        { Facing.SouthDown, new Vec3f(90f, 180f, 180f) },

        { Facing.WestNorth, new Vec3f(0f, 0f, 270f) },
        { Facing.WestSouth, new Vec3f(180f, 0f, 270f) },
        { Facing.WestUp,    new Vec3f(90f, 0f, 270f) },
        { Facing.WestDown,  new Vec3f(270f, 0f, 270f) },

        { Facing.UpNorth,   new Vec3f(0f, 0f, 180f) },
        { Facing.UpEast,    new Vec3f(0f, 270f, 180f) },
        { Facing.UpSouth,   new Vec3f(0f, 180f, 180f) },
        { Facing.UpWest,    new Vec3f(0f, 90f, 180f) },

        { Facing.DownNorth, new Vec3f(0f, 0f, 0f) },
        { Facing.DownEast,  new Vec3f(0f, 270f, 0f) },
        { Facing.DownSouth, new Vec3f(0f, 180f, 0f) },
        { Facing.DownWest,  new Vec3f(0f, 90f, 0f) }
    };

    /// <summary>
    /// Оптимизированное применение вращения мешей. 
    /// Работает только если facing содержит ровно один бит из словаря.
    /// </summary>
    public static void ApplyRotations(MeshData mesh, Facing facing)
    {
        // TryGetValue безопаснее и быстрее, чем прямой доступ по ключу [facing],
        // так как не выбросит исключение, если вдруг придет неизвестный флаг (например, None).
        if (Rotations.TryGetValue(facing, out var rot))
        {
            var origin = new Vec3f(0.5f, 0.5f, 0.5f);

            mesh.Rotate(origin,
                rot.X * GameMath.DEG2RAD,
                rot.Y * GameMath.DEG2RAD,
                rot.Z * GameMath.DEG2RAD);
        }
    }


    /// <summary>
    /// Оптимизированное применение вращения коллизиям/выделениям. 
    /// Работает только если facing содержит ровно один бит из словаря.
    /// </summary>
    public static void ApplyRotations(ref Cuboidf[] boxes, Facing facing)
    {
        // TryGetValue безопаснее и быстрее, чем прямой доступ по ключу [facing],
        // так как не выбросит исключение, если вдруг придет неизвестный флаг (например, None).
        if (Rotations.TryGetValue(facing, out var rot))
        {
            var origin = new Vec3d(0.5f, 0.5f, 0.5f);

            var boxes2=new Cuboidf[boxes.Length];

            for (int i = 0; i < boxes.Length; i++)
            {
                boxes2[i] = boxes[i].RotatedCopy(rot.X, rot.Y, rot.Z, origin);
            }

            boxes = boxes2;
        }
        
        
    }
}
