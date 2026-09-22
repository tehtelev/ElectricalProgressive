using Cairo;
using ElectricalProgressive.Construction;
using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Patch;

/// <summary>
/// Handbook: секция «Сборка» для любого блока с attributes.construction.levels
/// (Basics fuel gen, Industry crusher/recycler, …) — без зависимости от Industry.
/// </summary>
public static class HandbookConstructionPatch
{
    private static ICoreClientAPI? _capi;
    private static readonly ConcurrentDictionary<string, ItemStack> StackCache = new();
    private static bool _applied;

    private const float ItemSize = 40f;
    private const float LineSpacing = 14f;
    private const float SmallPadding = 2f;
    private const float RecipeSpacing = 14f;

    public static void Apply(ICoreClientAPI capi)
    {
        if (_applied)
            return;

        _capi = capi;
        var original = AccessTools.Method(
            typeof(CollectibleBehaviorHandbookTextAndExtraInfo),
            "GetHandbookInfo",
            [
                typeof(ItemSlot),
                typeof(ICoreClientAPI),
                typeof(ItemStack[]),
                typeof(ActionConsumable<string>)
            ]);

        if (original == null)
        {
            capi.Logger.Error("[MachineConstruct] GetHandbookInfo not found — handbook assembly section skipped");
            return;
        }

        var harmony = new Harmony("electricalprogressive.handbook.construction");
        harmony.Patch(original, postfix: new HarmonyMethod(typeof(HandbookConstructionPatch),
            nameof(Postfix)) { priority = Priority.Last });
        _applied = true;
        capi.Logger.Notification("[MachineConstruct] Handbook construction section enabled");
    }

    public static void Postfix(
        CollectibleBehaviorHandbookTextAndExtraInfo __instance,
        ItemSlot inSlot,
        ICoreClientAPI capi,
        ItemStack[] allStacks,
        ActionConsumable<string> openDetailPageFor,
        ref RichTextComponentBase[] __result)
    {
        try
        {
            var stack = inSlot?.Itemstack;
            if (stack?.Block == null)
                return;

            var components = new List<RichTextComponentBase>(__result);
            RestrictAcidAccumLiquids(components, capi, stack, openDetailPageFor);

            var haveText = components.Count > 0;
            if (!AddConstructionInfo(components, capi, stack, openDetailPageFor, ref haveText))
            {
                __result = components.ToArray();
                return;
            }

            __result = components.ToArray();
        }
        catch (Exception ex)
        {
            capi.Logger.Error($"[MachineConstruct] Handbook construction: {ex}");
        }
    }

