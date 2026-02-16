using System;
using System.Collections.Concurrent;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Глобальный кэш путей для иммерсивных проводов
    /// </summary>
    public static class ImmersivePathCacheManager
    {
        private class Entry
        {
            public BlockPos[]? Path;
            public byte[]? NodeIndices; // Индексы узлов для каждого блока в пути
            public float PathLength;    // Суммарная длина пути (WireLength)
            public DateTime LastAccessed;
            public int Version;
            public int Voltage;
        }

        private static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(global::ElectricalProgressive.ElectricalProgressive.cacheTimeoutCleanupMinutes);

        private static readonly ConcurrentDictionary<ulong, Entry> Cache = new();

        /// <summary>
        /// Быстрый хэш для пары позиций
        /// </summary>
        private static ulong HashPair(BlockPos a, BlockPos b)
        {
            unchecked
            {
                var ha = HashBlockPos(a);
                var hb = HashBlockPos(b);
                return ha ^ (hb * 0x9E3779B97F4A7C15UL);
            }
        }

        private static ulong HashBlockPos(BlockPos pos)
        {
            unchecked
            {
                var dim = (ulong)(uint)pos.dimension & 0xFUL;
                var x = (ulong)(uint)(pos.X + 8388608) & 0xFFFFFFUL;
                var y = (ulong)(uint)(pos.Y + 2048) & 0xFFFUL;
                var z = (ulong)(uint)(pos.Z + 8388608) & 0xFFFFFFUL;

                return (dim << 60) ^ (x << 36) ^ (y << 24) ^ z;
            }
        }

        /// <summary>
        /// Попытаться получить путь из кэша
        /// </summary>
        public static bool TryGet(
            BlockPos start,
            BlockPos end,
            out BlockPos[] path,
            out byte[] nodeIndices,
            out float pathLength,  // Добавляем выходной параметр длины
            out int version,
            out int voltage)
        {
            var key = HashPair(start, end);
            if (Cache.TryGetValue(key, out var entry))
            {
                entry.LastAccessed = DateTime.UtcNow;
                path = entry.Path!;
                nodeIndices = entry.NodeIndices!;
                pathLength = entry.PathLength;  // Возвращаем длину
                version = entry.Version;
                voltage = entry.Voltage;
                return true;
            }

            path = null!;
            nodeIndices = null!;
            pathLength = 0f;  // Инициализируем
            version = 0;
            voltage = 0;
            return false;
        }

        /// <summary>
        /// Сохранить в кэше новый путь или обновить существующий
        /// </summary>
        public static void AddOrUpdate(
            BlockPos start,
            BlockPos end,
            int currentVersion,
            BlockPos[] path,
            byte[] nodeIndices,
            float pathLength,  // Добавляем параметр длины
            int voltage)
        {
            var key = HashPair(start, end);

            Cache.AddOrUpdate(key,
                _ => new Entry
                {
                    Path = path,
                    NodeIndices = nodeIndices,
                    PathLength = pathLength,  // Сохраняем длину
                    LastAccessed = DateTime.UtcNow,
                    Version = currentVersion,
                    Voltage = voltage
                },
                (_, existing) =>
                {
                    existing.Path = path;
                    existing.NodeIndices = nodeIndices;
                    existing.PathLength = pathLength;  // Обновляем длину
                    existing.LastAccessed = DateTime.UtcNow;
                    existing.Version = currentVersion;
                    existing.Voltage = voltage;
                    return existing;
                });
        }



        /// <summary>
        /// Очистка старых записей, не использовавшихся дольше TTL
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
        /// Удалить все записи для указанных позиций
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
            Cache.Clear();
        }
    }
}