using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage;

public class ItemEStorageCell : Vintagestory.API.Common.Item
{
    public override void InGuiIdle(IWorldAccessor world, ItemStack stack)
    {
        if (world is IClientWorldAccessor)
            GuiTransform.Rotation.Y = GameMath.Mod(world.ElapsedMilliseconds / 50f, 360f);
    }

    public override void OnGroundIdle(EntityItem entityItem)
    {
        if (entityItem.World is IClientWorldAccessor)
            GroundTransform.Rotation.Y = -GameMath.Mod(entityItem.World.ElapsedMilliseconds / 50f, 360f);
    }

    public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
    {
        if (byEntity.World is not IClientWorldAccessor)
            return;

        var angle = GameMath.Mod(-byEntity.World.ElapsedMilliseconds / 50f, 360f);
        FpHandTransform.Rotation.Y = angle;
        TpHandTransform.Rotation.Y = angle;
    }

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (inSlot.Itemstack == null)
            return;

        dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-cell-hint"));
        StorageAccess.DescribeCell(inSlot.Itemstack, world, dsc);
    }
}
