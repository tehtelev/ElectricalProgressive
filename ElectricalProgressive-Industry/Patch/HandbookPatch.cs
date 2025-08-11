using Cairo;
using ElectricalProgressive.RicipeSystem;
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

public class HandbookPatch
{
    private static ICoreClientAPI _capi;
    private static readonly ConcurrentDictionary<string, ItemStack> _stackCache = new();

    public static void ApplyPatches(ICoreClientAPI clientApi)
    {
        _capi = clientApi;
        var harmony = new Harmony("electricalprogressive.handbook.patches");
        harmony.Patch(
            typeof(CollectibleBehaviorHandbookTextAndExtraInfo).GetMethod("GetHandbookInfo"),
            postfix: new HarmonyMethod(typeof(HandbookPageComposer).GetMethod("AddRecipeInfoPostfix"))
        );
    }

    public static class HandbookPageComposer
    {
        private const float ItemSize = 40.0f;
        private const float LineSpacing = 20f;
        private const float SmallPadding = 2f;
        private const float RecipeSpacing = 14f;

        public static void AddRecipeInfoPostfix(
            CollectibleBehaviorHandbookTextAndExtraInfo __instance,
            ItemSlot inSlot,
            ICoreClientAPI capi,
            ItemStack[] allStacks,
            ActionConsumable<string> openDetailPageFor,
            ref RichTextComponentBase[] __result)
        {
            try
            {
                var stack = inSlot.Itemstack;
                if (stack == null) return;

                var components = new List<RichTextComponentBase>(__result);
                bool haveText = components.Count > 0;

                CheckAndAddRecipes(components, capi, stack, openDetailPageFor, ref haveText);

                __result = components.ToArray();
            }
            catch (Exception ex)
            {
                capi.Logger.Error($"Handbook error: {ex}");
            }
        }

        private static void CheckAndAddRecipes(
            List<RichTextComponentBase> components, 
            ICoreClientAPI capi,
            ItemStack stack, 
            ActionConsumable<string> openDetailPageFor,
            ref bool haveText)
        {
            var machines = new Dictionary<string, (string code, IEnumerable<dynamic> recipes)>
            {
                { "ecentrifuge-", ("electricalprogressiveindustry:ecentrifuge-north", RecipeManager.CentrifugeRecipes) },
                { "ehammer-", ("electricalprogressiveindustry:ehammer-north", RecipeManager.HammerRecipes) },
                { "epress-", ("electricalprogressiveindustry:epress-north", RecipeManager.PressRecipes) }
            };

            var machine = machines.FirstOrDefault(m => stack.Collectible.Code.Path.StartsWith(m.Key));

            if (machine.Value != default)
            {
                if (machine.Value.recipes != null)
                {
                    AddMachineInfo(components, capi, machine.Value.code,
                        GetCachedTranslation("electricalprogressive:produced-in"), 
                        openDetailPageFor, 
                        ref haveText);
                    AddRecipes(components, capi, machine.Value.recipes, openDetailPageFor);
                }
            }
            else
            {
                foreach (var m in machines)
                {
                    if (m.Value.recipes == null) continue;

                    var relevantRecipes = m.Value.recipes
                        .Where(r => IsItemInRecipe(stack, r))
                        .ToList();

                    if (relevantRecipes.Count > 0)
                    {
                        AddMachineInfo(components, capi, m.Value.code,
                            GetCachedTranslation("electricalprogressive:produced-in"),
                            openDetailPageFor,
                            ref haveText);
                        AddRecipes(components, capi, relevantRecipes, openDetailPageFor);
                    }
                }
            }
        }

        private static string GetCachedTranslation(string key)
        {
            return Lang.Get(key);
        }

        private static void AddMachineInfo(
            List<RichTextComponentBase> components, 
            ICoreClientAPI capi,
            string machineCode, 
            string title,
            ActionConsumable<string> openDetailPageFor,
            ref bool haveText)
        {
            if (haveText)
                components.Add(new ClearFloatTextComponent(capi, LineSpacing));

            haveText = true;

            components.Add(new RichTextComponent(capi, title + " ",
                CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold))
            {
                VerticalAlign = EnumVerticalAlign.Middle, 
                Float = EnumFloat.Inline
            });

            var machineStack = GetOrCreateStack(machineCode, 1, capi.World);
            if (machineStack != null)
            {
                var machineIcon = CreateItemStackComponent(capi, machineStack, openDetailPageFor);
                machineIcon.VerticalAlign = EnumVerticalAlign.Middle;
                machineIcon.Float = EnumFloat.Inline;
                components.Add(machineIcon);
            }
        }

