using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ProtoBuf;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils;

[Flags]
[ProtoContract]
public enum Facing {
    None = 0b_0000_0000_0000_0000_0000_0000,

    AllAll = 0b_1111_1111_1111_1111_1111_1111,
    AllNorth = Facing.EastNorth | Facing.WestNorth | Facing.UpNorth | Facing.DownNorth,
    AllEast = Facing.NorthEast | Facing.SouthEast | Facing.UpEast | Facing.DownEast,
    AllSouth = Facing.EastSouth | Facing.WestSouth | Facing.UpSouth | Facing.DownSouth,
    AllWest = Facing.NorthWest | Facing.SouthWest | Facing.UpWest | Facing.DownWest,
    AllUp = Facing.NorthUp | Facing.EastUp | Facing.SouthUp | Facing.WestUp,
    AllDown = Facing.NorthDown | Facing.EastDown | Facing.SouthDown | Facing.WestDown,

    NorthAll = Facing.NorthEast | Facing.NorthWest | Facing.NorthUp | Facing.NorthDown,
    NorthEast = 0b_1000_0000_0000_0000_0000_0000,
    NorthWest = 0b_0100_0000_0000_0000_0000_0000,
    NorthUp = 0b_0010_0000_0000_0000_0000_0000,
    NorthDown = 0b_0001_0000_0000_0000_0000_0000,

    EastAll = Facing.EastNorth | Facing.EastSouth | Facing.EastUp | Facing.EastDown,
    EastNorth = 0b_0000_1000_0000_0000_0000_0000,
    EastSouth = 0b_0000_0100_0000_0000_0000_0000,
    EastUp = 0b_0000_0010_0000_0000_0000_0000,
    EastDown = 0b_0000_0001_0000_0000_0000_0000,

    SouthAll = Facing.SouthEast | Facing.SouthWest | Facing.SouthUp | Facing.SouthDown,
    SouthEast = 0b_0000_0000_1000_0000_0000_0000,
    SouthWest = 0b_0000_0000_0100_0000_0000_0000,
    SouthUp = 0b_0000_0000_0010_0000_0000_0000,
    SouthDown = 0b_0000_0000_0001_0000_0000_0000,

    WestAll = Facing.WestNorth | Facing.WestSouth | Facing.WestUp | Facing.WestDown,
    WestNorth = 0b_0000_0000_0000_1000_0000_0000,
    WestSouth = 0b_0000_0000_0000_0100_0000_0000,
    WestUp = 0b_0000_0000_0000_0010_0000_0000,
    WestDown = 0b_0000_0000_0000_0001_0000_0000,

    UpAll = Facing.UpNorth | Facing.UpEast | Facing.UpSouth | Facing.UpWest,
    UpNorth = 0b_0000_0000_0000_0000_1000_0000,
    UpEast = 0b_0000_0000_0000_0000_0100_0000,
    UpSouth = 0b_0000_0000_0000_0000_0010_0000,
    UpWest = 0b_0000_0000_0000_0000_0001_0000,

    DownAll = Facing.DownNorth | Facing.DownEast | Facing.DownSouth | Facing.DownWest,
    DownNorth = 0b_0000_0000_0000_0000_0000_1000,
    DownEast = 0b_0000_0000_0000_0000_0000_0100,
    DownSouth = 0b_0000_0000_0000_0000_0000_0010,
    DownWest = 0b_0000_0000_0000_0000_0000_0001
}

/// <summary>
/// Работа с направлениями (Facing) и гранями (BlockFacing).
/// Все оптимизировано для быстрого доступа и минимизации аллокаций
/// </summary>
public static class FacingHelper
{
    private static readonly BlockFacing[] blockfaces = {
        BlockFacing.NORTH,
        BlockFacing.EAST,
        BlockFacing.SOUTH,
        BlockFacing.WEST,
        BlockFacing.UP,
        BlockFacing.DOWN
    };

    private static readonly Facing[] facesAll = {
        Facing.NorthAll,
        Facing.EastAll,
        Facing.SouthAll,
        Facing.WestAll,
        Facing.UpAll,
        Facing.DownAll
    };

    private static readonly Facing[] directionsAll = {
        Facing.AllNorth,
        Facing.AllEast,
        Facing.AllSouth,
        Facing.AllWest,
        Facing.AllUp,
        Facing.AllDown
    };



    /// <summary>
    /// Выдает Facing по BlockFacing, но для граней (Faces)
    /// </summary>
    /// <param name="face"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Facing FromFace(BlockFacing face)
    {
        return (face?.Index >= 0 && face?.Index < 6) ? facesAll[face.Index] : Facing.None;
    }

    /// <summary>
    /// Выдает Facing по BlockFacing, но для направлений (Directions)
    /// </summary>
    /// <param name="direction"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Facing FromDirection(BlockFacing direction)
    {
        return (direction?.Index >= 0 && direction?.Index < 6) ? directionsAll[direction.Index] : Facing.None;
    }


    /// <summary>
    /// Выдает по индексу соответствующий BlockFacing
    /// </summary>
    /// <param name="flag"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BlockFacing BlockFacingFromIndex(int flag)
    {
        return (flag >= 0 && flag < blockfaces.Length) ? blockfaces[flag] : null!;
    }


    /// <summary>
    /// Выдает Facing по BlockFacing, но для граней (Faces) и направлений (Directions).
    /// </summary>
    /// <param name="face"></param>
    /// <param name="direction"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Facing From(BlockFacing face, BlockFacing direction)
    {
        return FromFace(face) & FromDirection(direction);
    }





