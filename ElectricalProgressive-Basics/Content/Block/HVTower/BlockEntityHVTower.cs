using ElectricalProgressive.Utils;
using Vintagestory.API.Common;

namespace ElectricalProgressive.Content.Block.HVTower
{
    internal class BlockEntityHVTower : BlockEntityEIBase
    {
        private BEBehaviorHVTower Behavior => this.GetBehavior<BEBehaviorHVTower>();
    

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

