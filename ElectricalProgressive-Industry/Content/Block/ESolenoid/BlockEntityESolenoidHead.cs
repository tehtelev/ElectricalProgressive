using ElectricalProgressive.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ESolenoid;

public class BlockEntityESolenoidHead : BlockEntity
{
    public Facing Facing { get; set; } = Facing.None;
    public BlockPos BasePos { get; set; } = new(0, 0, 0);
    public bool HasBase { get; set; }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("baseX", BasePos.X);
        tree.SetInt("baseY", BasePos.Y);
        tree.SetInt("baseZ", BasePos.Z);
        tree.SetInt("facing", (int)Facing);
        tree.SetBool("hasBase", HasBase);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        BasePos = new BlockPos(tree.GetInt("baseX"), tree.GetInt("baseY"), tree.GetInt("baseZ"));
        Facing = (Facing)tree.GetInt("facing");
        HasBase = tree.GetBool("hasBase");
    }

    public override void OnBlockRemoved()
    {
        if (HasBase && Api?.World?.BlockAccessor.GetBlockEntity(BasePos) is BlockEntityESolenoid body)
            body.NotifyHeadGone();

        base.OnBlockRemoved();
    }
}
