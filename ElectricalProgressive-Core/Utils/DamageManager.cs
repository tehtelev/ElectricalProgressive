using System;
using System.Collections.Generic;
using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Utils
{
    public class DamageManager
    {
        //урон в зависимости от напряжения в проводе
        public static readonly Dictionary<int, float> DamageAmount = new()
        {
            { 32,  0.1f },
            { 128,  0.5f }
        };

        //сила отталкивания
        private const double KnockbackStrength = 0.4;

        // Интервал в миллисекундах (2 секунды)
        private const long DamageIntervalMs = 2000;

        // Ключ для хранения времени удара
        private const string Key = "damageByElectricity";

        public static global::ElectricalProgressive.ElectricalProgressive? SystemEP;

        private readonly ICoreServerAPI _sapi;


        /// <summary>
        /// Конструктор класса
        /// </summary>
        /// <param name="api"></param>
        public DamageManager(ICoreServerAPI api)
        {
            this._sapi = api;

            // Получаем ссылку на систему ElectricalProgressive, если она есть
            SystemEP = _sapi!.ModLoader.GetModSystem<global::ElectricalProgressive.ElectricalProgressive>();
        }




        // Время жизни кэша в миллисекундах (например, 1 с)
        private static readonly long _cacheTtlMs = 1000;
        // Интервал между автосбросами кэша в миллисекундах (например, 1 минута)
        private static readonly long _cleanupIntervalMs = 60 * 1000;

        // Структура для хранения данных + время записи
        private class CacheEntry
        {
            public int HeightRain;
            public float Precip;
            public long Timestamp;
        }

        private static readonly Dictionary<(int X, int Z), CacheEntry> Cache = [];

        // Последний момент, когда мы чистили кэш
        private static long _lastCleanup = Environment.TickCount;

        // Генерация ключа из позиции
        private static (int X, int Z) MakeKey(BlockPos pos) => (pos.X, pos.Z);

        // Удаляем устаревшие записи
        private static void CleanupIfNeeded()
        {
            long now = Environment.TickCount;
            if (now - _lastCleanup < _cleanupIntervalMs)
                return;


            _lastCleanup = now;

            var toRemove = new List<(int, int)>();
            foreach (var kv in Cache)
            {
                if (now - kv.Value.Timestamp > _cacheTtlMs)
                {
                    toRemove.Add(kv.Key);
                }
            }
            foreach (var key in toRemove)
            {
                Cache.Remove(key);
            }
        }

        /// <summary>
        /// Возвращает или обновляет кэшированные данные по дождю.
        /// При sapi != null — берёт свежие данные и кладёт в кэш.
        /// При sapi == null — пытается вернуть последнее сохранённое.
        /// </summary>
        public static (int heightRain, float precip) GetWeatherData(
            ICoreServerAPI sapi, Vec3d tmpPos, BlockPos pos)
        {
            // Чистим кэш периодически
            CleanupIfNeeded();

            var key = MakeKey(pos);
            long now = Environment.TickCount;

            if (sapi != null)
            {
                // Получаем актуальные данные
                var heightRain = sapi.World.BlockAccessor.GetRainMapHeightAt(pos.X, pos.Z);
                var precip = ElectricalProgressive.WeatherSystemServer!.GetPrecipitation(tmpPos);

                Cache[key] = new CacheEntry
                {
                    HeightRain = heightRain,
                    Precip = precip,
                    Timestamp = now
                };

                return (heightRain, precip);
            }
            else
            {
                // sapi == null — возвращаем из кэша, если есть
                if (Cache.TryGetValue(key, out var entry))
                {
                    return (entry.HeightRain, entry.Precip);
                }
                // Если нет в кэше — считаем, что осадков нет и дождь не доходит
                return (int.MaxValue, 0f);
            }
        }



        /// <summary>
        /// Функция для обработки сгорания
        /// </summary>
        /// <param name="i"></param>
        /// <param name="part"></param>
        static void Burnout(int i, ref NetworkPart part)
        {
            part.eparams[i].prepareForBurnout(3);
        }




        /// <summary>
        /// Наносим  урон сущности
        /// </summary>
        /// <param name="world"></param>
        /// <param name="entity"></param>
        /// <param name="pos"></param>
        /// <param name="facing"></param>
        /// <param name="AllEparams"></param>
        /// <param name="block"></param>
        public void DamageEntity(IWorldAccessor world, Entity entity, BlockPos pos, BlockFacing facing, EParams[] AllEparams, Block block, float specifiedDamage=0.0f)
        {
            if (SystemEP == null) // Если система ElectricalProgressive не инициализирована, ничего не делаем
                return;

            var doDamage = false;
            var voltage = 0;
            NetworkInformation networkInformation;

            for (var i = 0; i <= 5; i++) //перебор всех граней
            {
                networkInformation = SystemEP.GetNetworks(pos, FacingHelper.FromFace(FacingHelper.BlockFacingFromIndex(i)));      //получаем информацию о сети

                if (networkInformation?.NumberOfProducers > 0 || networkInformation?.NumberOfAccumulators > 0) //если в сети есть генераторы или аккумы
                {
                    if (AllEparams != null) //энтити существует?
                    {
                        var par = AllEparams[i];
                        if (!par.burnout)           //не сгорел?
                        {
                            if (!par.isolated)      //не изолированный?
                            {
                                doDamage = true;   //значит урон разрешаем
                                if (par.voltage > voltage)  //запишем самый большой вольтаж
                                    voltage = par.voltage;
                            }
                        }
                        else                        //сгорел
                        {
                            doDamage = true;   //значит урон разрешаем
                            if (par.voltage > voltage)  //запишем самый большой вольтаж
                                voltage = par.voltage;
                        }
                    }
                }
            }



            if (!doDamage)
                return;

            // Текущее время в миллисекундах с запуска сервера
            var now = world.ElapsedMilliseconds;
            var last = entity.Attributes.GetDouble(Key);

            if (last > now)
                last = 0;

            // Если прошло >= 2 секунд, наносим урон и сбрасываем таймер
            if (now - last >= DamageIntervalMs)
            {
                // 1) Наносим урон
                var dmg = new DamageSource()
                {
                    Source = EnumDamageSource.Block,
                    SourceBlock = block,
                    Type = EnumDamageType.Electricity,
                    SourcePos = pos.ToVec3d()
                };
                entity.ReceiveDamage(dmg, ((specifiedDamage>0.0f)? specifiedDamage: DamageAmount[voltage]));

                // 2) Вычисляем вектор от блока к сущности и отталкиваем
                var center = pos.ToVec3d().Add(0.5, 0.5, 0.5);
                var diff = entity.Pos.XYZ - center;
                diff.Y = 0.2; // небольшой подъём
                diff.Normalize();

                entity.Attributes.SetDouble("kbdirX", diff.X * KnockbackStrength);
                entity.Attributes.SetDouble("kbdirY", diff.Y * KnockbackStrength);
                entity.Attributes.SetDouble("kbdirZ", diff.Z * KnockbackStrength);

                // 3) Запоминаем время удара
                entity.Attributes.SetDouble(Key, now);

                //рисуем искры
                ParticleManager.SpawnElectricSparks(entity.World, entity.Pos.XYZ);

                //воспроизводим звук
                world.PlaySoundAt(
                    ElectricalProgressive.soundElectricShok,
                    pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5,
                    null,
                    false,
                    32,
                    0.6f
                );
            }
        }





        /// <summary>
        /// Наносим урон приборам от жидкостей
        /// </summary>
        /// <param name="sapi"></param>
        /// <param name="part"></param>
        /// <param name="blockAccessor"></param>
        /// <returns></returns>
        public bool DamageByEnvironment(ICoreServerAPI sapi, ref NetworkPart part, ref IBlockAccessor blockAccessor)
        {
            //без api тут точно нечего делать
            if (sapi == null || SystemEP==null)
                return false;



            var tmpPos = new Vec3d();
            var here = blockAccessor.GetBlock(part.Position);
            var Y = 0.0f;
            var updated = false;

            // Проверка мультиблока
            // Проверка мультиблока
            var multiblockBehavior = here.GetBehavior<BlockBehaviorMultiblock>();
            if (multiblockBehavior != null)
            {
                var properties = multiblockBehavior.propertiesAtString;
                if (!string.IsNullOrEmpty(properties))
                {
                    try
                    {
                        // Используем System.Text.Json для парсинга
                        // Используем Utf8JsonReader для минимальных аллокаций
                        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(properties);
                        var reader = new Utf8JsonReader(jsonBytes);

                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonTokenType.PropertyName)
                            {
                                if (reader.ValueTextEquals("sizey"))
                                {
                                    if (reader.Read() && reader.TokenType == JsonTokenType.Number)
                                    {
                                        Y = reader.GetSingle() - 1;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Логирование ошибки, если требуется
                    }
                }
            }

            // Позиция для проверки дождя
            tmpPos.Set(part.Position.X + 0.5, part.Position.Y + Y, part.Position.Z + 0.5);

            // Проверка дождя
            // Получаем кэшированные или свежие данные дождя
            var (heightRain, precip) = GetWeatherData(
                sapi, tmpPos, part.Position
            );

            // с некоторым шансом даже если дождь попадает
            // но рано или поздно спалит
            // heightRain=0 когда чанк не прогружен, либо sapi=null не трогать условие, иначе спалит ВСЁ
            var isRaining = heightRain > 0
                            && heightRain <= part.Position.Y + Y
                            && precip > 0.1f;





            var pos = part.Position; // Позиция блока, к которому относится часть сети



            // проверяем находится ли кабель под нагрузкой
            var powered = new bool[6]; //массив для хранения устройств, которые под напряжением

            NetworkInformation networkInformation;
            for (var i = 0; i <= 5; i++) //перебор всех граней
            {
                networkInformation = SystemEP.GetNetworks(pos, FacingHelper.FromFace(FacingHelper.BlockFacingFromIndex(i)));      //получаем информацию о сети

                if (networkInformation?.Production > 0f || networkInformation?.NumberOfAccumulators > 0) //если в сети активная генерация или есть аккумы
                {
                    powered[i] = true; //значит под напряжением
                }
            }

            

            // Проверка осадков для всех граней
            if (isRaining)
            {
                for (var i = 0; i < 6; i++)
                {
                    if (!part.eparams[i].burnout && !part.eparams[i].isolatedEnvironment && powered[i])
                    {
                        Burnout(i, ref part);
                        updated = true;
                    }
                }
            }

            // Проверка жидкости в текущем блоке
            var fluidHere = blockAccessor.GetBlock(pos, BlockLayersAccess.Fluid);
            if (fluidHere.IsLiquid())
            {
                if (fluidHere.Code.Path.Contains("lava"))
                {
                    for (var i = 0; i < 6; i++)
                    {
                        Burnout(i, ref part);
                    }
                    updated = true;

                    if (sapi != null)
                        sapi.World.BlockAccessor.BreakBlock(pos, null);
                    return updated;
                }
                else if (fluidHere.Code.Path.Contains("water"))
                {
                    for (var i = 0; i < 6; i++)
                    {
                        if (!part.eparams[i].burnout && !part.eparams[i].isolatedEnvironment && powered[i])
                        {
                            Burnout(i, ref part);
                            updated = true;
                        }
                    }
                }
            }

            // Проверка соседних блоков
            foreach (var face in BlockFacing.ALLFACES)
            {
                var neighborPos = pos.AddCopy(face);
                var fluid = blockAccessor.GetBlock(neighborPos, BlockLayersAccess.Fluid);
                if (fluid.IsLiquid())
                {
                    if (fluid.Code.Path.Contains("lava"))
                    {
                        for (var i = 0; i < 6; i++)
                        {
                            Burnout(i, ref part);
                        }
                        updated = true;
                        if (sapi != null)
                            sapi.World.BlockAccessor.BreakBlock(pos, null);
                        return updated;
                    }
                    else if (fluid.Code.Path.Contains("water"))
                    {
                        for (var i = 0; i < 6; i++)
                        {
                            if (!part.eparams[i].burnout && !part.eparams[i].isolatedEnvironment && powered[i])
                            {
                                Burnout(i, ref part);
                                updated = true;
                            }
                        }
                    }
                }
            }

            /* для отладки
            if (updated)
            {
                isRaining = isRaining;
            }
            */

            return updated;
        }
    }
}
