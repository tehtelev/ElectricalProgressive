using ElectricalProgressive.Utils;

namespace ElectricalProgressive.Content.Block.EMotor;

public class BlockEntityEMotor : BlockEntityEFacingBase
{
    public override Facing GetConnection(Facing value)
    {
        return FacingHelper.FullFace(value);
    }

   
}