    /// <summary>
    /// Выдает все направления, которые соответствуют флагам Face.
    /// Использовать ToList() вне foreach, и если нужно получить весь список
    /// </summary>
    /// <param name="self"></param>
    /// <returns></returns>
    public static IEnumerable<BlockFacing> Faces(Facing self)
    {
        if ((self & Facing.NorthAll) != 0)
            yield return BlockFacing.NORTH;
        if ((self & Facing.EastAll) != 0)
            yield return BlockFacing.EAST;
        if ((self & Facing.SouthAll) != 0)
            yield return BlockFacing.SOUTH;
        if ((self & Facing.WestAll) != 0)
            yield return BlockFacing.WEST;
        if ((self & Facing.UpAll) != 0)
            yield return BlockFacing.UP;
        if ((self & Facing.DownAll) != 0)
            yield return BlockFacing.DOWN;
    }



    /// <summary>
    /// Выдает все направления, которые соответствуют флагам Directions.
    /// Использовать ToList() вне foreach, и если нужно получить весь список
    /// </summary>
    /// <param name="self"></param>
    /// <returns></returns>
    public static IEnumerable<BlockFacing> Directions(Facing self)
    {
        if ((self & Facing.AllNorth) != 0)
            yield return BlockFacing.NORTH;
        if ((self & Facing.AllEast) != 0)
            yield return BlockFacing.EAST;
        if ((self & Facing.AllSouth) != 0)
            yield return BlockFacing.SOUTH;
        if ((self & Facing.AllWest) != 0)
            yield return BlockFacing.WEST;
        if ((self & Facing.AllUp) != 0)
            yield return BlockFacing.UP;
        if ((self & Facing.AllDown) != 0)
            yield return BlockFacing.DOWN;
    }




    /// <summary>
    /// Заполняет buffer «гранями» (Faces) заданного флага self.
    /// buffer при этом не аллоцируется заново, а просто очищается и заполняется.
    /// </summary>
    public static void FillFaces(Facing self, List<BlockFacing> buffer)
    {
        buffer.Clear();
        if ((self & Facing.NorthAll) != 0) buffer.Add(BlockFacing.NORTH);
        if ((self & Facing.EastAll) != 0) buffer.Add(BlockFacing.EAST);
        if ((self & Facing.SouthAll) != 0) buffer.Add(BlockFacing.SOUTH);
        if ((self & Facing.WestAll) != 0) buffer.Add(BlockFacing.WEST);
        if ((self & Facing.UpAll) != 0) buffer.Add(BlockFacing.UP);
        if ((self & Facing.DownAll) != 0) buffer.Add(BlockFacing.DOWN);
    }

    /// <summary>
    /// Заполняет buffer «направлениями» (Directions) заданного флага self.
    /// buffer при этом не аллоцируется заново, а просто очищается и заполняется.
    /// </summary>
    public static void FillDirections(Facing self, List<BlockFacing> buffer)
    {
        buffer.Clear();
        if ((self & Facing.AllNorth) != 0) buffer.Add(BlockFacing.NORTH);
        if ((self & Facing.AllEast) != 0) buffer.Add(BlockFacing.EAST);
        if ((self & Facing.AllSouth) != 0) buffer.Add(BlockFacing.SOUTH);
        if ((self & Facing.AllWest) != 0) buffer.Add(BlockFacing.WEST);
        if ((self & Facing.AllUp) != 0) buffer.Add(BlockFacing.UP);
        if ((self & Facing.AllDown) != 0) buffer.Add(BlockFacing.DOWN);
    }




    /// <summary>
    /// Возвращает Facing, который соответствует всем граням (Faces) в self.
    /// </summary>
    /// <param name="self"></param>
    /// <returns></returns>
    public static Facing FullFace(Facing self)
    {
        return Faces(self).ToList().Aggregate(Facing.None, (current, face) => current | FromFace(face));
    }


    /// <summary>
    /// Выдает количество направлений в Facing.
    /// </summary>
    /// <param name="self"></param>
    /// <returns></returns>
    public static int Count(Facing self)
    {
        var count = 0;

        while (self != Facing.None)
        {
            count++;
            self &= self - 1;
        }

        return count;
    }


    /// <summary>
    /// Получает индекс грани (Face) по Facing.
    /// </summary>
    /// <param name="facing"></param>
    /// <returns></returns>
    public static int GetFaceIndex(Facing facing)
    {
        if (facing == Facing.None) return -1;

        if ((facing & Facing.NorthEast) != 0) return 0;
        if ((facing & Facing.NorthWest) != 0) return 0;
        if ((facing & Facing.NorthUp) != 0) return 0;
        if ((facing & Facing.NorthDown) != 0) return 0;

        if ((facing & Facing.EastNorth) != 0) return 1;
        if ((facing & Facing.EastSouth) != 0) return 1;
        if ((facing & Facing.EastUp) != 0) return 1;
        if ((facing & Facing.EastDown) != 0) return 1;

        if ((facing & Facing.SouthEast) != 0) return 2;
        if ((facing & Facing.SouthWest) != 0) return 2;
        if ((facing & Facing.SouthUp) != 0) return 2;
        if ((facing & Facing.SouthDown) != 0) return 2;

        if ((facing & Facing.WestNorth) != 0) return 3;
        if ((facing & Facing.WestSouth) != 0) return 3;
        if ((facing & Facing.WestUp) != 0) return 3;
        if ((facing & Facing.WestDown) != 0) return 3;

        if ((facing & Facing.UpNorth) != 0) return 4;
        if ((facing & Facing.UpEast) != 0) return 4;
        if ((facing & Facing.UpSouth) != 0) return 4;
        if ((facing & Facing.UpWest) != 0) return 4;

        if ((facing & Facing.DownNorth) != 0) return 5;
        if ((facing & Facing.DownEast) != 0) return 5;
        if ((facing & Facing.DownSouth) != 0) return 5;
        if ((facing & Facing.DownWest) != 0) return 5;

        return -1;
    }





}
