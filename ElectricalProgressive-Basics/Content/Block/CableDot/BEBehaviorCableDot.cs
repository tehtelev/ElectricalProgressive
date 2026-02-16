using ElectricalProgressive.Interface;
using ElectricalProgressive.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Block.CableDot
{
    public class BEBehaviorCableDot : BlockEntityBehavior, IEImmersiveConductor
    {

        private bool hasBurnout;
        private bool prepareBurnout;

        public bool IsOpen { get; set; }


        public const string IsOpenKey = "electricalprogressive:isopen";

        public BEBehaviorCableDot(BlockEntity blockEntity) : base(blockEntity)
        {

        }

        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);

            IsOpen = MyMiniLib.GetAttributeBool(this.Block, "IsOpen", true);
        }




        public void Update()
        {
            if (Blockentity is not BlockEntityEIBase entity ||
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
            tree.SetBool(IsOpenKey, IsOpen);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            IsOpen = tree.GetBool(IsOpenKey);
        }
    }
}
