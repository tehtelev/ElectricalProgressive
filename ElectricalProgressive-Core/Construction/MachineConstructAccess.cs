using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Construction;

/// <summary>
/// Доступ к MachineConstruct с любого блока multiblock (dummy → контроллер).
/// </summary>
public static class MachineConstructAccess
{
    /// <summary>
    /// Позиция контроллера: для meta-multiblock — через OffsetInv, иначе сам pos.
    /// </summary>
    public static BlockPos GetControllerPos(IWorldAccessor world, BlockPos pos)
    {
        var block = world.BlockAccessor.GetBlock(pos);
        if (block is BlockMultiblock mb)
            return pos.AddCopy(mb.OffsetInv);
        return pos;
    }

    /// <summary>
    /// Позиция контроллера с учётом offset от IMultiBlockInteract.
    /// </summary>
    public static BlockPos GetControllerPos(BlockPos clickedPos, Vec3i offsetInv)
    {
        return clickedPos.AddCopy(offsetInv);
    }

    public static BEBehaviorMachineConstruct? GetBehavior(IWorldAccessor world, BlockPos anyPosInStructure)
    {
        var controllerPos = GetControllerPos(world, anyPosInStructure);
        return world.BlockAccessor.GetBlockEntity(controllerPos)?.GetBehavior<BEBehaviorMachineConstruct>();
    }

    public static BEBehaviorMachineConstruct? GetBehavior(IWorldAccessor world, BlockPos clickedPos, Vec3i offsetInv)
    {
        var controllerPos = GetControllerPos(clickedPos, offsetInv);
        return world.BlockAccessor.GetBlockEntity(controllerPos)?.GetBehavior<BEBehaviorMachineConstruct>();
    }

    /// <summary>
    /// ПКМ-сборка: true если обработали (в т.ч. «уже готова» — false, чтобы GUI).
    /// </summary>
    public static bool TryConstructInteract(IWorldAccessor world, IPlayer byPlayer, BlockPos anyPosInStructure)
    {
        var beh = GetBehavior(world, anyPosInStructure);
        if (beh == null || !beh.HasConstruction || beh.IsReady)
            return false;

        beh.TryConstruct(byPlayer);
        return true;
    }

    public static bool TryConstructInteract(IWorldAccessor world, IPlayer byPlayer, BlockPos clickedPos,
        Vec3i offsetInv)
    {
        var beh = GetBehavior(world, clickedPos, offsetInv);
        if (beh == null || !beh.HasConstruction || beh.IsReady)
            return false;

        beh.TryConstruct(byPlayer);
        return true;
    }
}
