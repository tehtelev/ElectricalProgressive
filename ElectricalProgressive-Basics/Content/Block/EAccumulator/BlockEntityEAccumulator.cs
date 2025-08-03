using ElectricalProgressive.Utils;
using System.Linq;
using Vintagestory.API.Common;

namespace ElectricalProgressive.Content.Block.EAccumulator;

public class BlockEntityEAccumulator : BlockEntityEBase
{
    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);

        if (this.ElectricalProgressive == null || byItemStack == null)
            return;

        //задаем параметры блока/проводника
        var voltage = MyMiniLib.GetAttributeInt(byItemStack.Block, "voltage", 32);
        var maxCurrent = MyMiniLib.GetAttributeFloat(byItemStack.Block, "maxCurrent", 5.0F);
        var isolated = MyMiniLib.GetAttributeBool(byItemStack.Block, "isolated", false);
        var isolatedEnvironment = MyMiniLib.GetAttributeBool(byItemStack.Block, "isolatedEnvironment", false);

        var eparams = new EParams(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment);
        var downAllIndex = FacingHelper.Faces(Facing.DownAll).First().Index;

        this.ElectricalProgressive!.Connection = Facing.DownAll;
        this.ElectricalProgressive.Eparams = (eparams, downAllIndex);
    }
}
