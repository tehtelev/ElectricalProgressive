using ElectricalProgressive.Utils;

using Vintagestory.API.Common;

namespace ElectricalProgressive.Content.Block.EFence
{
    public class BlockEntityEFence : BlockEntityEBase
    {

        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);

            var electricity = this.ElectricalProgressive;
            if (electricity == null)
                return;

            electricity.Connection = Facing.AllAll;
            electricity.Eparams = (new(128, 128.0F, "", 0, 1, 1, false, false, true), 0);
            electricity.Eparams = (new(128, 128.0F, "", 0, 1, 1, false, false, true), 1);
            electricity.Eparams = (new(128, 128.0F, "", 0, 1, 1, false, false, true), 2);
            electricity.Eparams = (new(128, 128.0F, "", 0, 1, 1, false, false, true), 3);
            electricity.Eparams = (new(128, 128.0F, "", 0, 1, 1, false, false, true), 4);
            electricity.Eparams = (new(128, 128.0F, "", 0, 1, 1, false, false, true), 5);
        }
    }
}
