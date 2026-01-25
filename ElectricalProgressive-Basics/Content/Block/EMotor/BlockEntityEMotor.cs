using ElectricalProgressive.Utils;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace ElectricalProgressive.Content.Block.EMotor;

public class BlockEntityEMotor : BlockEntityEFacingBase
{
    public override Facing GetConnection(Facing value)
    {
        return FacingHelper.FullFace(value);
    }

   
}
