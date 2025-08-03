using ElectricalProgressive.Utils;
using System.Linq;
using Vintagestory.API.Common;

namespace ElectricalProgressive.Content.Block.ETransformator;

public class BlockEntityETransformator : BlockEntityEBase
{
    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);

        if (this.ElectricalProgressive == null || byItemStack == null)
            return;

        //задаем параметры блока/проводника
        var voltage = MyMiniLib.GetAttributeInt(byItemStack.Block, "voltage", 32);
        var lowVoltage = MyMiniLib.GetAttributeInt(byItemStack.Block, "lowVoltage", 32);
        var maxCurrent = MyMiniLib.GetAttributeFloat(byItemStack.Block, "maxCurrent", 5.0F);
        var isolated = MyMiniLib.GetAttributeBool(byItemStack.Block, "isolated", false);
        var isolatedEnvironment = MyMiniLib.GetAttributeBool(byItemStack.Block, "isolatedEnvironment", false);

        this.ElectricalProgressive.Connection = Facing.DownAll;
        this.ElectricalProgressive.Eparams = (
            new(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment),
            FacingHelper.Faces(Facing.DownAll).First().Index);
    }
}
