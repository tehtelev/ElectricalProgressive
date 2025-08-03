using ElectricalProgressive.Content.Block.ECable;
using ElectricalProgressive.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ESwitch;

public class BlockESwitch : BlockEBase
{
    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
        ItemStack byItemStack)
    {
        var selection = new Selection(blockSel);
        var face = FacingHelper.FromFace(selection.Face);

        if (
            !(world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityECable blockEntity &&   //есть ли там провод
              blockEntity.GetBehavior<BEBehaviorElectricalProgressive>() is { } electricity &&            //поведение єлектричества
              (blockEntity.Switches & face) == 0 && //на этой грани есть переключатель
              (electricity.Connection & face) != 0) //провода нет на этой грани
        )
        {
            return false;
        }

        blockEntity.Orientation = blockEntity.Orientation & ~face | selection.Facing; //в какую сторону повернут выключатель
        blockEntity.Switches = blockEntity.Switches & ~face | face;                 //какие направления грани он контролирует
        blockEntity.SwitchesState |= face;                                              //в какой грани занят выключатель
        blockEntity.MarkDirty(true);

        return true;
    }

    /// <inheritdoc />
    public override void OnEntityCollide(
        IWorldAccessor world,
        Entity entity,
        BlockPos pos,
        BlockFacing facing,
        Vec3d collideSpeed,
        bool isImpact
    )
    {

    }
}
