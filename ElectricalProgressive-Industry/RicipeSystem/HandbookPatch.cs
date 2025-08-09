using Cairo;
using ElectricalProgressive.RicipeSystem;
using ElectricalProgressive.RicipeSystem.Recipe;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Patches
{
    public class HandbookPatch
    {
        private static ICoreClientAPI _capi;

        // Основной метод для применения патчей
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
            // Константы для оформления
            private const float ItemSize = 40.0f; // Размер иконок предметов
            private const float LineSpacing = 20f; // Расстояние между строками
            private const float SmallPadding = 2f; // Небольшой отступ
            private const float RecipeSpacing = 14f; // Расстояние между рецептами

            // Постфиксный метод, добавляющий информацию о рецептах в справочник
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

                    // Проверяем и добавляем рецепты для предмета
                    CheckAndAddRecipes(components, capi, stack, ref haveText);

                    __result = components.ToArray();
                }
                catch (Exception ex)
                {
                    capi.Logger.Error($"Handbook error: {ex}");
                }
            }

            // Проверяет рецепты и добавляет их в компоненты
            private static void CheckAndAddRecipes(List<RichTextComponentBase> components, ICoreClientAPI capi, ItemStack stack, ref bool haveText)
            {
                // Словарь с информацией о машинах и их рецептах
                var machines = new Dictionary<string, (string code, IEnumerable<dynamic> recipes)>()
                {
                    { "ecentrifuge-", ("electricalprogressiveindustry:ecentrifuge-north", RecipeManager.CentrifugeRecipes) },
                    { "ehammer-", ("electricalprogressiveindustry:ehammer-north", RecipeManager.HammerRecipes) },
                    { "epress-", ("electricalprogressiveindustry:epress-north", RecipeManager.PressRecipes) }
                };

                // Проверяем, является ли предмет машиной
                var machine = machines.FirstOrDefault(m => stack.Collectible.Code.Path.StartsWith(m.Key));

                if (machine.Value != default)
                {
                    // Если это машина - показываем все её рецепты
                    if (machine.Value.recipes != null)
                    {
                        AddMachineInfo(components, capi, machine.Value.code, Lang.Get("electricalprogressive:produced-in"), ref haveText);
                        AddRecipes(components, capi, machine.Value.recipes);
                    }
                }
                else
                {
                    // Если это не машина - показываем рецепты, где предмет участвует
                    foreach (var m in machines)
                    {
                        if (m.Value.recipes == null) continue;

                        var relevantRecipes = m.Value.recipes
                            .Where(r => IsItemInRecipe(stack, r))
                            .ToList();

                        if (relevantRecipes.Count > 0)
                        {
                            AddMachineInfo(components, capi, m.Value.code, Lang.Get("electricalprogressive:produced-in"), ref haveText);
                            AddRecipes(components, capi, relevantRecipes);
                        }
                    }
                }
            }

            // Добавляет информацию о машине
            private static void AddMachineInfo(List<RichTextComponentBase> components, ICoreClientAPI capi, string machineCode, string title, ref bool haveText)
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

                var machineStack = ResolveStack(new AssetLocation(machineCode), 1, capi.World);
                if (machineStack != null)
                {
                    var machineIcon = CreateItemStackComponent(capi, machineStack);
                    machineIcon.VerticalAlign = EnumVerticalAlign.Middle;
                    machineIcon.Float = EnumFloat.Inline;
                    components.Add(machineIcon);
                }
            }

            // Добавляет рецепты в компоненты
            private static void AddRecipes(List<RichTextComponentBase> components, ICoreClientAPI capi,
                IEnumerable<dynamic> recipes)
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

                        // Отображаем ингредиенты
                        foreach (var ing in recipe.Ingredients)
                        {
                            var resolved = ResolveStack(ing.Code, (int)ing.Quantity, capi.World);
                            if (resolved != null)
                            {
                                components.Add(CreateItemStackComponent(capi, resolved));
                            }
                        }

                        // Стрелка между ингредиентами и результатом
                        var arrow = new RichTextComponent(capi, "→ ",
                            CairoFont.WhiteMediumText().WithWeight(FontWeight.Bold))
                        {
                            VerticalAlign = EnumVerticalAlign.Top
                        };
                        components.Add(arrow);

                        // Основной результат
                        var outputStack = ResolveStack(recipe.Output.Code, (int)recipe.Output.Quantity, capi.World);
                        if (outputStack != null)
                        {
                            components.Add(CreateItemStackComponent(capi, outputStack));
                        }

                        // Пробуем получить побочный продукт (если есть)
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

                                var secondaryOutputStack = ResolveStack(recipe.SecondaryOutput.Code,
                                    (int)recipe.SecondaryOutput.Quantity, capi.World);
                                if (secondaryOutputStack != null)
                                {
                                    components.Add(CreateItemStackComponent(capi, secondaryOutputStack));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            capi.Logger.Debug($"No secondary output in this recipe type: {ex.Message}");
                        }

                        // Энергия
                        components.Add(new RichTextComponent(capi,
                            $"\n{Lang.Get("electricalprogressive:energy-required", recipe.EnergyOperation)}\n",
                            CairoFont.WhiteSmallText()));
                    }
                    catch (Exception ex)
                    {
                        capi.Logger.Error($"Error rendering recipe: {ex}");
                    }
                }
            }

            // Создает компонент для отображения предмета
            private static ItemstackTextComponent CreateItemStackComponent(ICoreClientAPI capi, ItemStack stack)
            {
                return new ItemstackTextComponent(capi, stack, ItemSize, 10.0, EnumFloat.Inline, null)
                {
                    ShowStacksize = true,
                    VerticalAlign = EnumVerticalAlign.Middle
                };
            }

            // Преобразует AssetLocation в ItemStack
            private static ItemStack ResolveStack(AssetLocation code, int quantity, IWorldAccessor world)
            {
                try
                {
                    if (code == null) return null;

                    var item = world.GetItem(code);
                    if (item != null) return new ItemStack(item, quantity);

                    var block = world.GetBlock(code);
                    if (block != null) return new ItemStack(block, quantity);

                    return null;
                }
                catch (Exception ex)
                {
                    _capi?.Logger.Error($"Error resolving item {code}: {ex}");
                    return null;
                }
            }

            // Проверяет, участвует ли предмет в рецепте
            private static bool IsItemInRecipe(ItemStack stack, dynamic recipe)
            {
                if (stack == null || recipe == null) return false;

                // Проверяем ингредиенты
                foreach (var ing in recipe.Ingredients)
                {
                    var resolved = ResolveStack(ing.Code, (int)ing.Quantity, _capi.World);
                    if (resolved != null && resolved.Collectible.Code == stack.Collectible.Code)
                        return true;
                }

                // Проверяем результат
                var outputStack = ResolveStack(recipe.Output.Code, (int)recipe.Output.Quantity, _capi.World);
                return outputStack != null && outputStack.Collectible.Code == stack.Collectible.Code;
            }
        }
    }
}

