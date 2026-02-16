using ElectricalProgressive.Utils;
using Vintagestory.API.Common;

namespace ElectricalProgressive.Content.Block.WallConnector
{
    internal class BlockEntityWConnector : BlockEntityEIBase
    {
        private BEBehaviorWConnector Behavior => this.GetBehavior<BEBehaviorWConnector>();
    

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

