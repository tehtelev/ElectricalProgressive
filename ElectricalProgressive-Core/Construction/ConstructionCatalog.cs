using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Construction;

/// <summary>
/// Список машин с construction.levels и выбранная схема в книге.
/// </summary>
public static class ConstructionCatalog
{
    public const string AttrSelected = "selected";

    public static List<Block> ListSchematics(IWorldAccessor world)
    {
        var result = new List<Block>();
        var seen = new HashSet<string>();

        foreach (var block in world.Blocks)
        {
            if (block == null || block.Id == 0 || block.Code == null)
                continue;
            if (block.Code.Domain == "game")
                continue;
            if (!MachineConstructSystem.HasConstructionLevels(block))
                continue;

            if (block.Variant != null)
            {
                if (block.Variant.TryGetValue("state", out var state) && state != "incomplete")
                    continue;
                if (block.Variant.TryGetValue("side", out var side) && side != "north")
                    continue;
            }

            var key = block.Code.Domain + ":" + block.FirstCodePart();
            if (!seen.Add(key))
                continue;

            result.Add(block);
        }

        result.Sort((a, b) =>
            string.CompareOrdinal(new ItemStack(a).GetName(), new ItemStack(b).GetName()));
        return result;
    }

    public static Block? GetSelectedBlock(IWorldAccessor world, ItemStack? stack)
    {
        var code = stack?.Attributes?.GetString(AttrSelected);
        if (string.IsNullOrEmpty(code))
            return null;
        return world.GetBlock(new AssetLocation(code));
    }

    public static void SetSelected(ItemStack stack, string code)
    {
        stack.Attributes ??= new TreeAttribute();
        stack.Attributes.SetString(AttrSelected, code);
    }

    /// <summary>Блок-схема из книги или сам held-блок машины.</summary>
    public static Block? ResolveHeldConstructBlock(IWorldAccessor world, ItemStack? held)
    {
        if (held == null)
            return null;

        if (held.Item is ItemEConstructionBook)
            return GetSelectedBlock(world, held);

        if (held.Block != null &&
            MachineConstructSystem.HasConstructionLevels(held.Block) &&
            held.Block.Variant != null &&
            held.Block.Variant.TryGetValue("state", out var state) &&
            state == "incomplete")
            return held.Block;

        return null;
    }

    /// <summary>
    /// Клетка постановки: сам блок, если он заменяемый; иначе соседняя клетка по грани прицела.
    /// У предмета в руке DidOffset часто false — Position сидит на целевом блоке.
    /// </summary>
    public static BlockPos GetPlacePos(IWorldAccessor world, BlockSelection sel, Block toPlace)
    {
        var pos = sel.Position.Copy();
        var at = world.BlockAccessor.GetBlock(pos);
        if (at != null && at.IsReplacableBy(toPlace))
            return pos;

        if (sel.Face != null)
            return pos.AddCopy(sel.Face);

        return pos;
    }

    public static BlockSelection PreparePlaceSelection(IWorldAccessor world, BlockSelection blockSel, Block toPlace)
    {
        var sel = blockSel.Clone();
        var pos = sel.Position.Copy();
        var at = world.BlockAccessor.GetBlock(pos);
        if (at != null && at.Id != 0 && !at.IsReplacableBy(toPlace))
        {
            var face = sel.Face ?? BlockFacing.UP;
            pos = pos.AddCopy(face);
            sel.DidOffset = true;
        }

        sel.Position = pos;
        return sel;
    }

    public static Block ResolvePlaceBlock(IWorldAccessor world, IPlayer byPlayer, Block block, BlockSelection sel)
    {
        var stack = new ItemStack(block);
        var ho = block.GetBehavior<BlockBehaviorHorizontalOrientable>();
        if (ho != null)
        {
            var oriented = ho.GetLookAwareBlockVariant(byPlayer, stack, sel);
            if (oriented != null)
                block = oriented;
        }

        if (block.Variant == null)
            return block;

        var side = block.Variant.TryGetValue("side", out var s) && !string.IsNullOrEmpty(s) ? s : "north";
        if (block.Variant.ContainsKey("state") && block.Variant.ContainsKey("side"))
        {
            var inc = world.GetBlock(block.CodeWithVariants(["state", "side"], ["incomplete", side]));
            if (inc != null)
                return inc;
        }
        else if (block.Variant.ContainsKey("state"))
        {
            var inc = world.GetBlock(block.CodeWithVariant("state", "incomplete"));
            if (inc != null)
                return inc;
        }

        return block;
    }
}
