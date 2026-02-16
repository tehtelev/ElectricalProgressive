using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Block.HVSFonar
{
    public class BEBehaviorHVSFonar : BlockEntityBehavior, IEImmersiveConsumer
    {
        /// <summary>
        /// Уровень света
        /// </summary>
        public int LightLevel { get; private set; }

        public bool IsBurned => this.Block.Code.GetName().Contains("burned"); // пока так 

        /// <summary>
        /// Ключ для сохранения уровня света в дереве атрибутов
        /// </summary>
        public const string LightLevelKey = "electricalprogressive:LightLevel";

        /// <summary>
        /// Максимальное потребление
        /// </summary>
        private readonly int _maxConsumption;
        private bool hasBurnout;
        private bool prepareBurnout;

        public BEBehaviorHVSFonar(BlockEntity blockEntity) : base(blockEntity)
        {
            _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 4);
        }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);

            if (Blockentity is BlockEntityHVSFonar entity &&
                entity.EPImmersive != null)
            {
                // вычисляем высоту для частиц дыма
                /*
                var heightStr = entity.Block.Variant["height"];
                var height = heightStr.ToFloat() - 1;
                entity.ElectricalProgressive.ParticlesOffsetPos.Clear();
                entity.ElectricalProgressive.ParticlesOffsetPos.Add(new Vec3d(0.1, height, 0.1));
                */
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetInt(LightLevelKey, LightLevel);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            LightLevel = tree.GetInt(LightLevelKey);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
        {
            base.GetBlockInfo(forPlayer, stringBuilder);

            //проверяем не сгорел ли прибор
            if (Blockentity is not BlockEntityHVSFonar)
                return;

            if (IsBurned)
            {
                return;
            }

            stringBuilder.AppendLine(StringHelper.Progressbar(this.LightLevel * 100.0f / _maxConsumption));
            stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Consumption") + ": " + this.LightLevel + "/" + _maxConsumption + " " + Lang.Get("electricalprogressivebasics:W"));

            stringBuilder.AppendLine();
        }

        #region IElectricConsumer

        public float Consume_request()
        {
            return _maxConsumption;
        }


        public float AvgConsumeCoeff { get; set; }

        public void Consume_receive(float amount)
        {
            if (Api is null)
                return;

            var roundAmount = (int)Math.Round(Math.Min(amount, _maxConsumption), MidpointRounding.AwayFromZero);

            if (roundAmount == LightLevel)
                return;

            //включаем если питание больше 25 %
            if (roundAmount * 4 >= _maxConsumption && Block.Variant["state"] == "disabled") // включаем если питание больше 25%
            {
                Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "enabled")).BlockId, Pos);
                Blockentity.MarkDirty(true);
            }
            else if (roundAmount * 4 < _maxConsumption && Block.Variant["state"] == "enabled") // гасим если питание меньше 25%
            {
                Api.World.BlockAccessor.ExchangeBlock(Api.World.GetBlock(Block.CodeWithVariant("state", "disabled")).BlockId, Pos);
                Blockentity.MarkDirty(true);
            }




            // в любом случае обновляем значение
            LightLevel = roundAmount;

        }

        public float getPowerReceive()
        {
            return this.LightLevel;
        }

        public float getPowerRequest()
        {
            return _maxConsumption;
        }

        public void Update()
        {
            if (Blockentity is not BlockEntityHVSFonar entity ||
                entity.EPImmersive == null)
            {
                return;
            }

            //entity.MarkDirty();

            bool anyBurnout = false;
            bool anyPrepareBurnout = false;

            var eParam = entity.EPImmersive.MainEparams();
            {
                if (!hasBurnout && eParam.burnout)
                {
                    hasBurnout = true;
                    entity.MarkDirty(true);
                }

                if (!prepareBurnout && eParam.ticksBeforeBurnout > 0)
                {
                    prepareBurnout = true;
                    entity.MarkDirty(true);
                }

                if (eParam.burnout)
                    anyBurnout = true;

                if (eParam.ticksBeforeBurnout > 0)
                    anyPrepareBurnout = true;
            }

            if (!anyBurnout && hasBurnout)
            {
                hasBurnout = false;
                entity.MarkDirty(true);
            }

            if (!anyPrepareBurnout && prepareBurnout)
            {
                prepareBurnout = false;
                entity.MarkDirty(true);
            }

            
        }

        #endregion
    }
}
