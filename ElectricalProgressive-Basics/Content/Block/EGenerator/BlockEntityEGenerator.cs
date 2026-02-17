using ElectricalProgressive.Utils;

namespace ElectricalProgressive.Content.Block.EGenerator;

public class BlockEntityEGenerator : BlockEntityEFacingBase
{
    public override Facing GetConnection(Facing value)
    {
        return FacingHelper.FullFace(value);
    }
    


}
