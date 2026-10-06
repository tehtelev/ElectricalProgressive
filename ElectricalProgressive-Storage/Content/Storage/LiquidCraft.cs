using System;
using System.Collections.Generic;
using System.Globalization;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Storage;

/// <summary>
/// Жидкость из шаблона верстака. В сетке сборщика ёмкость не лежит: нужные литры берутся из сети.
/// </summary>
public static class LiquidCraft
{
    public static IPlayer? Player(IWorldAccessor world, BlockPos? near)
    {
        IPlayer? best = null;
        var bestDist = double.MaxValue;
        foreach (var player in world.AllOnlinePlayers)
        {
            if (player.Entity == null)
                continue;
            if (near == null)
                return player;

            var pos = player.Entity.Pos;
            var dx = pos.X - (near.X + 0.5);
            var dy = pos.Y - (near.Y + 0.5);
            var dz = pos.Z - (near.Z + 0.5);
            var dist = dx * dx + dy * dy + dz * dz;
            if (dist >= bestDist)
                continue;
            best = player;
            bestDist = dist;
        }

        return best;
    }

    public static bool[] Mask(IWorldAccessor world, ItemStack pattern, IPlayer? player)
    {
        var mask = new bool[9];
        foreach (var need in Collect(world, pattern, player))
            mask[need.Cell] = true;
        return mask;
    }

