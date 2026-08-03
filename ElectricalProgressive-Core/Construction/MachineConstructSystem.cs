using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Construction;

/// <summary>
/// Автоподключение MachineConstruct, если в blocktype есть attributes.construction.levels.
/// </summary>
public static class MachineConstructSystem
{
    public const string BeBehaviorName = "MachineConstruct";
    public const string BlockBehaviorName = "MachineConstruct";

    public static void Register(ICoreAPI api)
    {
        api.RegisterBlockEntityBehaviorClass(BeBehaviorName, typeof(BEBehaviorMachineConstruct));
        api.RegisterBlockBehaviorClass(BlockBehaviorName, typeof(BlockBehaviorMachineConstruct));
    }

    /// <summary>
    /// AssetsFinalize: внедрить behaviors во все блоки с construction.levels.
    /// </summary>
    public static void InjectIntoBlocks(ICoreAPI api)
    {
        var count = 0;
        foreach (var block in api.World.Blocks)
        {
            if (block == null || block.Id == 0)
                continue;

            if (!HasConstructionLevels(block))
                continue;

            // Не трогаем ваниль (meta-* и т.п. могут иметь чужой "construction")
            if (block.Code.Domain == "game")
                continue;

            if (string.IsNullOrEmpty(block.EntityClass))
            {
                api.Logger.Warning(
                    "[MachineConstruct] Block {0} has construction.levels but no entityClass — skipped",
                    block.Code);
                continue;
            }

            try
            {
                InjectBlockEntityBehavior(block);
                InjectBlockBehavior(block);
                count++;
            }
            catch (Exception ex)
            {
                api.Logger.Error("[MachineConstruct] Failed to inject into {0}: {1}", block.Code, ex);
            }
        }

        if (count > 0)
            api.Logger.Notification("[MachineConstruct] Auto-attached to {0} block type(s)", count);
    }

    public static bool HasConstructionLevels(Block block)
    {
        try
        {
            var levelsToken = block.Attributes?["construction"]?["levels"];
            if (levelsToken == null)
                return false;

            // JsonObject indexer often returns empty object instead of null — проверяем парсингом
            var levels = levelsToken.AsObject<ConstructionLevel[]>(null!);
            return levels is { Length: > 0 };
        }
        catch
        {
            return false;
        }
    }

    private static void InjectBlockEntityBehavior(Block block)
    {
        var list = block.BlockEntityBehaviors?.ToList() ?? new List<BlockEntityBehaviorType>();
        if (list.Any(b => b?.Name == BeBehaviorName))
            return;

        list.Add(new BlockEntityBehaviorType
        {
            Name = BeBehaviorName
        });
        block.BlockEntityBehaviors = list.ToArray();
    }

    private static void InjectBlockBehavior(Block block)
    {
        if (block.BlockBehaviors != null &&
            block.BlockBehaviors.Any(b => b is BlockBehaviorMachineConstruct))
            return;

        var bh = new BlockBehaviorMachineConstruct(block);
        // НЕ передавать null — CollectibleBehavior.Initialize падает с NRE
        bh.Initialize(new JsonObject("{}"));

        var list = block.BlockBehaviors?.ToList() ?? new List<BlockBehavior>();
        list.Insert(0, bh);
        block.BlockBehaviors = list.ToArray();

        var colList = block.CollectibleBehaviors?.ToList() ?? new List<CollectibleBehavior>();
        if (!colList.Any(b => b is BlockBehaviorMachineConstruct))
        {
            colList.Insert(0, bh);
            block.CollectibleBehaviors = colList.ToArray();
        }
    }
}