    private static void RestrictAcidAccumLiquids(
        List<RichTextComponentBase> components,
        ICoreClientAPI capi,
        ItemStack stack,
        ActionConsumable<string> openDetailPageFor)
    {
        if (stack.Block?.Code?.Path == null || !stack.Block.Code.Path.StartsWith("eacidaccum"))
            return;

        var acidItem = capi.World.GetItem(new AssetLocation("game", "acid-full-sulfuric"));
        if (acidItem == null)
            return;

        var storedIn = Lang.Get("handbook-storedin");
        var acidSlide = new SlideshowItemstackTextComponent(
            capi,
            [new ItemStack(acidItem)],
            40.0,
            EnumFloat.Inline,
            cs => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs)))
        {
            VerticalAlign = EnumVerticalAlign.Middle
        };

        var storedAt = -1;
        for (var i = 0; i < components.Count; i++)
        {
            var text = GetComponentText(components[i]);
            if (string.IsNullOrEmpty(text))
                continue;
            if (text.Contains(storedIn) || text.Contains("Может содержать") || text.Contains("Can hold"))
            {
                storedAt = i;
                break;
            }
        }

        if (storedAt >= 0)
        {
            var j = storedAt + 1;
            while (j < components.Count && IsHoldListComponent(components[j]))
                components.RemoveAt(j);
            components.Insert(j, acidSlide);
            return;
        }

        for (var i = 0; i < components.Count; i++)
        {
            if (!IsHoldListComponent(components[i]))
                continue;
            if (GetSlideshowCount(components[i]) <= 1)
                continue;
            components[i] = acidSlide;
        }
    }

    private static bool IsHoldListComponent(RichTextComponentBase c)
    {
        if (c is SlideshowItemstackTextComponent or ItemstackTextComponent)
            return true;
        var name = c.GetType().Name;
        return name.Contains("Slideshow", StringComparison.Ordinal) ||
               name.Contains("Itemstack", StringComparison.Ordinal);
    }

    private static int GetSlideshowCount(RichTextComponentBase c)
    {
        if (c is SlideshowItemstackTextComponent slide && slide.Itemstacks != null)
            return slide.Itemstacks.Length;

        foreach (var name in new[] { "Itemstacks", "itemstacks", "Stacks", "stacks" })
        {
            var p = c.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p?.GetValue(c) is ItemStack[] arr)
                return arr.Length;
            var f = c.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f?.GetValue(c) is ItemStack[] arr2)
                return arr2.Length;
        }

        return 0;
    }

    private static string? GetComponentText(RichTextComponentBase c)
    {
        foreach (var name in new[] { "DisplayText", "Text", "text", "ActualText" })
        {
            var p = c.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p?.GetValue(c) is string s && s.Length > 0)
                return s;
            var f = c.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f?.GetValue(c) is string s2 && s2.Length > 0)
                return s2;
        }

        return null;
    }

    private static bool AddConstructionInfo(
        List<RichTextComponentBase> components,
        ICoreClientAPI capi,
        ItemStack stack,
        ActionConsumable<string> openDetailPageFor,
        ref bool haveText)
    {
        var block = stack.Block;
        if (block?.Attributes?["construction"] == null)
            return false;

        var conf = block.Attributes["construction"];
        var levels = conf["levels"].AsObject<ConstructionLevel[]>(null!);
        if (levels == null || levels.Length == 0)
            return false;

        if (!levels.Any(l => l?.RequireStacks is { Length: > 0 }))
            return false;

        if (haveText)
            components.Add(new ClearFloatTextComponent(capi, LineSpacing));
        haveText = true;

        components.Add(new RichTextComponent(capi,
            Lang.Get("electricalprogressivecore:construction-handbook-assembly") + "\n",
            CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold)));

        components.Add(new RichTextComponent(capi,
            Lang.Get("electricalprogressivecore:construction-handbook-assembly-hint") + "\n",
            CairoFont.WhiteDetailText()));

        components.Add(new ClearFloatTextComponent(capi, RecipeSpacing));

        var bookItem = capi.World.GetItem(new AssetLocation("electricalprogressivecore", "econstructionbook"));
        if (bookItem != null)
        {
            components.Add(CreateItemStackComponent(capi, new ItemStack(bookItem), openDetailPageFor));
            components.Add(new RichTextComponent(capi,
                "  " + Lang.Get("electricalprogressivecore:construction-handbook-place-with-book") + "\n",
                CairoFont.WhiteSmallText())
            {
                VerticalAlign = EnumVerticalAlign.Middle,
                Float = EnumFloat.Inline
            });
            components.Add(new ClearFloatTextComponent(capi, SmallPadding));
        }

        for (var i = 0; i < levels.Length; i++)
        {
            var level = levels[i];
            if (level?.RequireStacks is not { Length: > 0 })
                continue;

            components.Add(new ClearFloatTextComponent(capi, RecipeSpacing));
            components.Add(new RichTextComponent(capi,
                Lang.Get("electricalprogressivecore:construction-handbook-step", i) + "\n",
                CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold)));

            AddIngredientIcons(components, capi, level.RequireStacks, openDetailPageFor);
            components.Add(new ClearFloatTextComponent(capi, SmallPadding));
        }

        components.Add(new ClearFloatTextComponent(capi, RecipeSpacing));
        components.Add(new RichTextComponent(capi, "→ ",
            CairoFont.WhiteMediumText().WithWeight(FontWeight.Bold))
        {
            VerticalAlign = EnumVerticalAlign.Middle,
            Float = EnumFloat.Inline
        });

        var formed = GetFormedStack(block, capi);
        if (formed != null)
            components.Add(CreateItemStackComponent(capi, formed, openDetailPageFor));

        components.Add(new RichTextComponent(capi,
            "  " + Lang.Get("electricalprogressivecore:construction-handbook-result") + "\n",
            CairoFont.WhiteSmallText())
        {
            VerticalAlign = EnumVerticalAlign.Middle,
            Float = EnumFloat.Inline
        });

        components.Add(new ClearFloatTextComponent(capi, LineSpacing));
        return true;
    }

    private static ItemStack? GetFormedStack(Block block, ICoreClientAPI capi)
    {
        if (block.Variant == null || !block.Variant.ContainsKey("state"))
            return new ItemStack(block);

        var side = block.Variant.ContainsKey("side") ? block.Variant["side"] : "north";
        foreach (var trySide in new[] { side, "south", "north" })
        {
            var code = block.CodeWithVariants(["state", "side"], ["formed", trySide]);
            var b = capi.World.GetBlock(code);
            if (b != null)
                return new ItemStack(b);
        }

        return null;
    }

    private static void AddIngredientIcons(
        List<RichTextComponentBase> components,
        ICoreClientAPI capi,
        ConstructionIngredient[] ingredients,
        ActionConsumable<string> openDetailPageFor)
    {
        var first = true;
        foreach (var ing in ingredients)
        {
            if (ing == null)
                continue;
            try
            {
                ItemStack? resolved = null;
                if (ing.Resolve(capi.World, "handbook construction"))
                    resolved = ing.ResolvedItemStack?.Clone();

                resolved ??= GetOrCreateStack(ing.Code, ing.Quantity, capi.World);
                if (resolved == null)
                    continue;

                resolved.StackSize = Math.Max(1, ing.Quantity);
                if (!first)
                    components.Add(PlusText(capi));
                components.Add(CreateItemStackComponent(capi, resolved, openDetailPageFor));
                first = false;
            }
            catch (Exception ex)
            {
                capi.Logger.Warning("[MachineConstruct] handbook ingredient: {0}", ex.Message);
            }
        }
    }

    private static RichTextComponent PlusText(ICoreClientAPI capi) =>
        new(capi, " + ", CairoFont.WhiteMediumText().WithWeight(FontWeight.Bold))
        {
            VerticalAlign = EnumVerticalAlign.Middle,
            Float = EnumFloat.Inline
        };

    private static ItemstackComponentBase CreateItemStackComponent(
        ICoreClientAPI capi,
        ItemStack stack,
        ActionConsumable<string> openDetailPageFor)
    {
        // HandbookItemstackTextComponent: не рисует 3D-иконки, если handbook не в фокусе
        // (иначе просвечивают сквозь креативный инвентарь / другие GUI поверх)
        return new HandbookItemstackTextComponent(capi, stack, ItemSize, 0,
            EnumFloat.Inline,
            cs => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs)))
        {
            ShowStacksize = true,
            VerticalAlign = EnumVerticalAlign.Middle
        };
    }

    private static ItemStack? GetOrCreateStack(AssetLocation? code, int quantity, IWorldAccessor world)
    {
        if (code == null)
            return null;

        var key = code.ToString() + "@" + quantity;
        if (StackCache.TryGetValue(key, out var cached))
            return cached.Clone();

        var block = world.GetBlock(code);
        if (block != null)
        {
            var s = new ItemStack(block, Math.Max(1, quantity));
            StackCache[key] = s;
            return s.Clone();
        }

        var item = world.GetItem(code);
        if (item != null)
        {
            var s = new ItemStack(item, Math.Max(1, quantity));
            StackCache[key] = s;
            return s.Clone();
        }

        return null;
    }
}
