using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Utils
{
    public static class ParticleManager
    {
        // Синхронные методы (оставляем без изменений)
        public static void SpawnElectricSparks(IWorldAccessor world, Vec3d pos)
        {
            SparksTemplate.MinPos = pos;
            world.SpawnParticles(SparksTemplate);
        }
        /// <summary>
        /// Шаблон «электрических искр»
        /// </summary>
        private static readonly SimpleParticleProperties SparksTemplate = new(
            minQuantity: 5, maxQuantity: 10,
            color: ColorUtil.ColorFromRgba(155, 255, 255, 153),
            minPos: new Vec3d(), maxPos: new Vec3d(0.1, 0.0, 0.1),
            minVelocity: new Vec3f(-4f, 0f, -4f), maxVelocity: new Vec3f(4f, 4f, 4f)
        )
        {
            Bounciness = 1f,
            VertexFlags = 128,
            addLifeLength = 0.5f,
            LifeLength = 0.5f,
            GravityEffect = 1.0f,
            ParticleModel = EnumParticleModel.Cube,
            MinSize = 0.4f,
            MaxSize = 0.6f,
            LightEmission = 0,
            WindAffected = false
        };


        // Advanced Particle Templates
        private static readonly AdvancedParticleProperties SparksTemplateAdvanced;
        private static readonly AdvancedParticleProperties WindVerticalTemplateAdvanced;
        private static readonly AdvancedParticleProperties BlackSmokeTemplateAdvanced;
        private static readonly AdvancedParticleProperties WhiteSmokeTemplateAdvanced;
        private static readonly AdvancedParticleProperties WhiteSmoke2TemplateAdvanced;
        private static readonly AdvancedParticleProperties WhiteSlowSmokeTemplateAdvanced;
        private static readonly AdvancedParticleProperties WaterTemplateAdvanced;

        static ParticleManager()
        {
            // Инициализация шаблонов AdvancedParticleProperties
            SparksTemplateAdvanced = CreateSparksTemplate();
            WindVerticalTemplateAdvanced = CreateWindVerticalTemplate();
            BlackSmokeTemplateAdvanced = CreateBlackSmokeTemplate();
            WhiteSmokeTemplateAdvanced = CreateWhiteSmokeTemplate();
            WhiteSmoke2TemplateAdvanced = CreateWhiteSmoke2Template();
            WhiteSlowSmokeTemplateAdvanced = CreateWhiteSlowSmokeTemplate();
            WaterTemplateAdvanced = CreateWaterDripTemplate();
        }

        private static AdvancedParticleProperties CreateSparksTemplate()
        {
            var template = new AdvancedParticleProperties();

            // Настройки количества частиц
            template.Quantity.avg = 7.5f;
            template.Quantity.var = 2.5f;

            // Настройки цвета в формате HSVA
            template.HsvaColor[0].avg = 39; // Hue - голубоватый оттенок
            template.HsvaColor[0].var = 2;
            template.HsvaColor[1].avg = 255;  // Saturation
            template.HsvaColor[1].var = 2;
            template.HsvaColor[2].avg = 255; // Value
            template.HsvaColor[2].var = 2;
            template.HsvaColor[3].avg = 255;  // Alpha
            template.HsvaColor[3].var = 10;

            // Настройки скорости частиц
            template.Velocity[0].avg = 0f;
            template.Velocity[1].avg = 1.1f;
            template.Velocity[2].avg = 0f;
            template.Velocity[0].var = 3.5f;
            template.Velocity[1].var = 0.2f;
            template.Velocity[2].var = 3.5f;


            // Физические свойства
            template.WindAffectednes = 0f;
            template.LifeLength.avg = 0.5f;
            template.LifeLength.var = 0.1f;
            template.GravityEffect.avg = 1.0f;
            template.GravityEffect.var = 0f;
            template.Bounciness= 1f;

            // свечение bloom
            template.VertexFlags= 128;

            // Визуальные свойства
            template.ParticleModel = EnumParticleModel.Cube;
            template.Size.avg = 0.25f;
            template.Size.var = 0.1f;
            
            return template;
        }

        private static AdvancedParticleProperties CreateBlackSmokeTemplate()
        {
            var template = new AdvancedParticleProperties();

            template.Quantity.avg = 0.5f;
            template.Quantity.var = 0.1f;

            template.HsvaColor[0].avg = 0;
            template.HsvaColor[0].var = 0;
            template.HsvaColor[1].avg = 0;
            template.HsvaColor[1].var = 0;
            template.HsvaColor[2].avg = 20;
            template.HsvaColor[2].var = 5;
            template.HsvaColor[3].avg = 200;
            template.HsvaColor[3].var = 20;

            template.Velocity[0].avg = 0;
            template.Velocity[1].avg = 0;
            template.Velocity[2].avg = 0;
            template.Velocity[0].var = 0.1f;
            template.Velocity[1].var = 0.1f;
            template.Velocity[2].var = 0.1f;

            template.WindAffectednes = 1f;
            template.WindAffectednesAtPos = 1f;
            template.LifeLength.avg = 1f;
            template.LifeLength.var = 0.5f;
            template.GravityEffect.avg = -0.01f;
            template.GravityEffect.var = 0f;

            template.ParticleModel = EnumParticleModel.Quad;
            template.SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, 1f);
            template.OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -150);
            template.Size.avg = 1.0f;
            template.Size.var = 0.2f;

            return template;
        }

        private static AdvancedParticleProperties CreateWhiteSmokeTemplate()
        {
            var template = new AdvancedParticleProperties();

            template.Quantity.avg = 0.5f;
            template.Quantity.var = 0.1f;

            template.HsvaColor[0].avg = 0;
            template.HsvaColor[0].var = 0;
            template.HsvaColor[1].avg = 0;
            template.HsvaColor[1].var = 0;
            template.HsvaColor[2].avg = 200; // Более светлый чем черный дым
            template.HsvaColor[2].var = 5;
            template.HsvaColor[3].avg = 200;
            template.HsvaColor[3].var = 20;

            template.Velocity[0].avg = 0;
            template.Velocity[1].avg = 0.5f;
            template.Velocity[2].avg = 0;
            template.Velocity[0].var = 0.1f;
            template.Velocity[1].var = 0.1f;
            template.Velocity[2].var = 0.1f;

            template.WindAffectednes = 1f;
            template.WindAffectednesAtPos = 1f;
            template.LifeLength.avg = 2f;
            template.LifeLength.var = 0.5f;
            template.GravityEffect.avg = -0.02f;
            template.GravityEffect.var = 0f;

            template.ParticleModel = EnumParticleModel.Quad;
            template.SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, 1f);
            template.OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -100);
            template.Size.avg = 0.8f;
            template.Size.var = 0.2f;

            return template;
        }

        private static AdvancedParticleProperties CreateWindVerticalTemplate()
        {
            var template = new AdvancedParticleProperties();

            template.Quantity.avg = 0.5f;
            template.Quantity.var = 0.1f;

            template.HsvaColor[0].avg = 0;
            template.HsvaColor[0].var = 0;
            template.HsvaColor[1].avg = 0;
            template.HsvaColor[1].var = 0;
            template.HsvaColor[2].avg = 90;
            template.HsvaColor[2].var = 35;
            template.HsvaColor[3].avg = 230;
            template.HsvaColor[3].var = 20;

            template.Velocity[0].avg = 0;
            template.Velocity[1].avg = 4.0f;
            template.Velocity[2].avg = 0;
            template.Velocity[0].var = 0.1f;
            template.Velocity[1].var = 0.3f;
            template.Velocity[2].var = 0.1f;

            template.WindAffectednes = 0f;
            template.WindAffectednesAtPos = 0f;
            template.LifeLength.avg = 1f;
            template.LifeLength.var = 0.5f;
            template.GravityEffect.avg = -0.02f;
            template.GravityEffect.var = 0f;

            template.ParticleModel = EnumParticleModel.Quad;
            template.Size.avg = 0.1f;
            template.Size.var = 0.025f;

            return template;
        }



        private static AdvancedParticleProperties CreateWhiteSmoke2Template()
        {
            var template = new AdvancedParticleProperties();

            template.Quantity.avg = 1.0f;
            template.Quantity.var = 0.0f;

            template.HsvaColor[0].avg = 0;
            template.HsvaColor[0].var = 0;
            template.HsvaColor[1].avg = 0;
            template.HsvaColor[1].var = 0;
            template.HsvaColor[2].avg = 200; // Более светлый чем черный дым
            template.HsvaColor[2].var = 5;
            template.HsvaColor[3].avg = 220;
            template.HsvaColor[3].var = 20;

            template.Velocity[0].avg = 0;
            template.Velocity[1].avg = 0.5f;
            template.Velocity[2].avg = 0;
            template.Velocity[0].var = 0.1f;
            template.Velocity[1].var = 0.1f;
            template.Velocity[2].var = 0.1f;

            template.WindAffectednes = 1f;
            template.WindAffectednesAtPos = 1f;
            template.LifeLength.avg = 2f;
            template.LifeLength.var = 0.5f;
            template.GravityEffect.avg = -0.02f;
            template.GravityEffect.var = 0f;

            template.ParticleModel = EnumParticleModel.Quad;
            template.SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, 1f);
            template.OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -100);
            template.Size.avg = 0.65f;
            template.Size.var = 0.2f;

            return template;
        }



        private static AdvancedParticleProperties CreateWhiteSlowSmokeTemplate()
        {
            var template = new AdvancedParticleProperties();

            template.Quantity.avg = 0.5f;
            template.Quantity.var = 0.1f;

            template.HsvaColor[0].avg = 0;
            template.HsvaColor[0].var = 0;
            template.HsvaColor[1].avg = 0;
            template.HsvaColor[1].var = 0;
            template.HsvaColor[2].avg = 90;
            template.HsvaColor[2].var = 5;
            template.HsvaColor[3].avg = 210;
            template.HsvaColor[3].var = 20;

            template.Velocity[0].avg = 0;
            template.Velocity[1].avg = 0;
            template.Velocity[2].avg = 0;
            template.Velocity[0].var = 0.1f;
            template.Velocity[1].var = 0.1f;
            template.Velocity[2].var = 0.1f;

            template.WindAffectednes = 1f;
            template.WindAffectednesAtPos = 1f;
            template.LifeLength.avg = 2f;
            template.LifeLength.var = 0.5f;
            template.GravityEffect.avg = -0.01f;
            template.GravityEffect.var = 0f;

            template.ParticleModel = EnumParticleModel.Quad;
            template.SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, 0.5f);
            template.OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -100);
            template.Size.avg = 0.625f;
            template.Size.var = 0.125f;

            return template;
        }
        private static AdvancedParticleProperties CreateWaterDripTemplate()
        {
            var template = new AdvancedParticleProperties();
    
            template.Quantity.avg = 1.9f;
            template.Quantity.var = 0.9f;
    
            // Цвет воды (голубой)
            template.HsvaColor[0].avg = 150;
            template.HsvaColor[0].var = 10;
            template.HsvaColor[1].avg = 255;
            template.HsvaColor[1].var = 0;
            template.HsvaColor[2].avg = 230;
            template.HsvaColor[2].var = 15;
            template.HsvaColor[3].avg = 200;
            template.HsvaColor[3].var = 30;
    
            // Скорость
            template.Velocity[0].avg = 0f;
            template.Velocity[1].avg = -1.0f;
            template.Velocity[2].avg = 0f;
            template.Velocity[0].var = 0.1f;
            template.Velocity[1].var = 0.2f;
            template.Velocity[2].var = 0.1f;
    
            template.LifeLength.avg = 0.4f;
            template.LifeLength.var = 0.1f;
            template.GravityEffect.avg = 0f;
            template.ParticleModel = EnumParticleModel.Quad;
            template.Size.avg = 0.07f;
            template.Size.var = 0.03f;
            template.OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -200f);
    
            return template;
        }
       




        // Асинхронные методы с использованием шаблонов

        public static void SpawnWindVerticalAsync(IAsyncParticleManager manager, Vec3d pos, Vec3d variationPos)
        {
            var particles = WindVerticalTemplateAdvanced.Clone();
            particles.WindAffectednesAtPos = 0.0f; // обязательно
            particles.basePos = RandomBlockPos(pos, variationPos);
            manager.Spawn(particles);
        }

        public static void SpawnElectricSparksAsync(IAsyncParticleManager manager, Vec3d pos, Vec3d variationPos)
        {
            var particles = SparksTemplateAdvanced.Clone();
            particles.WindAffectednesAtPos = 0.1f; // обязательно
            particles.basePos = RandomBlockPos(pos, variationPos);
            manager.Spawn(particles);
        }

        public static void SpawnBlackSlowSmokeAsync(IAsyncParticleManager manager, Vec3d pos, Vec3d variationPos)
        {
            var particles = BlackSmokeTemplateAdvanced.Clone();
            particles.WindAffectednesAtPos = 1f; // обязательно
            particles.basePos = RandomBlockPos(pos, variationPos);
            manager.Spawn(particles);
        }

        public static void SpawnWhiteSmokeAsync(IAsyncParticleManager manager, Vec3d pos, Vec3d variationPos)
        {
            var particles = WhiteSmokeTemplateAdvanced.Clone();
            particles.WindAffectednesAtPos = 1f; // обязательно
            particles.basePos = RandomBlockPos(pos, variationPos);
            manager.Spawn(particles);
        }

        public static void SpawnWhiteSmoke2Async(IAsyncParticleManager manager, Vec3d pos, Vec3d variationPos)
        {
            var particles = WhiteSmoke2TemplateAdvanced.Clone();
            particles.WindAffectednesAtPos = 1f; // обязательно
            particles.basePos = RandomBlockPos(pos, variationPos);
            manager.Spawn(particles);
        }

        public static void SpawnWhiteSlowSmokeAsync(IAsyncParticleManager manager, Vec3d pos, Vec3d variationPos)
        {
            var particles = WhiteSlowSmokeTemplateAdvanced.Clone();
            particles.WindAffectednesAtPos = 1f; // обязательно
            particles.basePos = RandomBlockPos(pos, variationPos);
            manager.Spawn(particles);
        }
        
        public static void SpawnWaterSparksAsync(IAsyncParticleManager manager, Vec3d pos, Vec3d variationPos)
        {
            var particles = WaterTemplateAdvanced.Clone();
            particles.WindAffectednesAtPos = 0.1f; // обязательно
            particles.basePos = RandomBlockPos(pos, variationPos);
            manager.Spawn(particles);
        }



        // Перегрузки для удобства (без variationPos)
        public static void SpawnWindVerticalAsync(IAsyncParticleManager manager, Vec3d pos)
        {
            SpawnWindVerticalAsync(manager, pos, new Vec3d(0.5, 0.0, 0.5));
        }



        public static void SpawnElectricSparksAsync(IAsyncParticleManager manager, Vec3d pos)
        {
            SpawnElectricSparksAsync(manager, pos, new Vec3d(0.1, 0.0, 0.1));
        }

        public static void SpawnBlackSlowSmokeAsync(IAsyncParticleManager manager, Vec3d pos)
        {
            SpawnBlackSlowSmokeAsync(manager, pos, new Vec3d(0.5, 0.1, 0.5));
        }

        public static void SpawnWhiteSmokeAsync(IAsyncParticleManager manager, Vec3d pos)
        {
            SpawnWhiteSmokeAsync(manager, pos, new Vec3d(0.1, 0.1, 0.1));
        }

        public static void SpawnWhiteSmoke2Async(IAsyncParticleManager manager, Vec3d pos)
        {
            SpawnWhiteSmoke2Async(manager, pos, new Vec3d(0.1, 0.1, 0.1));
        }


        public static void SpawnWhiteSlowSmokeAsync(IAsyncParticleManager manager, Vec3d pos)
        {
            SpawnWhiteSlowSmokeAsync(manager, pos, new Vec3d(0.5, 0.1, 0.5));
        }
        
        public static void SpawnWaterSparksAsync(IAsyncParticleManager manager, Vec3d pos)
        {
            SpawnWaterSparksAsync(manager, pos, new Vec3d(0.1, 0.0, 0.1));
        }



        /// <summary>
        /// Метод для спавна частиц по типу
        /// </summary>
        /// <param name="manager"></param>
        /// <param name="pos"></param>
        /// <param name="type"></param>
        public static void SpawnParticlesAsync(IAsyncParticleManager manager, Vec3d pos, int type)
        {
            if (type == 0)
                SpawnWhiteSlowSmokeAsync(manager, pos);
            else if (type == 1)
                SpawnBlackSlowSmokeAsync(manager, pos);
            else if (type == 2)
                SpawnWhiteSmokeAsync(manager, pos);
            else if (type == 3)
                SpawnWhiteSmoke2Async(manager, pos);
            else if (type == 4)
                SpawnElectricSparksAsync(manager, pos);
            else if (type == 5)
                SpawnWindVerticalAsync(manager, pos);
            else if (type == 6)
                SpawnWaterSparksAsync(manager, pos);

        }




        /// <summary>
        /// Генератор случайных чисел 
        /// </summary>
        private static readonly Random rand = new Random();

        /// <summary>
        /// Генерирует случайную позицию в пределах вариации от заданной позиции
        /// </summary>
        /// <param name="pos"></param>
        /// <param name="variation"></param>
        /// <returns></returns>
        public static Vec3d RandomBlockPos(Vec3d pos, Vec3d variation)
        {
            return new Vec3d(
                pos.X + (rand.NextDouble() * 2 - 1) * variation.X,
                pos.Y + (rand.NextDouble() * 2 - 1) * variation.Y,
                pos.Z + (rand.NextDouble() * 2 - 1) * variation.Z
            );
        }





        /*


       public static void SpawnBlackSmoke(IWorldAccessor world, Vec3d pos)
       {
           SmokeTemplate.MinPos = pos;
           world.SpawnParticles(SmokeTemplate);
       }

       public static void SpawnWhiteSmoke(IWorldAccessor world, Vec3d pos)
       {
           WhiteSmokeTemplate.MinPos = pos;
           world.SpawnParticles(WhiteSmokeTemplate);
       }

       public static void SpawnWhiteSlowSmoke(IWorldAccessor world, Vec3d pos)
       {
           WhiteSlowSmokeTemplate.MinPos = pos;
           world.SpawnParticles(WhiteSlowSmokeTemplate);
       }





     /// <summary>
     /// Шаблон «чёрного дыма»
     /// </summary>
     private static readonly SimpleParticleProperties SmokeTemplate = new(
         minQuantity: 2, maxQuantity: 2,
         color: ColorUtil.ColorFromRgba(50, 50, 50, 200),
         minPos: new Vec3d(), maxPos: new Vec3d(0.8, 0.1, 0.8),
         minVelocity: new Vec3f(-0.1f, -0.1f, -0.1f), maxVelocity: new Vec3f(0.1f, 0.1f, 0.1f)
     )
     {
         WindAffected = true,
         WindAffectednes = 1.0f,
         LifeLength = 2f,
         GravityEffect = -0.01f,
         ParticleModel = EnumParticleModel.Quad,
         SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, 1f),
         OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -100),
         MinSize = 0.8f,
         MaxSize = 1.2f,
     };

     /// <summary>
     /// Шаблон «белого дыма» для дымовых труб
     /// </summary>
     private static readonly SimpleParticleProperties WhiteSmokeTemplate = new(
         minQuantity: 1, maxQuantity: 1,
         color: ColorUtil.ColorFromRgba(210, 210, 210, 200),
         minPos: new Vec3d(-0.1, -0.1, -0.1), maxPos: new Vec3d(0.1, 0.1, 0.1),
         minVelocity: new Vec3f(-0.1f, -0.1f, 0f), maxVelocity: new Vec3f(0.1f, 0.1f, 0.1f)
     )
     {
         WindAffected = true,
         WindAffectednes = 1.0f,
         LifeLength = 2f,
         GravityEffect = -0.02f,
         ParticleModel = EnumParticleModel.Quad,
         SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, 1f),
         OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -100)
     };

     /// <summary>
     /// Шаблон «белого дыма» подготовки сгореть
     /// </summary>
     private static readonly SimpleParticleProperties WhiteSlowSmokeTemplate = new(
         minQuantity: 2, maxQuantity: 2,
         color: ColorUtil.ColorFromRgba(210, 210, 210, 200),
         minPos: new Vec3d(), maxPos: new Vec3d(0.8, 0.1, 0.8),
         minVelocity: new Vec3f(-0.1f, -0.1f, -0.1f), maxVelocity: new Vec3f(0.1f, 0.1f, 0.1f)
     )
     {
         WindAffected = true,
         WindAffectednes = 1.0f,
         LifeLength = 2f,
         GravityEffect = -0.01f,
         ParticleModel = EnumParticleModel.Quad,
         SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, 0.5f),
         OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -100),
         MinSize = 0.5f,
         MaxSize = 0.75f,
     };
     */



    }
}