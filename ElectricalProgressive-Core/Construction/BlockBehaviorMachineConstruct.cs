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
            return null!; // null = «не перехватываем», пусть идут дефолтные дропы

        // PreventSubsequent (не PreventDefault!): HorizontalOrientable по умолчанию
        // handleDrop=true и тоже кладёт блок в дроп → иначе 2 контроллера.
        handling = EnumHandling.PreventSubsequent;
        var drops = new List<ItemStack>();

        // В креативе мир обычно и так не спавнит дропы; материалы не отдаём
        var creative = byPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative;

        var controller = beh.GetIncompleteControllerStack();
        if (controller != null)
            drops.Add(controller);

        if (!creative)
        {
            foreach (var mat in beh.GetMaterialDrops())
            {
                if (mat is { StackSize: > 0 })
                    drops.Add(mat);
            }
        }

        return drops.ToArray();
    }

    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos, ref EnumHandling handling)
    {
        var beh = GetBeh(world, pos);
        if (beh == null || !beh.HasConstruction)
            return null!;

        var stack = beh.GetIncompleteControllerStack();
        if (stack != null)
        {
            // Тоже обрываем цепочку — HO.OnPickBlock иначе подменит стейк
            handling = EnumHandling.PreventSubsequent;
            return stack;
        }

        return null!;
    }
}