    /// <summary>
    /// На один крафт жидкость уже лежит в процессоре. Нет таких ячеек — тоже да.
    /// </summary>
    public static bool Available(IWorldAccessor world, BlockEntityEStorageProcessor? cpu, ItemStack pattern, IPlayer? player)
    {
        var needs = Collect(world, pattern, player);
        if (needs.Count == 0)
            return true;
        if (cpu == null)
            return false;

        for (var i = 0; i < needs.Count; i++)
        {
            var seen = false;
            for (var earlier = 0; earlier < i; earlier++)
            {
                if (!StorageAccess.Same(world, needs[i].Liquid, needs[earlier].Liquid))
                    continue;
                seen = true;
                break;
            }

            if (seen)
                continue;

            long want = 0;
            for (var k = 0; k < needs.Count; k++)
            {
                if (StorageAccess.Same(world, needs[i].Liquid, needs[k].Liquid))
                    want += needs[k].Portions;
            }

            if (cpu.CountOf(world, needs[i].Liquid) < want)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Пустые жидкостные ячейки получают временную ёмкость. Объём берётся из процессора.
    /// </summary>
    public static bool Fill(IWorldAccessor world, BlockEntityEStorageProcessor? cpu, BlockPos? network, ItemStack pattern, InventoryBase inventory, List<int> filled, IPlayer? player)
    {
        var needs = Collect(world, pattern, player);
        if (needs.Count == 0)
            return true;
        if (cpu == null)
            return false;

        var placed = new List<int>();
        foreach (var need in needs)
        {
            if (need.Cell < 0 || need.Cell >= inventory.Count)
            {
                ClearShells(world, cpu, network, inventory, placed);
                return false;
            }

            var slot = inventory[need.Cell];
            if (slot == null)
            {
                ClearShells(world, cpu, network, inventory, placed);
                return false;
            }

            if (!slot.Empty)
                continue;

            var got = cpu.Take(world, need.Liquid, need.Portions);
            if (got == null || got.StackSize < need.Portions)
            {
                if (got != null && got.StackSize > 0)
                    ReturnLiquid(world, cpu, network, got, got.StackSize);
                ClearShells(world, cpu, network, inventory, placed);
                return false;
            }

            var shell = Pour(world, pattern, need.Cell, got);
            if (shell == null)
            {
                ReturnLiquid(world, cpu, network, need.Liquid, need.Portions);
                ClearShells(world, cpu, network, inventory, placed);
                return false;
            }

            slot.Itemstack = shell;
            slot.MarkDirty();
            placed.Add(need.Cell);
        }

        filled.AddRange(placed);
        return true;
    }

    /// <summary>
    /// Остаток из временной ёмкости возвращается в процессор, сама ёмкость исчезает.
    /// </summary>
    public static void ClearShells(IWorldAccessor world, BlockEntityEStorageProcessor? cpu, BlockPos? network, InventoryBase inventory, List<int> filled)
    {
        foreach (var index in filled)
        {
            if (index < 0 || index >= inventory.Count)
                continue;

            var slot = inventory[index];
            if (slot?.Itemstack?.Collectible is BlockLiquidContainerBase container)
            {
                var content = container.GetContent(slot.Itemstack);
                if (content != null && content.StackSize > 0)
                    ReturnLiquid(world, cpu, network, content, content.StackSize);
            }

            if (slot == null)
                continue;
            slot.Itemstack = null;
            slot.MarkDirty();
        }
    }

    public static void ReturnLiquid(IWorldAccessor world, BlockEntityEStorageProcessor? cpu, BlockPos? network, ItemStack proto, int count)
    {
        var left = count;
        if (cpu != null && left > 0)
        {
            var bytes = ProcessorCluster.At(world, cpu.Pos).Bytes;
            var kept = cpu.Insert(world, proto, left, bytes);
            left -= kept;
        }

        if (left > 0 && network != null)
            StorageAccess.InsertLiquid(world, network, proto, left);
    }

    public static List<LiquidNeed> Collect(IWorldAccessor world, ItemStack pattern, IPlayer? player)
    {
        var list = new List<LiquidNeed>();
        if (player == null || ItemEStoragePattern.IsProcessing(pattern))
            return list;

        var slots = new ItemSlot[9];
        var cells = new ItemStack?[9];
        for (var i = 0; i < 9; i++)
        {
            var cell = ItemEStoragePattern.Cell(world, pattern, i);
            if (cell != null)
                cell.StackSize = 1;
            cells[i] = cell;
            slots[i] = new DummySlot(cell);
        }

        GridRecipe? matched = null;
        foreach (var recipe in world.GridRecipes)
        {
            if (!recipe.Enabled || recipe.Output?.ResolvedItemStack == null)
                continue;
            if (!recipe.Matches(player, world, slots, 3))
                continue;
            matched = recipe;
            break;
        }

        var ingredients = matched?.ResolvedIngredients;
        if (matched == null || ingredients == null || ingredients.Length == 0)
            return list;

        var used = new bool[ingredients.Length];
        for (var cell = 0; cell < 9; cell++)
        {
            var stack = cells[cell];
            if (stack?.Collectible is not BlockLiquidContainerBase container)
                continue;

            var content = container.GetContent(stack);
            if (content == null || content.StackSize <= 0)
                continue;
            if (content.Collectible == null && !content.ResolveBlockOrItem(world))
                continue;
            if (StorageAccess.LiquidProps(content) == null)
                continue;

            for (var n = 0; n < ingredients.Length; n++)
            {
                if (used[n])
                    continue;
                var ingredient = ingredients[n];
                if (ingredient == null)
                    continue;
                var attrs = ingredient.RecipeAttributes;
                if (attrs is not { Exists: true })
                    continue;
                var hasContent = attrs.KeyExists("requiresContent");
                var hasLitres = attrs.KeyExists("requiresLitres");
                if (!hasContent && !hasLitres)
                    continue;
                if (!container.MatchesForCrafting(stack, matched, ingredient))
                    continue;

                var litres = LitresOf(attrs, hasLitres, hasContent, container, stack);
                if (litres <= 0f)
                    break;

                var props = StorageAccess.LiquidProps(content);
                if (props == null || props.ItemsPerLitre <= 0)
                    break;

                var portions = (int)Math.Max(1, Math.Round(litres * props.ItemsPerLitre));
                var milli = (int)Math.Max(1, Math.Round(litres * 1000f));
                var liquid = content.Clone();
                liquid.StackSize = 1;
                used[n] = true;
                list.Add(new LiquidNeed(cell, liquid, portions, milli));
                break;
            }
        }

        return list;
    }

    public static string LitresText(int milli)
    {
        milli = Math.Max(0, milli);
        if (milli % 1000 == 0)
            return (milli / 1000).ToString(CultureInfo.InvariantCulture);
        return (milli / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static float LitresOf(JsonObject attrs, bool hasLitres, bool hasContent, BlockLiquidContainerBase container, ItemStack stack)
    {
        var litres = 0f;
        if (hasLitres)
        {
            var node = attrs["requiresLitres"];
            if (node != null)
                litres = node.AsFloat(0f);
        }

        if (litres <= 0f && hasContent)
            litres = container.GetCurrentLitres(stack);
        return litres;
    }

    private static ItemStack? Pour(IWorldAccessor world, ItemStack pattern, int cell, ItemStack got)
    {
        var sample = ItemEStoragePattern.Cell(world, pattern, cell);
        if (sample?.Collectible is not BlockLiquidContainerBase container)
            return null;

        var shell = sample.Clone();
        shell.StackSize = 1;
        var existing = container.GetContent(shell);
        if (existing != null && existing.StackSize > 0)
        {
            container.TryTakeContent(shell, existing.StackSize);
            var left = container.GetContent(shell);
            if (left != null && left.StackSize > 0)
                return null;
        }

        var props = StorageAccess.LiquidProps(got);
        if (props == null || props.ItemsPerLitre <= 0)
            return null;

        var before = got.StackSize;
        var put = container.TryPutLiquid(shell, got, before / props.ItemsPerLitre);
        if (put < before)
            return null;
        return shell;
    }
}

public sealed class LiquidNeed
{
    public int Cell { get; }
    public ItemStack Liquid { get; }
    public int Portions { get; }
    public int Milli { get; }

    public LiquidNeed(int cell, ItemStack liquid, int portions, int milli)
    {
        Cell = cell;
        Liquid = liquid;
        Portions = portions;
        Milli = milli;
    }
}
