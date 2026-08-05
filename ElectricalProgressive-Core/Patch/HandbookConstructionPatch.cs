using Cairo;
using ElectricalProgressive.Construction;
using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
            var haveText = components.Count > 0;
            if (!AddConstructionInfo(components, capi, stack, openDetailPageFor, ref haveText))
                return;

            __result = components.ToArray();
        }
        catch (Exception ex)
        {
            capi.Logger.Error($"[MachineConstruct] Handbook construction: {ex}");
        }
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

        components.Add(new ClearFloatTextComponent(capi, SmallPadding));

        for (var i = 0; i < levels.Length; i++)
        {
            var level = levels[i];
            if (level == null)
                continue;

            // Только «Шаг N» — материалы видны на иконках requireStacks, без дублирования списком
            components.Add(new ClearFloatTextComponent(capi, RecipeSpacing));
            components.Add(new RichTextComponent(capi,
                Lang.Get("electricalprogressivecore:construction-handbook-step", i) + "\n",
                CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold)));

            if (level.RequireStacks == null || level.RequireStacks.Length == 0)
            {
                if (i == 0)
                {
                    var controller = GetIncompleteStack(block, capi) ?? stack.Clone();
                    components.Add(CreateItemStackComponent(capi, controller, openDetailPageFor));
                }

                continue;
            }

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

    private static ItemStack? GetIncompleteStack(Block block, ICoreClientAPI capi)
    {
        if (block.Variant == null || !block.Variant.ContainsKey("state"))
            return new ItemStack(block);

        var side = block.Variant.ContainsKey("side") ? block.Variant["side"] : "north";
        foreach (var trySide in new[] { side, "south", "north" })
        {
            var code = block.CodeWithVariants(["state", "side"], ["incomplete", trySide]);
            var b = capi.World.GetBlock(code);
            if (b != null)
                return new ItemStack(b);
        }

        return null;
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
