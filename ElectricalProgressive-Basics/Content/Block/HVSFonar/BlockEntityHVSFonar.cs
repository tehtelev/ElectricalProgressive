using ElectricalProgressive.Utils;
using Vintagestory.API.Common;

namespace ElectricalProgressive.Content.Block.HVSFonar
{
    internal class BlockEntityHVSFonar : BlockEntityEIBase
    {
        private BEBehaviorHVSFonar Behavior => this.GetBehavior<BEBehaviorHVSFonar>();

        public bool IsEnabled
        {
            get
            {
                if (this.Behavior == null)
                    return false;

                return this.Behavior.LightLevel >= 1;
            }
        }

        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);

            if (this.EPImmersive == null || byItemStack == null)
                return;

            //задаем электрические параметры блока/проводника
            LoadImmersiveEProperties.Load(this.Block, this);
        }


    }
}

