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

            var voltage = MyMiniLib.GetAttributeInt(this.Block, "voltage", 32);
            var maxCurrent = MyMiniLib.GetAttributeFloat(this.Block, "maxCurrent", 5.0F);
            var isolated = MyMiniLib.GetAttributeBool(this.Block, "isolated", false);
            var isolatedEnvironment = MyMiniLib.GetAttributeBool(this.Block, "isolatedEnvironment", true);

            electricity.Connection = Facing.AllAll;
            electricity.Eparams = (new(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), 0);
            electricity.Eparams = (new(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), 1);
            electricity.Eparams = (new(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), 2);
            electricity.Eparams = (new(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), 3);
            electricity.Eparams = (new(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), 4);
            electricity.Eparams = (new(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), 5);
        }
    }
}
