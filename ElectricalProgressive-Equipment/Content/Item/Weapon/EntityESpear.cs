using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using ElectricalProgressive.Utils;

namespace ElectricalProgressive.Content.Item.Weapon
{
    public class EntityESpear : EntityProjectile
    {
        public bool canStrike; //можно ли ударить молнией

        public EntityESpear() : base()
        {
        }

        /// <summary>
        /// Обязательно
        /// </summary>
        /// <param name="properties"></param>
        /// <param name="api"></param>
        /// <param name="InChunkIndex3d"></param>
        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
        {
            base.Initialize(properties, api, InChunkIndex3d);
            if (api.Side == EnumAppSide.Client)
            {
                WatchedAttributes.RegisterModifiedListener("damage", UpdateDamageParticles);
            }
        }



        /// <summary>
        /// Рисуем частицы при необходимости
        /// </summary>
        private void UpdateDamageParticles()
        { 
            /*
            if (World is IClientWorldAccessor clientWorld)
            {
                clientWorld.SpawnParticles(
                    new SimpleParticleProperties(
                        10, 15,
                        ColorUtil.ColorFromRgba(255, 50, 50, 100),
                        ServerPos.XYZ,
                        ServerPos.XYZ,
                        new Vec3f(-0.25f, -0.25f, -0.25f),
                        new Vec3f(0.25f, 0.25f, 0.25f),
                        0.1f,
                        0.3f,
                        0.5f,
                        0.7f
                    )
                );
            }
            */
        }


        /// <summary>
        /// Попадание в энтити
        /// </summary>
        /// <param name="entity"></param>
        protected override void impactOnEntity(Entity entity)
        {
            base.impactOnEntity(entity);

            // без погоды не будет ничего
            if  (ElectricalProgressiveEquipment.WeatherSystemServer == null)
                return;


            // живой и можно сразить молнией?
            if (entity.Alive && canStrike)
            {
                var hitPoint = entity.Pos;

                ElectricalProgressiveEquipment.WeatherSystemServer.SpawnLightningFlash(hitPoint.XYZ); // бах молнией

                entity.IsOnFire = true; // поджигаем

                canStrike = false; // больше не можем бить молнией

                //ломаем сильно копье от молнии
                int lightstrike = MyMiniLib.GetAttributeInt(ProjectileStack.Block, "lightstrike", 2000);
                int consume = MyMiniLib.GetAttributeInt(ProjectileStack.Block, "consume", 20);

                int energy = ProjectileStack.Attributes.GetInt("durability") * consume;

                if (energy > lightstrike)
                {
                    energy -= lightstrike;
                    ProjectileStack.Attributes.SetInt("durability", Math.Max(1, energy / consume));
                    
                }
                else
                {
                    ProjectileStack.Attributes.SetInt("durability", 1);
                }
            }
        }


    }
}
