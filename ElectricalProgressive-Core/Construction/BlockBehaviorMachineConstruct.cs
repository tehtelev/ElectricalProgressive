using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Construction;

/// <summary>
/// Блок-поведение: ПКМ / подсказки / дроп материалов для MachineConstruct.
/// Работает с любого блока multiblock (dummy → контроллер).
/// Важно: GetPlacedBlockInteractionHelp НЕ должен возвращать null —
/// base.GetPlacedBlockInteractionHelpCount делает .Length и падает с NRE.
/// </summary>
public class BlockBehaviorMachineConstruct : BlockBehavior
{
    public BlockBehaviorMachineConstruct(Block block) : base(block)
    {
    }

    private static BEBehaviorMachineConstruct? GetBeh(IWorldAccessor world, BlockPos pos)
    {
        return MachineConstructAccess.GetBehavior(world, pos);
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel,
        ref EnumHandling handling)
    {
        if (MachineConstructAccess.TryConstructInteract(world, byPlayer, blockSel.Position))
        {
            handling = EnumHandling.PreventSubsequent;
            return true;
        }

        return false;
    }

    public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection,
        IPlayer forPlayer, ref EnumHandling handling)
    {
        var beh = GetBeh(world, selection.Position);
        if (beh == null || !beh.HasConstruction || beh.IsReady)
            return Array.Empty<WorldInteraction>();

        var help = beh.GetInteractionHelp(world, forPlayer);
        if (help is { Length: > 0 })
        {
            handling = EnumHandling.PreventDefault;
            return help;
        }

        return Array.Empty<WorldInteraction>();
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer,
        ref float dropChanceMultiplier, ref EnumHandling handling)
    {
        var beh = GetBeh(world, pos);
        if (beh == null || !beh.HasConstruction)
            return null!;

        // Только материалы сборки, без самой машины (formed и incomplete).
        handling = EnumHandling.PreventSubsequent;

        if (byPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative)
            return [];

        var drops = new List<ItemStack>();
        foreach (var mat in beh.GetMaterialDrops())
        {
            if (mat is { StackSize: > 0 })
                drops.Add(mat);
        }

        return drops.ToArray();
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos, ref EnumHandling handling)
    {
        var beh = GetBeh(world, pos);
        if (beh == null || !beh.HasConstruction || beh.IsReady)
            return null!;

        handling = EnumHandling.PreventSubsequent;
        return null!;
    }
}
