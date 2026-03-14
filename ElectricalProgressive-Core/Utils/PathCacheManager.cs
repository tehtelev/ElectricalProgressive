using System;
using System.Collections.Concurrent;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Глобальный кэш путей с быстрым ulong-ключом и очисткой по TTL.
    /// </summary>
    public static class PathCacheManager
    {
        private class Entry
        {
            public BlockPos[]? Path;
            public byte[]? FacingFrom;
            public bool[][]? NowProcessedFaces;
            public Facing[]? UsedConnections;
            public DateTime LastAccessed;
            public int Version;
            public int Voltage;
        }

        private static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(ElectricalProgressive.cacheTimeoutCleanupMinutes);

        // Заменили (BlockPos, BlockPos) на ulong
        private static readonly ConcurrentDictionary<ulong, Entry> Cache = new();

        /// <summary>
        /// Быстрый хэш для пары позиций.
        /// Учитывает X, Y, Z и Dimension для обеих точек.
        /// </summary>
        private static ulong HashPair(BlockPos a, BlockPos b)
        {
            unchecked
            {
                var ha = HashBlockPos(a);
                var hb = HashBlockPos(b);
                return ha ^ (hb * 0x9E3779B97F4A7C15UL); // перемешивание золотым сечением
            }
        }

        private static ulong HashBlockPos(BlockPos pos)
        {
            unchecked
            {
                // Сдвиги для устранения отрицательных значений
                var dim = (ulong)(uint)pos.dimension & 0xFUL;          // 4 бита
                var x = (ulong)(uint)(pos.X + 8388608) & 0xFFFFFFUL;   // 24 бита
                var y = (ulong)(uint)(pos.Y + 2048) & 0xFFFUL;         // 12 бит
                var z = (ulong)(uint)(pos.Z + 8388608) & 0xFFFFFFUL;   // 24 бита

                // Формируем 64-битный ключ: [DIM(4)][X(24)][Y(12)][Z(24)]
                return (dim << 60) ^ (x << 36) ^ (y << 24) ^ z;
            }
        }




        /// <summary>
        /// Попытаться получить путь из кэша.
        /// </summary>
        public static bool TryGet(
            BlockPos start,
            BlockPos end,
            out BlockPos[] path,
            out byte[] facingFrom,
            out bool[][] nowProcessed,
            out Facing[] usedConnections,
            out int version,
            out int voltage)
        {
            var key = HashPair(start, end);
            if (Cache.TryGetValue(key, out var entry))
            {
                entry.LastAccessed = DateTime.UtcNow;
                path = entry.Path!;
                facingFrom = entry.FacingFrom!;
                nowProcessed = entry.NowProcessedFaces!;
                usedConnections = entry.UsedConnections!;
                version = entry.Version;
                voltage= entry.Voltage;
                return true;
            }

            path = null!;
            facingFrom = null!;
            nowProcessed = null!;
            usedConnections = null!;
            version = 0;
            voltage= 0;
            return false;
        }

        /// <summary>
        /// Сохранить в кэше новый путь или обновить существующий.
        /// </summary>
        public static void AddOrUpdate(
            BlockPos start,
            BlockPos end,
            int currentVersion,
            BlockPos[] path,
            byte[] facingFrom,
            bool[][] nowProcessedFaces,
            Facing[] usedConnections,
            int voltage)
        {
            var key = HashPair(start, end);

            Cache.AddOrUpdate(key,
                _ => new Entry
                {
                    Path = path,
                    FacingFrom = facingFrom,
                    NowProcessedFaces = nowProcessedFaces,
                    UsedConnections = usedConnections,
                    LastAccessed = DateTime.UtcNow,
                    Version = currentVersion,
                    Voltage = voltage
                },
                (_, existing) =>
                {
                    existing.Path = path;
                    existing.FacingFrom = facingFrom;
                    existing.NowProcessedFaces = nowProcessedFaces;
                    existing.UsedConnections = usedConnections;
                    existing.Version = currentVersion;
                    existing.Voltage = voltage;
                    return existing;
                });
        }

        /// <summary>
        /// Очистка старых записей, не использовавшихся дольше TTL.
        /// </summary>
        public static void Cleanup()
        {
            var cutoff = DateTime.UtcNow - EntryTtl;
            foreach (var pair in Cache)
            {
                if (pair.Value.LastAccessed < cutoff)
                {
                    Cache.TryRemove(pair.Key, out _);
                }
            }
        }
        
        /// <summary>
        /// Удалить все записи для указанных позиций.
        /// </summary>
        public static void RemoveAll(BlockPos start, BlockPos end)
        {
            var key = HashPair(start, end);
            Cache.TryRemove(key, out _);
        }

        /// <summary>
        /// Удалить все записи 
        /// </summary>
        public static void Dispose()
        {
            Cache?.Clear();
        }

    }
}
