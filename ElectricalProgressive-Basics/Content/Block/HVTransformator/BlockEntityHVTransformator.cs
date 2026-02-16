using ElectricalProgressive.Utils;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.HVTransformator
{
    internal class BlockEntityHVTransformator : BlockEntityEIBase
    {
        
        public BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();

        private static MeshData? _mesh; // кеш для меша, который используется в анимации. Кеш нужен, чтобы не загружать меш из ресурсов каждый раз при тесселяции блока, а использовать уже загруженный и обработанный меш.
        private static Shape? _resultingShape; // кеш для формы, которая используется в анимации. Кеш нужен, чтобы не загружать форму из ресурсов каждый раз при тесселяции блока, а использовать уже загруженную и обработанную форму.


        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            
            RotationCache = CreateRotationCache();
            
            if (api.Side == EnumAppSide.Client)
            {
                // инициализируем аниматор
                if (animUtil != null)
                {
                    PrepareAnimUtil(api, "hvtransformator");
                    animUtil.InitializeAnimator("hvtransformator", _mesh, _resultingShape, new Vec3f(0, GetRotation(), 0f));
                }
            }
            
        }


        /// <summary>
        /// Подготавливает анимационный утилит для блока, загружая меш и форму из ресурсов
        /// </summary>
        /// <param name="api"></param>
        private void PrepareAnimUtil(ICoreAPI api, string cacheDictKey)
        {
            if (_mesh == null || _resultingShape==null)
            {
                AssetLocation shapePath = Block.Shape.Base.Clone().WithPathPrefixOnce("shapes/")
                    .WithPathAppendixOnce(".json");

                Shape _shape = Shape.TryGet(api, shapePath);
                
                _mesh = animUtil.CreateMesh(cacheDictKey, _shape, out _resultingShape, null);

            }
        }


        /// <summary>
        /// Получает угол поворота блока в градусах
        /// </summary>
        /// <returns></returns>
        public float GetRotation()
        {
            float rotateY = 0;

            if (Facing != Facing.None && RotationCache != null)
            {
                if (RotationCache.TryGetValue(Facing, out var rotation))
                {
                    rotateY = rotation.Y;
                }
            }
            else
            {
                rotateY = Block.Shape.rotateY;
            }
            return rotateY;
        }


        /// <summary>
        /// Аниматор блока, используется для анимации открывания дверцы генератора
        /// </summary>
        public BlockEntityAnimationUtil animUtil
        {
            get { return GetBehavior<BEBehaviorAnimatable>()?.animUtil!; }
        }


        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);

            if (this.ElectricalProgressive == null || byItemStack == null || this.ElectricalProgressive == null)
                return;

            //задаем электрические параметры блока/проводника
            LoadEProperties.Load(this.Block, this);
            LoadImmersiveEProperties.Load(this.Block, this);
        }





        /// <summary>
        /// Запускает анимацию открытия дверцы
        /// </summary>
        public void StartAnim()
        {
            if (animUtil?.activeAnimationsByAnimCode.ContainsKey("work") == false)
            {
                animUtil?.StartAnimation(new AnimationMetaData()
                {
                    Animation = "work",
                    Code = "work",
                    AnimationSpeed = 1.8f,
                    EaseOutSpeed = 15,
                    EaseInSpeed = 15
                });

                //применяем цвет и яркость
                //Block.LightHsv = new byte[] { 7, 7, 11 };

                //добавляем звук
                //_capi.World.PlaySoundAt(new("electricalprogressiveqol:sounds/freezer_open.ogg"), Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F);

            }

        }


        /// <summary>
        /// Закрывает дверцу генератора, останавливая анимацию открытия, если она запущена
        /// </summary>
        public void StopAnim()
        {
            if (animUtil?.activeAnimationsByAnimCode.ContainsKey("work") == true)
            {
                animUtil?.StopAnimation("work");

                //применяем цвет и яркость
                //Block.LightHsv = new byte[] { 7, 7, 0 };

                //добавляем звук
                //_capi.World.PlaySoundAt(new("electricalprogressiveqol:sounds/freezer_close.ogg"), Pos.X, Pos.Y, Pos.Z, null, false, 8.0F, 0.4F);
            }
        }


        /// <summary>
        /// Вызывается при тесселяции блока
        /// </summary>
        /// <param name="mesher"></param>
        /// <param name="tessThreadTesselator"></param>
        /// <returns></returns>
        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            base.OnTesselation(mesher, tessThreadTesselator); // вызываем базовую логику тесселяции


            // если анимации нет, то рисуем блок базовый
            if (animUtil?.activeAnimationsByAnimCode.Count==0)
            {
                (this.Block as ImmersiveWireBlock)._drawBaseMesh = true;
                return false;
            }

            (this.Block as ImmersiveWireBlock)._drawBaseMesh = false;

            return false;
        }


        /// <summary>
        /// Вызывается при удалении блока
        /// </summary>
        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            StopAnim();
            animUtil?.Dispose();

            _mesh.Dispose();
            _resultingShape = null;
        }

        /// <summary>
        /// Вызывается при выгрузке блока из мира (например, при удалении чанка)
        /// </summary>
        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            StopAnim();
            this.ElectricalProgressive?.OnBlockUnloaded();
            animUtil?.Dispose();

            _mesh.Dispose();
            _resultingShape = null;
        }


        private static Dictionary<Facing, RotationData> CreateRotationCache()
        {
            return new Dictionary<Facing, RotationData>
            {
                { Facing.NorthEast, new RotationData(0.0f, 0.0f, 0.0f) },
                { Facing.NorthWest, new RotationData(0.0f, 0.0f, 0.0f) },
                { Facing.NorthUp, new RotationData(0.0f, 0.0f, 0.0f) },
                { Facing.NorthDown, new RotationData(0.0f, 0.0f, 0.0f) },
                { Facing.EastNorth, new RotationData(0.0f, 270.0f, 0.0f) },
                { Facing.EastSouth, new RotationData(0.0f, 270.0f, 0.0f) },
                { Facing.EastUp, new RotationData(0.0f, 270.0f, 0.0f) },
                { Facing.EastDown, new RotationData(0.0f, 270.0f, 0.0f) },
                { Facing.SouthEast, new RotationData(0.0f, 180.0f, 0.0f) },
                { Facing.SouthWest, new RotationData(0.0f, 180.0f, 0.0f) },
                { Facing.SouthUp, new RotationData(0.0f, 180.0f, 0.0f) },
                { Facing.SouthDown, new RotationData(0.0f, 180.0f, 0.0f) },
                { Facing.WestNorth, new RotationData(0.0f, 90.0f, 0.0f) },
                { Facing.WestSouth, new RotationData(0.0f, 90.0f, 0.0f) },
                { Facing.WestUp, new RotationData(0.0f, 90.0f, 0.0f) },
                { Facing.WestDown, new RotationData(0.0f, 90.0f, 0.0f) }
            };
        }


    }
}

