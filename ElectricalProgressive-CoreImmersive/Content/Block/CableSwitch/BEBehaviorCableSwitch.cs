using ElectricalProgressive.Utils;
using EPImmersive.Interface;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;


namespace EPImmersive.Content.Block.CableSwitch
{
    public class BEBehaviorCableSwitch : BlockEntityBehavior, IEImmersiveConductor
    {

        private bool hasBurnout;
        private bool prepareBurnout;
        private bool _isOpen;
        
        public const string IsOpenKey = "electricalprogressive:isopen";

        public bool IsOpen
        {
            get
            {
                return _isOpen;
            }
            set
            {
                if (_isOpen != value)
                {
                    _isOpen = value;
                }
            }
        }


        public BEBehaviorCableSwitch(BlockEntity blockEntity) : base(blockEntity)
        {

        }

        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);

            _isOpen = MyMiniLib.GetAttributeBool(this.Block, "IsOpen", true);
        }




        public void Update()
        {
            if (Blockentity is not BlockEntityCableSwitch entity ||
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



        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetBool(IsOpenKey, _isOpen);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            _isOpen = tree.GetBool(IsOpenKey);
        }
    }
}
