using ElectricalProgressive.Content.Block.ECable;
using ElectricalProgressive.Utils;
using Vintagestory.API.Common;

namespace ElectricalProgressive.Content.Block.EConnector;

public class BlockEntityEConnector : BlockEntityEFacingBase
{
    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);

        var electricity = this.ElectricalProgressive;
        if (electricity == null)
            return;

        //задаем электрические параметры блока/проводника
        LoadEProperties.Load(this.Block, this);

        
    }
}