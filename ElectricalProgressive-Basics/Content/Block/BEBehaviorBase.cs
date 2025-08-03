using Vintagestory.API.Common;

namespace ElectricalProgressive.Content.Block;

public abstract class BEBehaviorBase : BlockEntityBehavior
{
    //public bool IsBurned => this.Block.Variant["state"] == "burned";
    public bool IsBurned => this.Block.Code.GetName().Contains("burned"); // пока так 
    protected BEBehaviorBase(BlockEntity blockentity) : base(blockentity)
    {
    }
}