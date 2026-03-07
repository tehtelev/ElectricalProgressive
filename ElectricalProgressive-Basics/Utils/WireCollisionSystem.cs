using ElectricalProgressive.Content.Block;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Utils
{
    /// <summary>
    /// Система проверки коллизий проводов с блоками и сущностями
    /// </summary>
    public static class WireCollisionSystem
    {
        // Размер коллизионного бокса для условной точки провода
        private const float COLLISION_BOX_SIZE = 0.05f;
        private const double IGNITION_REQUIRED_TIME = 10000; // мс
        private static EntityAgent dummyAgent;

        // Класс для хранения прогресса поджигания
        private class IgnitionProgress
        {
            public long LastContactTime;
            public double AccumulatedTime;
            public double RequiredTime;
        }

        // Ключ для идентификации контакта провода с блоком
        private class WireContactKey
        {
            public BlockPos WirePos;
            public byte LocalNodeIndex;
            public BlockPos NeighborPos;
            public BlockPos CollidingBlockPos;

            public override bool Equals(object obj)
            {
                return obj is WireContactKey other &&
                       EqualityComparer<BlockPos>.Default.Equals(WirePos, other.WirePos) &&
                       LocalNodeIndex == other.LocalNodeIndex &&
                       EqualityComparer<BlockPos>.Default.Equals(NeighborPos, other.NeighborPos) &&
                       EqualityComparer<BlockPos>.Default.Equals(CollidingBlockPos, other.CollidingBlockPos);
            }

            public override int GetHashCode()
            {
                int hash = 17;
                hash = hash * 23 + WirePos.GetHashCode();
                hash = hash * 23 + LocalNodeIndex.GetHashCode();
                hash = hash * 23 + NeighborPos.GetHashCode();
                hash = hash * 23 + CollidingBlockPos.GetHashCode();
                return hash;
            }
        }

        private class WireCollisionData
        {
            public BlockPos LocalPos { get; set; }
            public byte LocalNodeIndex { get; set; }
            public BlockPos NeighborPos { get; set; }
            public byte NeighborNodeIndex { get; set; }
            public Vec3d CollisionPoint { get; set; } // точка, вызвавшая разрыв
            public ConnectionData Connection { get; set; }
        }

        private static Dictionary<WireContactKey, IgnitionProgress> ignitionProgresses = new Dictionary<WireContactKey, IgnitionProgress>();



        /// <summary>
        /// Проверяет коллизии всех проводов в сети и разрывает соединения при необходимости
        /// Также наносит урон живым сущностям от неизолированных проводов под напряжением
        /// Должен вызываться в OnGameTickServer на этапе 13
        /// </summary>
        public static void CheckWireCollisions(
            ICoreServerAPI sapi,
            Dictionary<BlockPos, ImmersiveNetworkPart> parts,
            ElectricalProgressiveImmersive modSystem)
        {
            var blockAccessor = sapi.World.BlockAccessor;
            var connectionsToBreak = new List<WireCollisionData>();
            var activeKeys = new HashSet<WireContactKey>(); // контакты, активные в этом тике

            // Получаем блок огня (для последующей установки)
            Block blockFire = sapi.World.GetBlock(new AssetLocation("fire"));

            // Проходим по всем частям сети
            foreach (var partEntry in parts)
            {
                var partPos = partEntry.Key;
                var part = partEntry.Value;

                if (!part.IsLoaded)
                    continue;

                var blockEntity = blockAccessor.GetBlockEntity(partPos);
                var behavior = blockEntity?.GetBehavior<BEBehaviorEPImmersive>();

                if (behavior == null)
                    continue;

                // Проверяем каждое соединение
                foreach (var connection in part.Connections)
                {
                    // Получаем точки провода для коллизии
                    var collisionPoints = CalculateWireCollisionPoints(behavior, connection, partPos);
                    if (collisionPoints == null || collisionPoints.Count == 0)
                        continue;

                    bool shouldBreak = false;      // флаг разрыва провода
                    Vec3d breakPoint = null;       // точка, где произошёл разрыв (для выброса предмета)

                    foreach (var point in collisionPoints)
                    {
                        // Проверяем коллизию с блоками
                        if (!CheckPointCollisionWithBlock(blockAccessor, point, partPos, connection.NeighborPos))
                            continue;

                        // Определяем позицию блока, с которым столкнулись
                        var collidingBlockPos = new BlockPos(
                            (int)Math.Floor(point.X),
                            (int)Math.Floor(point.Y),
                            (int)Math.Floor(point.Z),
                            partPos.dimension
                        );

                        // Коррекция для мультиблоков
                        var block = blockAccessor.GetBlock(collidingBlockPos);
                        if (block is BlockMultiblock)
                        {
                            var realPos = GetRealPosition(blockAccessor, collidingBlockPos);
                            block = blockAccessor.GetBlock(realPos);
                            collidingBlockPos = realPos;
                        }

                        // Получаем горючие свойства блока
                        var combustible = block.GetCombustibleProperties(sapi.World, null, collidingBlockPos);
                        // если блок может гореть и провод не изолирован
                        if (combustible != null && !connection.Parameters.isolated)
                        {
                            // Блок горючий – обновляем прогресс поджигания
                            var key = new WireContactKey
                            {
                                WirePos = partPos,
                                LocalNodeIndex = connection.LocalNodeIndex,
                                NeighborPos = connection.NeighborPos,
                                CollidingBlockPos = collidingBlockPos
                            };
                            activeKeys.Add(key);

                            long now = sapi.World.ElapsedMilliseconds;
                            if (!ignitionProgresses.TryGetValue(key, out var progress))
                            {
                                progress = new IgnitionProgress
                                {
                                    LastContactTime = now,
                                    AccumulatedTime = 0,
                                    RequiredTime = IGNITION_REQUIRED_TIME
                                };
                                ignitionProgresses[key] = progress;
                            }
                            else
                            {
                                long delta = now - progress.LastContactTime;
                                // Защита от больших скачков времени

                                progress.AccumulatedTime += delta;

                            }

                            // Достигнуто ли необходимое время контакта?
                            if (progress.AccumulatedTime >= progress.RequiredTime)
                            {
                                // Пытаемся поджечь блок (ищем позицию для огня)
                                BlockPos firePos = FindFirePosition(sapi.World, collidingBlockPos, blockFire);
                                if (firePos != null)
                                {
                                    // Ставим огонь
                                    blockAccessor.SetBlock(blockFire.BlockId, firePos);
                                    var befire = blockAccessor.GetBlockEntity(firePos);
                                    befire?.GetBehavior<BEBehaviorBurning>()?.OnFirePlaced(GetFacingTowardsBlock(collidingBlockPos, firePos), null);

                                    // Визуальные эффекты
                                    ParticleManager.SpawnElectricSparks(sapi.World, firePos.ToVec3d().Add(0.5, 0.5, 0.5));
                                    sapi.World.PlaySoundAt(new AssetLocation("sounds/torch-ignite"), firePos.X, firePos.Y, firePos.Z, null, false, 16);

                                    // Разрываем провод после возгорания
                                    shouldBreak = true;
                                    breakPoint = point;
                                    break;
                                }
                                // Если не удалось найти место для огня – не разрываем, продолжаем копить время? 
                                // По логике, если нет места, то и зажечь не получится, поэтому, возможно, не разрываем,
                                // но и не сбрасываем прогресс – ждём, когда появится возможность.
                                // Можно оставить как есть: прогресс остаётся, но провод не рвётся.
                            }
                        }
                        else
                        {
                            // Блок негорючий → разрываем провод
                            shouldBreak = true;
                            breakPoint = point;
                            break;
                        }
                    }

                    if (shouldBreak)
                    {
                        // Если соединение уже есть в списке на разрыв – пропускаем
                        if (connectionsToBreak.Any(wd => wd.NeighborPos == partPos))
                            continue;

                        connectionsToBreak.Add(new WireCollisionData
                        {
                            LocalPos = partPos,
                            LocalNodeIndex = connection.LocalNodeIndex,
                            NeighborPos = connection.NeighborPos,
                            NeighborNodeIndex = connection.NeighborNodeIndex,
                            CollisionPoint = breakPoint,
                            Connection = connection
                        });
                    }

                    // Проверяем коллизии с сущностями (урон от провода) – без изменений
                    CheckWireEntityCollisions(sapi, connection, collisionPoints, partPos, modSystem);
                }
            }

            // Удаляем прогрессы для контактов, которые больше не активны
            foreach (var key in ignitionProgresses.Keys.ToList())
            {
                if (!activeKeys.Contains(key))
                    ignitionProgresses.Remove(key);
            }

            // Разрываем все накопленные соединения
            foreach (var wireData in connectionsToBreak)
            {
                BreakWireConnection(sapi, wireData);
            }
        }

        private static BlockPos FindFirePosition(IWorldAccessor world, BlockPos pos, Block blockFire)
        {
            // Сначала проверим сам блок, если он может быть заменён огнём (например, трава, куст)
            var block = world.BlockAccessor.GetBlock(pos);
            if (block.Replaceable > 6000) // большинство заменяемых блоков
                return pos;

            // Иначе проверим соседей
            foreach (var face in BlockFacing.ALLFACES)
            {
                BlockPos npos = pos.AddCopy(face);
                Block nblock = world.BlockAccessor.GetBlock(npos);
                if (nblock.IsReplacableBy(blockFire))
                    return npos;
            }
            return null;
        }

        private static BlockFacing GetFacingTowardsBlock(BlockPos from, BlockPos to)
        {
            foreach (var face in BlockFacing.ALLFACES)
            {
                if (from.AddCopy(face).Equals(to))
                    return face;
            }
            return null;
        }

        /// <summary>
        /// Проверяет коллизии провода с живыми сущностями и наносит урон
        /// </summary>
        private static void CheckWireEntityCollisions(
            ICoreServerAPI sapi,
            ConnectionData connection,
            List<Vec3d> collisionPoints,
            BlockPos wirePos,
            ElectricalProgressiveImmersive modSystem)
        {
            // Если провод изолирован или сгорел, урон не наносим
            if (connection.Parameters.isolated || connection.Parameters.burnout)
                return;

            // Если напряжение 0, урон не наносим
            if (connection.Parameters.voltage == 0)
                return;

            // Проверяем, есть ли в сети генераторы или аккумуляторы (провод под напряжением)
            var networkInfo = modSystem.GetNetworkForImmersiveWire(wirePos);
            if (networkInfo == null || (networkInfo.NumberOfProducers == 0 && networkInfo.NumberOfAccumulators == 0))
                return;

            // Получаем все сущности в области провода
            var minPoint = collisionPoints[0].Clone();
            var maxPoint = collisionPoints[0].Clone();

            foreach (var point in collisionPoints)
            {
                minPoint.X = Math.Min(minPoint.X, point.X);
                minPoint.Y = Math.Min(minPoint.Y, point.Y);
                minPoint.Z = Math.Min(minPoint.Z, point.Z);

                maxPoint.X = Math.Max(maxPoint.X, point.X);
                maxPoint.Y = Math.Max(maxPoint.Y, point.Y);
                maxPoint.Z = Math.Max(maxPoint.Z, point.Z);
            }

            // Расширяем область поиска на размер коллизионного бокса
            var searchBox = new Cuboidd(
                minPoint.X - COLLISION_BOX_SIZE,
                minPoint.Y - COLLISION_BOX_SIZE,
                minPoint.Z - COLLISION_BOX_SIZE,
                maxPoint.X + COLLISION_BOX_SIZE,
                maxPoint.Y + COLLISION_BOX_SIZE,
                maxPoint.Z + COLLISION_BOX_SIZE
            );

            // Получаем все сущности в области
            var entities = sapi.World.GetEntitiesAround(
                new Vec3d(
                    (minPoint.X + maxPoint.X) / 2,
                    (minPoint.Y + maxPoint.Y) / 2,
                    (minPoint.Z + maxPoint.Z) / 2
                ),
                (float)Math.Max(
                    Math.Max(maxPoint.X - minPoint.X, maxPoint.Y - minPoint.Y),
                    maxPoint.Z - minPoint.Z
                ) + 1f,
                (float)Math.Max(
                    Math.Max(maxPoint.X - minPoint.X, maxPoint.Y - minPoint.Y),
                    maxPoint.Z - minPoint.Z
                ) + 1f
            );

            // Проверяем каждую сущность
            foreach (var entity in entities)
            {
                // Проверяем только живые создания
                if (!entity.Alive || !entity.IsCreature)
                    continue;

                // если игрок держит в руках набор электрика, то не надо его бить током
                if (entity is EntityPlayer player)
                {
                    if ((player.LeftHandItemSlot?.Itemstack?.Item?.Code.Path.Contains("electricianskit") ?? false) ||
                        (player.RightHandItemSlot?.Itemstack?.Item?.Code.Path.Contains("electricianskit") ?? false))
                        continue;
                }

                // Получаем коллизионный бокс сущности
                var entityBox = entity.CollisionBox.ToDouble();
                entityBox.Translate(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);

                // Проверяем пересечение с точками провода
                foreach (var point in collisionPoints)
                {
                    var wireBox = new Cuboidd(
                        point.X - COLLISION_BOX_SIZE / 2,
                        point.Y - COLLISION_BOX_SIZE / 2,
                        point.Z - COLLISION_BOX_SIZE / 2,
                        point.X + COLLISION_BOX_SIZE / 2,
                        point.Y + COLLISION_BOX_SIZE / 2,
                        point.Z + COLLISION_BOX_SIZE / 2
                    );

                    if (entityBox.Intersects(wireBox))
                    {
                        // Наносим урон используя существующий DamageManager
                        DamageEntityFromWire(sapi, entity, wirePos, connection.Parameters, collisionPoints);
                        break; // Достаточно одного пересечения для урона
                    }
                }
            }
        }

        /// <summary>
        /// Наносит урон сущности от иммерсивного провода
        /// </summary>
        private static void DamageEntityFromWire(
            ICoreServerAPI sapi,
            Entity entity,
            BlockPos wirePos,
            EParams wireParams,
            List<Vec3d> collisionPoints = null)
        {
            // Проверяем интервал урона (чтобы не спамить)
            const string damageKey = "damageByElectricity";
            const long damageIntervalMs = 2000; // 2 секунды
            var now = sapi.World.ElapsedMilliseconds;
            var lastDamage = entity.Attributes.GetDouble(damageKey);
            if (lastDamage > now)
                lastDamage = 0;
            // Если прошло меньше 2 секунд, не наносим урон
            if (now - lastDamage < damageIntervalMs)
                return;
            // Рассчитываем урон на основе напряжения
            float damage = wireParams.voltage switch
            {
                512 => 1.0f,
                _ => 1.0f
            };
            // Наносим урон
            var dmgSource = new DamageSource
            {
                Source = EnumDamageSource.Block,
                Type = EnumDamageType.Electricity,
                SourcePos = wirePos.ToVec3d()
            };
            entity.ReceiveDamage(dmgSource, damage);
            // Отталкиваем сущность перпендикулярно проводу
            const double knockbackStrength = 0.3;
            Vec3d direction;
            if (collisionPoints != null && collisionPoints.Count >= 2) // Требуются минимум две точки для направления провода
            {
                var entityPos = entity.Pos.XYZ.Clone();
                // Используем середину сущности по высоте Y вместо позиции ног
                entityPos.Y += entity.SelectionBox.Y2 / 2.0;
                // Сортируем точки по расстоянию до сущности и берём две ближайшие
                var sortedPoints = collisionPoints
                    .OrderBy(point => entityPos.DistanceTo(point))
                    .ToList();
                var closest1 = sortedPoints[0];
                var closest2 = sortedPoints[1];
                // Направление провода
                var wireDir = closest2 - closest1;
                if (wireDir.Length() < 1e-6) // Если точки совпадают, fallback
                {
                    direction = entityPos - closest1;
                }
                else
                {
                    wireDir.Normalize();
                    // Вектор от closest1 к сущности
                    var vecToEntity = entityPos - closest1;
                    // Проекция на направление провода
                    var projectLength = vecToEntity.Dot(wireDir);
                    var project = wireDir * projectLength;
                    // Перпендикулярная компонента
                    var perpendicular = vecToEntity - project;
                    if (perpendicular.LengthSq() < 1e-6) // Если перпендикуляр нулевой, выбираем резервный перпендикуляр (например, в плоскости XZ)
                    {
                        perpendicular = new Vec3d(-wireDir.Z, 0, wireDir.X);
                        if (perpendicular.LengthSq() < 1e-6)
                            perpendicular = new Vec3d(0, 1, 0); // Вертикальный fallback
                    }
                    direction = perpendicular;
                }

                // Корректировка вертикальной компоненты с сохранением знака
                double minY = (Math.Abs(direction.X) < 0.05 && Math.Abs(direction.Z) < 0.05) ? 0.2 : 0.1;
                double absY = Math.Abs(direction.Y);
                if (absY < minY)
                {
                    double signY = Math.Sign(direction.Y);
                    direction.Y = signY * minY;
                    if (absY == 0) // Если Y был нулевым, добавляем положительный подъём
                        direction.Y = minY;
                }
                // Нормализуем
                //direction.Normalize();
            }
            else
            {
                // Фоллбэк: отталкивание от центра блока (если точки не переданы или недостаточно)
                var wireCenter = wirePos.ToVec3d().Add(0.5, 0.5, 0.5);
                var entityPos = entity.Pos.XYZ.Clone();
                entityPos.Y += entity.SelectionBox.Y2 / 2.0; // Также используем середину для фоллбэка
                direction = entityPos - wireCenter;
                direction.Y = Math.Max(direction.Y, 0.2); // Небольшой подъём в фоллбэке
                                                          // direction.Normalize();
            }


            // Применяем отталкивание
            entity.WatchedAttributes.SetDouble("kbdirX", direction.X * knockbackStrength);
            entity.WatchedAttributes.SetDouble("kbdirY", direction.Y * knockbackStrength);
            entity.WatchedAttributes.SetDouble("kbdirZ", direction.Z * knockbackStrength);
            // Запоминаем время урона
            entity.Attributes.SetDouble(damageKey, now);
            // Спавним искры в точке контакта (ближайшая точка провода)
            Vec3d sparkPos;
            if (collisionPoints != null && collisionPoints.Count > 0)
            {
                var entityPos = entity.Pos.XYZ;
                var closestPoint = collisionPoints.OrderBy(point => entityPos.DistanceTo(point)).First();
                sparkPos = closestPoint;
            }
            else
            {
                sparkPos = entity.Pos.XYZ;
            }
            ParticleManager.SpawnElectricSparks(entity.World, sparkPos);
            // Воспроизводим звук
            sapi.World.PlaySoundAt(
                ElectricalProgressive.soundElectricShok,
                entity.Pos.X, entity.Pos.Y, entity.Pos.Z,
                null,
                false,
                32f,
                0.6f
            );
        }

        private static BlockPos GetRealPosition(IBlockAccessor blockAccessor, BlockPos pos)
        {
            Vintagestory.API.Common.Block block = blockAccessor.GetBlock(pos);

            if (block is BlockMultiblock multiblock)
            {
                BlockPos controlPos = multiblock.GetControlBlockPos(pos);

                // Рекурсивно (на случай вложенных мультиблоков)
                return GetRealPosition(blockAccessor, controlPos);
            }

            return pos;
        }

        /// <summary>
        /// Рассчитывает условные точки коллизии для провода
        /// Аналогично CreateWireSegmentMesh, но только координаты центров сегментов
        /// </summary>
        private static List<Vec3d> CalculateWireCollisionPoints(
            BEBehaviorEPImmersive behavior,
            ConnectionData connection,
            BlockPos localPos)
        {
            var collisionPoints = new List<Vec3d>();

            try
            {
                // Получаем локальную ноду
                var localNode = behavior.GetWireNode(connection.LocalNodeIndex);
                if (localNode == null)
                    return collisionPoints;

                // Получаем координаты с учетом поворота блока (как в GetConnectedWires)
                float rotateY = 0;
                var entity = behavior.Blockentity as BlockEntityEIBase;
                if (entity != null && entity.Facing != Facing.None && entity.RotationCache != null)
                {
                    if (entity.RotationCache.TryGetValue(entity.Facing, out var rotation))
                    {
                        rotateY = rotation.Y;
                    }
                }

                var (x, y, z) = RotateCoords(rotateY, localNode.Position.X, localNode.Position.Y, localNode.Position.Z);

                var startPos = new Vec3f((float)x, (float)y, (float)z);

                // Получаем координаты соседней ноды
                rotateY = 0;
                var neighborEntity = behavior.Api.World.BlockAccessor.GetBlockEntity(connection.NeighborPos) as BlockEntityEIBase;
                if (neighborEntity != null && neighborEntity.Facing != Facing.None && neighborEntity.RotationCache != null)
                {
                    if (neighborEntity.RotationCache.TryGetValue(neighborEntity.Facing, out var rotation))
                    {
                        rotateY = rotation.Y;
                    }
                }

                (x, y, z) = RotateCoords(rotateY, connection.NeighborNodeLocalPos.X, connection.NeighborNodeLocalPos.Y, connection.NeighborNodeLocalPos.Z);

                var endPos = new Vec3f(
                    (float)(connection.NeighborPos.X - localPos.X + x),
                    (float)(connection.NeighborPos.Y - localPos.Y + y),
                    (float)(connection.NeighborPos.Z - localPos.Z + z)
                );

                // Центрируем координаты блока (как в CreateWireSegmentMesh)
                startPos = startPos.AddCopy(-0.5f, -0.5f, -0.5f);
                endPos = endPos.AddCopy(-0.5f, -0.5f, -0.5f);

                var dist = startPos.DistanceTo(endPos);
                if (dist < 0.001f)
                    return collisionPoints;

                // Количество сегментов - зависит от длины провода (как в CreateWireSegmentMesh)
                var segments = Math.Max(4, (int)(dist * 4f));
                var segmentCount = segments / 2 + 1;
                var sagFactor = 0.05f; // Стандартный фактор провисания

                // Рассчитываем точки коллизии для каждого сегмента
                for (var i = 0; i < segmentCount; i++)
                {
                    var progress = (float)i / segmentCount;

                    // Позиция центра сегмента с учетом провисания
                    var segmentPos = CalculateSagPosition(startPos, endPos, progress, sagFactor);

                    // Преобразуем в мировые координаты
                    var worldPos = new Vec3d(
                        localPos.X + segmentPos.X + 0.5,
                        localPos.Y + segmentPos.Y + 0.5,
                        localPos.Z + segmentPos.Z + 0.5
                    );

                    collisionPoints.Add(worldPos);
                }
            }
            catch (Exception)
            {
                // В случае ошибки возвращаем пустой список
                return new List<Vec3d>();
            }

            return collisionPoints;
        }

        /// <summary>
        /// Проверяет коллизию точки с блоком
        /// </summary>
        private static bool CheckPointCollisionWithBlock(
            IBlockAccessor blockAccessor,
            Vec3d point,
            BlockPos wireStartPos,
            BlockPos wireEndPos)
        {
            // Создаем маленький коллизионный бокс вокруг точки
            var collisionBox = new Cuboidd(
                point.X - COLLISION_BOX_SIZE / 2,
                point.Y - COLLISION_BOX_SIZE / 2,
                point.Z - COLLISION_BOX_SIZE / 2,
                point.X + COLLISION_BOX_SIZE / 2,
                point.Y + COLLISION_BOX_SIZE / 2,
                point.Z + COLLISION_BOX_SIZE / 2
            );

            // Получаем позицию блока, в котором находится точка
            var blockPos = new BlockPos(
                (int)Math.Floor(point.X),
                (int)Math.Floor(point.Y),
                (int)Math.Floor(point.Z),
                wireStartPos.dimension
            );

            // Игнорируем коллизии с блоками, на которых висит провод
            if (blockPos.Equals(wireStartPos) || blockPos.Equals(wireEndPos))
                return false;

            // Получаем блок
            var block = blockAccessor.GetBlock(blockPos);

            if (block is BlockMultiblock)
            {
                var realPosition = GetRealPosition(blockAccessor, blockPos);

                block = blockAccessor.GetBlock(realPosition);

                // Игнорируем коллизии с блоками, на которых висит провод
                if (realPosition.Equals(wireStartPos) || realPosition.Equals(wireEndPos))
                    return false;
            }

            // Игнорируем воздух и жидкости
            if (block == null || block.Id == 0 || block.IsLiquid())
                return false;

            // Получаем коллизионные боксы блока
            var blockCollisionBoxes = block.GetCollisionBoxes(blockAccessor, blockPos);

            if (blockCollisionBoxes == null || blockCollisionBoxes.Length == 0)
                return false;

            // Проверяем пересечение с каждым коллизионным боксом блока
            foreach (var blockBox in blockCollisionBoxes)
            {
                if (blockBox == null)
                    continue;

                // Преобразуем Cuboidf в Cuboidd и смещаем в мировые координаты
                var worldBlockBox = new Cuboidd(
                    blockPos.X + blockBox.X1,
                    blockPos.Y + blockBox.Y1,
                    blockPos.Z + blockBox.Z1,
                    blockPos.X + blockBox.X2,
                    blockPos.Y + blockBox.Y2,
                    blockPos.Z + blockBox.Z2
                );

                // Проверяем пересечение
                if (collisionBox.Intersects(worldBlockBox))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Разрывает соединение провода при коллизии
        /// Аналогично HandleWireDisconnection
        /// </summary>
        private static void BreakWireConnection(ICoreServerAPI sapi, WireCollisionData wireData)
        {
            try
            {
                var blockAccessor = sapi.World.BlockAccessor;

                // Получаем BlockEntity и Behavior
                var blockEntity = blockAccessor.GetBlockEntity(wireData.LocalPos);
                var behavior = blockEntity?.GetBehavior<BEBehaviorEPImmersive>();

                if (behavior == null)
                    return;

                var neighborEntity = blockAccessor.GetBlockEntity(wireData.NeighborPos);
                var neighborBehavior = neighborEntity?.GetBehavior<BEBehaviorEPImmersive>();

                // Рассчитываем длину провода для выпадения
                var cableLength = (int)Math.Ceiling(wireData.Connection.WireLength);

                // Создаем стак кабеля для выпадения
                ItemStack cableStack = null;

                if (!wireData.Connection.Parameters.burnout)
                {
                    // Провод не сгорел - выпадает нормальный кабель
                    cableStack = ImmersiveWireBlock.CreateCableStack(sapi, wireData.Connection.Parameters);
                }
                else
                {
                    // Провод сгорел - выпадают кусочки металла
                    var assetLoc = new AssetLocation("metalbit-" + wireData.Connection.Parameters.material);
                    var item = sapi.World.GetItem(assetLoc);
                    if (item != null)
                    {
                        cableStack = new ItemStack(item);
                    }
                }

                if (cableStack != null)
                {
                    cableStack.StackSize = cableLength;

                    // Выбрасываем кабель в месте коллизии (первая точка коллизии)
                    var dropPos = wireData.CollisionPoint ?? wireData.LocalPos.ToVec3d();
                    sapi.World.SpawnItemEntity(cableStack, dropPos);
                }

                // Удаляем соединение с обеих сторон
                behavior.RemoveConnection(
                    wireData.LocalNodeIndex,
                    wireData.NeighborPos,
                    wireData.NeighborNodeIndex
                );

                neighborBehavior?.RemoveConnection(
                    wireData.NeighborNodeIndex,
                    wireData.LocalPos,
                    wireData.LocalNodeIndex
                );

                // Очищаем кэш мешей (это вызовется при следующем обновлении на клиенте)
                ImmersiveWireBlock.InvalidateBlockMeshCache(wireData.LocalPos);
                ImmersiveWireBlock.InvalidateBlockMeshCache(wireData.NeighborPos);

                // Помечаем блоки как измененные для синхронизации с клиентами
                blockEntity?.MarkDirty(true);
                neighborEntity?.MarkDirty(true);
            }
            catch (Exception ex)
            {
                sapi.Logger.Error($"[WireCollisionSystem] Error breaking wire connection: {ex.Message}");
            }
        }

        /// <summary>
        /// Вычисляет позицию с учетом провисания
        /// Копия из ImmersiveWireBlock.CalculateSagPosition
        /// </summary>
        private static Vec3f CalculateSagPosition(Vec3f start, Vec3f end, float progress, float sagFactor)
        {
            var linear = start + (end - start) * progress;

            if (sagFactor <= 0.001f)
                return linear;

            // Горизонтальное расстояние
            var hDist = (float)Math.Sqrt((end.X - start.X) * (end.X - start.X) +
                                          (end.Z - start.Z) * (end.Z - start.Z));

            if (hDist < 0.001f)
                return linear;

            // Провисание по катеноиде (вниз)
            var a = hDist / (8f * sagFactor);
            var hProgress = progress * hDist;
            var sagY = a * ((float)Math.Cosh((hProgress - hDist / 2f) / a) -
                             (float)Math.Cosh(hDist / 2f / a));

            return new Vec3f(linear.X, linear.Y + sagY, linear.Z);
        }

        /// <summary>
        /// Поворачивает координаты вокруг оси Y
        /// Копия из ImmersiveWireBlock.RotateCoords
        /// </summary>
        private static (double x, double y, double z) RotateCoords(float rotateY, double x, double y, double z)
        {
            if (Math.Abs(rotateY) < 0.001f)
                return (x, y, z);

            // Центр поворота - центр блока (0.5, 0.5, 0.5)
            var centerX = 0.5;
            var centerZ = 0.5;

            // Смещаем к центру
            var dx = x - centerX;
            var dz = z - centerZ;

            // Поворачиваем
            var cos = Math.Cos(rotateY);
            var sin = Math.Sin(rotateY);

            var newX = dx * cos - dz * sin;
            var newZ = dx * sin + dz * cos;

            // Возвращаем обратно
            return (newX + centerX, y, newZ + centerZ);
        }
    }
}