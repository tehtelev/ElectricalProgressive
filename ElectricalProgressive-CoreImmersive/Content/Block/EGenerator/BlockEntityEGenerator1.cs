using ElectricalProgressive.Content.Block;
using ElectricalProgressive.Utils;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace EPImmersive.Content.Block.EGenerator;

public class BlockEntityEGenerator1 : BlockEntityEIBase
{
    public Facing GetConnection(Facing value)
    {
        return FacingHelper.FullFace(value);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        tree.SetBytes(FacingKey, SerializerUtil.Serialize(this.Facing));
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        try
        {
            this.Facing = SerializerUtil.Deserialize<Facing>(tree.GetBytes(FacingKey));
        }
        catch (Exception exception)
        {
            this.Api?.Logger.Error(exception.ToString());
        }
    }



}
