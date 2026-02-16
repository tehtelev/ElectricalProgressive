using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Block.HVTransformator
{
    public class BEBehaviorLV2HVTransformator : BlockEntityBehavior, IEImmersiveProducer, IElectricConsumer
    {
        private float _storedEnergy = 0f;
        private float _maxCapacity = 0f;
        private float _lastElectricReceived = 0f;
        private float _lastElectricRequested = 0f;
        private float _immersiveOrder = 0f;
        private float _immersiveGiven = 0f;
        private float _speedOfElectricity = 0f;

        private bool hasBurnout;
        private bool prepareBurnout;
        public bool IsOpen { get; set; }

        public const string StoredEnergyKey = "electricalprogressive:storedenergy";
        public const string ImmersiveOrderKey = "ElectricalProgressive:immersiveorder";
        public const string ImmersiveGivenKey = "ElectricalProgressive:immersivegiven";
        public const string IsOpenKey = "electricalprogressive:isopen";

        public BEBehaviorLV2HVTransformator(BlockEntity blockEntity) : base(blockEntity)
        {
        }

        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);
            IsOpen = MyMiniLib.GetAttributeBool(this.Block, "IsOpen", true);
            _maxCapacity = MyMiniLib.GetAttributeInt(this.Block, "maxcapacity", 5119);
        }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);

            // Получаем максимальную емкость из атрибутов
            _maxCapacity = MyMiniLib.GetAttributeInt(this.Block, "maxcapacity", 5119);

            // Получаем скорость электричества
            _speedOfElectricity = global::ElectricalProgressive.ElectricalProgressive.speedOfElectricity;

            // Регистрируем слушатель для обновления анимации, если мы на клиенте
            if (api.Side == EnumAppSide.Client)
            {
                Blockentity.RegisterGameTickListener(animUpdate, 2000);
            }
        }


        /// <summary>
        /// Управляет анимацией трансформатора в зависимости от наличия энергии в буфере. Если энергия есть, анимация включается, если нет - выключается.
        /// </summary>
        /// <param name="obj"></param>
        private void animUpdate(float obj)
        {
            var entity = Blockentity as BlockEntityHVTransformator;

            if (entity == null)
                return;

            if (_storedEnergy == 0)
            {
                entity.StopAnim();
            }
            else
            {
                entity.StartAnim();
            }

        }

        public void Update()
        {
            if (Blockentity is not BlockEntityHVTransformator entity ||
                entity.ElectricalProgressive == null ||
                entity.EPImmersive == null)
            {
                return;
            }

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

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetFloat(StoredEnergyKey, _storedEnergy);
            tree.SetFloat(ImmersiveOrderKey, _immersiveOrder);
            tree.SetFloat(ImmersiveGivenKey, _immersiveGiven);
            tree.SetBool(IsOpenKey, IsOpen);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            _storedEnergy = tree.GetFloat(StoredEnergyKey);
            _immersiveOrder = tree.GetFloat(ImmersiveOrderKey);
            _immersiveGiven = tree.GetFloat(ImmersiveGivenKey);
            IsOpen = tree.GetBool(IsOpenKey);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder stringBuilder)
        {
            base.GetBlockInfo(forPlayer, stringBuilder);

            if (Blockentity is not BlockEntityHVTransformator entity)
                return;

            stringBuilder.AppendLine(StringHelper.Progressbar(_storedEnergy * 100.0f / _maxCapacity));
            stringBuilder.AppendLine("└ " + Lang.Get("electricalprogressivebasics:buffer") + ": " +
                                    ((int)_storedEnergy).ToString() + "/" +
                                    ((int)_maxCapacity).ToString() + " " + Lang.Get("electricalprogressivebasics:W"));
        }

        #region IElectricConsumer Implementation

        public float AvgConsumeCoeff { get; set; }

        public float Consume_request()
        {
            // Запрашиваем энергию только если есть свободное место в буфере
            // Учитываем скорость электричества: свободное место в ваттах
            float freeSpace = (_maxCapacity - _storedEnergy) * _speedOfElectricity;
            _lastElectricRequested = Math.Max(0, freeSpace);
            return _lastElectricRequested;
        }

        public void Consume_receive(float amount)
        {
            if (Api is null)
                return;

            // Получаем энергию и добавляем в буфер с учетом скорости электричества
            // amount приходит в ваттах, нам нужно перевести в джоули для хранения
            float maxCanStore = (_maxCapacity - _storedEnergy) * _speedOfElectricity;
            float amountToAdd = Math.Min(amount, maxCanStore);

            // Переводим из ваттов в джоули для хранения
            _storedEnergy += amountToAdd / _speedOfElectricity;
            _lastElectricReceived = amountToAdd;

            // Проверяем, чтобы не превысить максимальную емкость (дополнительная защита)
            if (_storedEnergy > _maxCapacity)
                _storedEnergy = _maxCapacity;
        }

        public float getPowerReceive()
        {
            return _lastElectricReceived;
        }

        public float getPowerRequest()
        {
            return _lastElectricRequested;
        }

        #endregion

        #region IEImmersiveProducer Implementation

        public float Produce_give()
        {
            // Говорим сколько можем дать в ваттах
            float availableInWatts = _storedEnergy * _speedOfElectricity;
            _immersiveGiven = availableInWatts;

            return _immersiveGiven;
        }

        public void Produce_order(float amount)
        {
            _immersiveOrder = amount;

            // Забираем энергию из буфера с учетом скорости электричества
            // amount приходит в ваттах, нам нужно вычесть в джоулях
            if (amount > 0 && _storedEnergy > 0)
            {
                float amountToTake = Math.Min(amount, _storedEnergy * _speedOfElectricity);
                _storedEnergy -= amountToTake / _speedOfElectricity;

                if (_storedEnergy < 0)
                    _storedEnergy = 0;
            }
        }

        public float getPowerGive()
        {
            return _immersiveGiven;
        }

        public float getPowerOrder()
        {
            return _immersiveOrder;
        }

        #endregion
    }
}