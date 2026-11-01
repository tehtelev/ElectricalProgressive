using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils;

public class Selection
{
    private readonly bool _didOffset;
    private readonly Vec3d _hitPosition;

    public Selection(Vec3d hitPosition, bool didOffset)
    {
        this._hitPosition = hitPosition;
        this._didOffset = didOffset;
    }

    public Selection(BlockSelection blockSelection)
    {
        this._hitPosition = blockSelection.HitPosition;
        this._didOffset = blockSelection.DidOffset;
    }

    public Vec2d Position2D
    {
        get
        {
            switch (this.Face.Index)
            {
                case BlockFacing.indexNORTH:
                case BlockFacing.indexSOUTH:
                    return new Vec2d(this._hitPosition.X, this._hitPosition.Y);
                case BlockFacing.indexEAST:
                case BlockFacing.indexWEST:
                    return new Vec2d(this._hitPosition.Y, this._hitPosition.Z);
                case BlockFacing.indexUP:
                case BlockFacing.indexDOWN:
                    return new Vec2d(this._hitPosition.X, this._hitPosition.Z);
                default:
                    throw new Exception();
            }
        }
    }

    public BlockFacing Direction
    {
        get
        {
            switch (this.Face.Index)
            {
                case BlockFacing.indexNORTH:
                case BlockFacing.indexSOUTH:
                    return this.DirectionHelper(BlockFacing.EAST, BlockFacing.WEST, BlockFacing.UP, BlockFacing.DOWN);
                case BlockFacing.indexEAST:
                case BlockFacing.indexWEST:
                    return this.DirectionHelper(BlockFacing.UP, BlockFacing.DOWN, BlockFacing.SOUTH, BlockFacing.NORTH);
                case BlockFacing.indexUP:
                case BlockFacing.indexDOWN:
                    return this.DirectionHelper(
                        BlockFacing.EAST,
                        BlockFacing.WEST,
                        BlockFacing.SOUTH,
                        BlockFacing.NORTH
                    );
                default:
                    throw new Exception();
            }
        }
    }

    /// <summary>
    /// Берем сторону, на которую указывает вектор от центра блока к точке попадания.
    /// </summary>
    public BlockFacing Face
    {
        get
        {
            var normalize = this._hitPosition.SubCopy(0.5f, 0.5f, 0.5f);

            // Находим координату с наибольшим абсолютным значением
            var absX = (float)Math.Abs(normalize.X);
            var absY = (float)Math.Abs(normalize.Y);
            var absZ = (float)Math.Abs(normalize.Z);

            if (absX >= absY && absX >= absZ)
            {
                return (normalize.X > 0) ^ this._didOffset
                    ? BlockFacing.EAST
                    : BlockFacing.WEST;
            }

            if (absZ >= absX && absZ >= absY)
            {
                return (normalize.Z > 0) ^ this._didOffset
                    ? BlockFacing.SOUTH
                    : BlockFacing.NORTH;
            }

            // Оставшийся случай (Y)
            return (normalize.Y > 0) ^ this._didOffset
                ? BlockFacing.UP
                : BlockFacing.DOWN;
        }
    }



    public Facing Facing => FacingHelper.From(this.Face, this.Direction);

    private static Vec2d Rotate(Vec2d point, Vec2d origin, double angle)
    {
        return new Vec2d(
            GameMath.Cos(angle) * (point.X - origin.X) - GameMath.Sin(angle) * (point.Y - origin.Y) + origin.X,
            GameMath.Sin(angle) * (point.X - origin.X) + GameMath.Cos(angle) * (point.Y - origin.Y) + origin.Y
        );
    }



    /// <summary>
    /// Берем направление, на которое указывает вектор от центра грани к точке попадания.
    /// </summary>
    /// <param name="mapping"></param>
    /// <returns></returns>
    private BlockFacing DirectionHelper(params BlockFacing[] mapping)
    {
        var hitPosition = Rotate(this.Position2D, new Vec2d(0.5, 0.5), 45.0 * GameMath.DEG2RAD);

        // Добавляем небольшую погрешность для обработки граничных случаев
        const float epsilon = 0.0001f;

        var right = hitPosition.X >= 0.5f - epsilon;
        var left = hitPosition.X <= 0.5f + epsilon;
        var top = hitPosition.Y >= 0.5f - epsilon;
        var bottom = hitPosition.Y <= 0.5f + epsilon;

        // Обрабатываем все возможные комбинации
        if (right && top && !left && !bottom) // правый верхний угол
        {
            return mapping[0];
        }

        if (left && bottom && !right && !top) // левый нижний угол
        {
            return mapping[1];
        }

        if (left && top && !right && !bottom) // левый верхний угол
        {
            return mapping[2];
        }

        if (right && bottom && !left && !top) // правый нижний угол
        {
            return mapping[3];
        }

        // Обработка граничных случаев
        if (Math.Abs(hitPosition.X - 0.5f) < epsilon)
        {
            // Вертикальная линия через центр
            return hitPosition.Y > 0.5f ? mapping[2] : mapping[3];
        }

        if (Math.Abs(hitPosition.Y - 0.5f) < epsilon)
        {
            // Горизонтальная линия через центр
            return hitPosition.X > 0.5f ? mapping[0] : mapping[1];
        }

        // Если попали точно в центр (оба условия выше не сработали)
        // Выбираем направление по умолчанию или на основе дополнительной логики
        return mapping[0]; // или другое значение по умолчанию
    }
}

