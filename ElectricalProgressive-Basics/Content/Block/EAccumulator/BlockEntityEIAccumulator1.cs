// запаска пример использования с иммерсивными проводами
using ElectricalProgressive.Utils;
using Vintagestory.API.Common;

namespace ElectricalProgressive.Content.Block.EAccumulator;

public class BlockEntityEIAccumulator1 : BlockEntityEIBase
{
    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);

        if (this.EPImmersive == null || byItemStack == null)
            return;

        //задаем электрические параметры блока/проводника
        LoadImmersiveEProperties.Load(this.Block, this);
    }
}