        private static void AddRecipes(
            List<RichTextComponentBase> components, 
            ICoreClientAPI capi,
            IEnumerable<dynamic> recipes,
            ActionConsumable<string> openDetailPageFor)
        {
            components.Add(new ClearFloatTextComponent(capi, SmallPadding));

            foreach (var recipe in recipes)
            {
                try
                {
                    if (components.Count > 0 && components.Last() is not ClearFloatTextComponent)
                    {
                        components.Add(new ClearFloatTextComponent(capi, RecipeSpacing));
                    }

                    bool firstIngredient = true;
                    foreach (var ing in recipe.Ingredients)
                    {
                        if (!firstIngredient)
                        {
                            var plus = new RichTextComponent(capi, "+ ",
                                CairoFont.WhiteMediumText().WithWeight(FontWeight.Bold))
                            {
                                VerticalAlign = EnumVerticalAlign.Middle
                            };
                            components.Add(plus);
                        }

                        var resolved = GetOrCreateStack(ing.Code, (int)ing.Quantity, capi.World);
                        if (resolved != null)
                        {
                            components.Add(CreateItemStackComponent(capi, resolved, openDetailPageFor));
                        }

                        firstIngredient = false;
                    }

                    var arrow = new RichTextComponent(capi, "→ ",
                        CairoFont.WhiteMediumText().WithWeight(FontWeight.Bold))
                    {
                        VerticalAlign = EnumVerticalAlign.Middle
                    };
                    components.Add(arrow);

                    var outputStack = GetOrCreateStack(recipe.Output.Code, (int)recipe.Output.Quantity, capi.World);
                    if (outputStack != null)
                    {
                        components.Add(CreateItemStackComponent(capi, outputStack, openDetailPageFor));
                    }

                    try
                    {
                        if (recipe.SecondaryOutput != null)
                        {
                            float chance = 0f;
                            try { chance = recipe.SecondaryOutputChance; }
                            catch { }

                            var chanceText = new RichTextComponent(capi, $"~{(int)(chance * 100)}%",
                                CairoFont.WhiteSmallText()) { VerticalAlign = EnumVerticalAlign.Middle };
                            components.Add(chanceText);

                            var secondaryOutputStack = GetOrCreateStack(recipe.SecondaryOutput.Code,
                                (int)recipe.SecondaryOutput.Quantity, capi.World);
                            if (secondaryOutputStack != null)
                            {
                                components.Add(CreateItemStackComponent(capi, secondaryOutputStack, openDetailPageFor));
                            }
                        }
                    }
                    catch
                    {
                        // Игнорируем ошибки вторичных продуктов
                    }

                    components.Add(new RichTextComponent(capi,
                        $"\n{GetCachedTranslation("electricalprogressive:energy-required")}: {recipe.EnergyOperation} {GetCachedTranslation("electricalprogressive:energy-unit")}\n",
                        CairoFont.WhiteSmallText()));
                }
                catch (Exception ex)
                {
                    capi.Logger.Error($"Error rendering recipe: {ex}");
                }
            }
        }

        private static ItemstackTextComponent CreateItemStackComponent(
            ICoreClientAPI capi, 
            ItemStack stack,
            ActionConsumable<string> openDetailPageFor)
        {
            var component = new ItemstackTextComponent(capi, stack, ItemSize, 10.0, EnumFloat.Inline, 
                onStackClicked: (cs) => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs)))
            {
                ShowStacksize = true,
                VerticalAlign = EnumVerticalAlign.Middle
            };
            return component;
        }

        private static ItemStack GetOrCreateStack(AssetLocation code, int quantity, IWorldAccessor world)
        {
            if (code == null) return null;

            string cacheKey = $"{code}-{quantity}";
            if (_stackCache.TryGetValue(cacheKey, out var cachedStack))
            {
                return cachedStack;
            }

            try
            {
                var item = world.GetItem(code);
                if (item != null)
                {
                    var stack = new ItemStack(item, quantity);
                    _stackCache.TryAdd(cacheKey, stack);
                    return stack;
                }

                var block = world.GetBlock(code);
                if (block != null)
                {
                    var stack = new ItemStack(block, quantity);
                    _stackCache.TryAdd(cacheKey, stack);
                    return stack;
                }

                return null;
            }
            catch (Exception ex)
            {
                _capi?.Logger.Error($"Error resolving item {code}: {ex}");
                return null;
            }
        }

        private static bool IsItemInRecipe(ItemStack stack, dynamic recipe)
        {
            if (stack == null || recipe == null) return false;

            foreach (var ing in recipe.Ingredients)
            {
                var resolved = GetOrCreateStack(ing.Code, (int)ing.Quantity, _capi.World);
                if (resolved != null && resolved.Collectible.Code == stack.Collectible.Code)
                    return true;
            }

            var outputStack = GetOrCreateStack(recipe.Output.Code, (int)recipe.Output.Quantity, _capi.World);
            return outputStack != null && outputStack.Collectible.Code == stack.Collectible.Code;
        }
    }
}