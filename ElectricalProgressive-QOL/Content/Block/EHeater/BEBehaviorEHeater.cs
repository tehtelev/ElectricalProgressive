using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EHeater
{
    public class BEBehaviorEHeater : BlockEntityBehavior, IElectricConsumer
    {
        public int HeatLevel { get; private set; }

        private int _maxConsumption;
        private bool hasBurnout;
        private bool prepareBurnout;

        public float GreenhouseBonus { get; set; }

        public const string HeatLevelKey = "electricalprogressive:heatlevel";

        public bool IsBurned => this.Block.Code.GetName().Contains("burned"); // пока так 

        private float _tempKoeff;

        public float TempKoeff => _tempKoeff;

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);

            GreenhouseBonus = 0;

            _tempKoeff = MyMiniLib.GetAttributeFloat(this.Block, "temp_koeff", 0f);

            _maxConsumption = MyMiniLib.GetAttributeInt(this.Block, "maxConsumption", 4);
        }



        public BEBehaviorEHeater(BlockEntity blockEntity) : base(blockEntity)
        {
           
        }

        public float AvgConsumeCoeff { get; set; }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
        {
            base.GetBlockInfo(forPlayer, stringBuilder);

            //проверяем не сгорел ли прибор
            if (Blockentity is not BlockEntityEHeater entity || IsBurned)
                return;
            

            stringBuilder.AppendLine(StringHelper.Progressbar(this.HeatLevel * 100.0f / _maxConsumption));
            stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:Consumption") + ": " + this.HeatLevel + "/" + _maxConsumption + " " + Lang.Get("electricalprogressivebasics:W"));




            if (GreenhouseBonus<=0)
                stringBuilder.AppendLine(Lang.Get("electricalprogressiveqol:heater-no-bonus"));
            else
            {
                stringBuilder.AppendLine(Lang.Get("electricalprogressiveqol:heater-bonus", GreenhouseBonus.ToString("F2")));
            }


            stringBuilder.AppendLine();
        }


        public float Consume_request()
        {
            return _maxConsumption;
        }
        
        public void Consume_receive(float amount)
        {
            if (this.Api is not { } api)
                return;

            var roundAmount = (int)Math.Round(amount, MidpointRounding.AwayFromZero);
            if (roundAmount == this.HeatLevel || this.Block.Variant["state"] == "burned")
                return;

            // включаем если питание больше 1
            if (roundAmount >= 1 && this.Block.Variant["state"] == "disabled")
            {
                api.World.BlockAccessor.ExchangeBlock(api.World.GetBlock(Block.CodeWithVariant("state", "enabled")).BlockId, Pos);
            }
            // гасим если питание меньше 1
            else if (roundAmount < 1 && this.Block.Variant["state"] == "enabled")
            {
                api.World.BlockAccessor.ExchangeBlock(api.World.GetBlock(Block.CodeWithVariant("state", "disabled")).BlockId, Pos);
            }

            this.HeatLevel = roundAmount;
            this.Blockentity.MarkDirty(true);
        }

        public void Update()
        {
            if (Blockentity is not BlockEntityEHeater entity ||
                entity.ElectricalProgressive == null ||
                entity.ElectricalProgressive.AllEparams is null)
            {
                return;
            }

            bool anyBurnout = false;
            bool anyPrepareBurnout = false;

            foreach (var eParam in entity.ElectricalProgressive.AllEparams)
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




            if (!hasBurnout || entity.Block.Variant["state"] == "burned")
                return;

            // Используем CodeWithVariant вместо CodeWithVariants для одного варианта
            // Это эффективнее, так как не требует создания массивов
            const string type = "state";
            const string variant = "burned";

            // Кэшируем блок для обмена
            var burnedBlock = Api.World.GetBlock(Block.CodeWithVariant(type, variant));
            Api.World.BlockAccessor.ExchangeBlock(burnedBlock.BlockId, Pos);
        }

        public float getPowerReceive()
        {
            return this.HeatLevel;
        }

        public float getPowerRequest()
        {
            return _maxConsumption;
        }

       


        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetInt(HeatLevelKey, HeatLevel);
            tree.SetFloat("GreenhouseBonus", GreenhouseBonus);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            HeatLevel = tree.GetInt(HeatLevelKey);
            GreenhouseBonus= tree.GetFloat("GreenhouseBonus");
        }


    }
}
