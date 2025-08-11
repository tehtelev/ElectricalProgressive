using System;
using System.Collections.Concurrent;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    
    /// <summary>
    /// Глобальный, не зависящий от сети кэш путей с удалением по TTL.
    /// </summary>
    public static class PathCacheManager
    {
        private class Entry
        {
            public BlockPos[]? Path;
            public int[]? FacingFrom;
            public bool[][]? NowProcessedFaces;
            public Facing[]? UsedConnections;
            public DateTime LastAccessed;
            public int version;
        }

        // TTL, по истечении которого неиспользуемые записи удаляются
        private static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(ElectricalProgressive.cacheTimeoutCleanupMinutes);

        // Сам кэш, ключом служит только (start, end, version)
        private static readonly ConcurrentDictionary<(BlockPos, BlockPos), Entry> cache = new();

        /// <summary>
        /// Попытаться получить путь из кэша.
        /// </summary>
        public static bool TryGet(
            BlockPos start, BlockPos end,
            out BlockPos[] path,
            out int[] facingFrom,
            out bool[][] nowProcessed,
            out Facing[] usedConnections,
            out int version)
        {
            var key = (start, end);
            if (cache.TryGetValue(key, out var entry))
            {
                entry.LastAccessed = DateTime.UtcNow;
                path = entry.Path!;
                facingFrom = entry.FacingFrom!;
                nowProcessed = entry.NowProcessedFaces!;
                usedConnections = entry.UsedConnections!;
                version = entry.version;
                return true;
            }

            path = null!;
            facingFrom = null!;
            nowProcessed = null!;
            usedConnections = null!;
            version = 0;
            return false;
        }


        
        /// <summary>
        /// Сохранить в кэше новый вычисленный путь (или обновить существующий).
        /// </summary>
        public static void AddOrUpdate(
            BlockPos start, BlockPos end, int currentVersion,
            BlockPos[] path,
            int[] facingFrom,
            bool[][] nowProcessedFaces,
            Facing[] usedConnections)
        {
            var key = (start, end);
            // При обновлении существующей записи не сбрасываем LastAccessed, чтобы не мешать очистке
            cache.AddOrUpdate(key,
                k => new Entry
                {
                    Path = path,
                    FacingFrom = facingFrom,
                    NowProcessedFaces = nowProcessedFaces,
                    UsedConnections = usedConnections,
                    LastAccessed = DateTime.UtcNow,
                    version = currentVersion
                },
                (k, existing) =>
                {
                    existing.Path = path;
                    existing.FacingFrom = facingFrom;
                    existing.NowProcessedFaces = nowProcessedFaces;
                    existing.UsedConnections = usedConnections;
                    existing.version = currentVersion;
                    // сохраняем existing.LastAccessed без изменения
                    return existing;
                });
        }



        /// <summary>
        /// Удалить все записи, к которым не обращались в течение TTL.
        /// Вызывать периодически (например, раз в секунду или каждые N тиков).
        /// </summary>
        public static void Cleanup()
        {
            var cutoff = DateTime.UtcNow - EntryTtl;

            // Перебираем snapshot словаря и сразу удаляем устаревшие элементы
            foreach (var pair in cache)
            {
                if (pair.Value.LastAccessed < cutoff)
                {
                    cache.TryRemove(pair.Key, out _);
                }
            }
        }


        /// <summary>
        /// Принудительно удаляет из кэша все записи для указанных координат start и end
        /// независимо от version.
        /// </summary>
        public static void RemoveAll(BlockPos start, BlockPos end)
        {
            var key = (start, end);
            cache.TryRemove(key, out _);
        }
    }
    
}
