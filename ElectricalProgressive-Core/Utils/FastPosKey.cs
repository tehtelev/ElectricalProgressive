using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Быстрый ключ для позиции блока с кэшированием хэш-кода
    /// </summary>
    public struct FastPosKey : IEquatable<FastPosKey>
    {
        public int X, Y, Z, Dim;
        public BlockPos Pos;

        public FastPosKey(int x, int y, int z, int dim, BlockPos pos = null)
        {
            X = x;
            Y = y;
            Z = z;
            Dim = dim;
            Pos = pos;
        }

        public bool Equals(FastPosKey other) =>
            X == other.X && Y == other.Y && Z == other.Z && Dim == other.Dim;

        public override bool Equals(object obj) => obj is FastPosKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = X;
                hash = hash << 9 ^ hash >> 23 ^ Y;
                hash = hash << 9 ^ hash >> 23 ^ Z;
                return hash ^ Dim * 269023;
            }
        }
    }
}
