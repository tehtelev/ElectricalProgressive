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
    /// <summary>
    /// Патч для добавления информации о рецептах в справочник
    /// </summary>
    public class HandbookPatch
    {
        private static ICoreClientAPI _capi;

        /// <summary>
        /// Применяет Harmony-патчи к игре
        /// </summary>
        /// <param name="clientApi">API клиента</param>
        public static void ApplyPatches(ICoreClientAPI clientApi)
        {
            _capi = clientApi;
            var harmony = new Harmony("electricalprogressive.handbook.patches");
            harmony.Patch(
                typeof(CollectibleBehaviorHandbookTextAndExtraInfo).GetMethod("GetHandbookInfo"),
                postfix: new HarmonyMethod(typeof(HandbookPageComposer).GetMethod("AddRecipeInfoPostfix"))
            );
        }

        /// <summary>
        /// Класс для формирования страниц справочника
        /// </summary>
        public static class HandbookPageComposer
        {
            // Константы для форматирования
            private const float ItemSize = 40.0f;        // Размер иконок предметов
            private const float LineSpacing = 20f;       // Расстояние между строками
            private const float SmallPadding = 2f;       // Небольшой отступ
            private const float RecipeSpacing = 14f;     // Расстояние между рецептами

            /// <summary>
            /// Постфиксный метод для добавления информации о рецептах
            /// </summary>
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

                    // 1. Рецепты центрифуги
                    if (RecipeManager.CentrifugeRecipes != null)
                    {
                        var centrifugeRecipes = RecipeManager.CentrifugeRecipes
                            .Where(r => IsItemInRecipe(stack, r))
                            .ToList();
                        
                        if (centrifugeRecipes.Count > 0)
                        {
                            AddHeading(components, capi, "electricalprogressive:centrifuge-recipes", ref haveText);
                            AddRecipes(components, capi, centrifugeRecipes);
                        }
                    }

                    // 2. Рецепты молота
                    if (RecipeManager.HammerRecipes != null)
                    {
                        var hammerRecipes = RecipeManager.HammerRecipes
                            .Where(r => IsItemInRecipe(stack, r))
                            .ToList();
                        
                        if (hammerRecipes.Count > 0)
                        {
                            AddHeading(components, capi, "electricalprogressive:hammer-recipes", ref haveText);
                            AddRecipes(components, capi, hammerRecipes);
                        }
                    }

                    // 3. Рецепты пресса
                    if (RecipeManager.PressRecipes != null)
                    {
                        var pressRecipes = RecipeManager.PressRecipes
                            .Where(r => IsItemInRecipe(stack, r))
                            .ToList();
                        
                        if (pressRecipes.Count > 0)
                        {
                            AddHeading(components, capi, "electricalprogressive:press-recipes", ref haveText);
                            AddRecipes(components, capi, pressRecipes);
                        }
                    }

                    __result = components.ToArray();
                }
                catch (Exception ex)
                {
                    capi.Logger.Error($"Handbook error: {ex}");
                }
            }

            /// <summary>
            /// Добавляет группу рецептов в справочник
            /// </summary>
            /// <param name="components">Список компонентов страницы</param>
            /// <param name="capi">API клиента</param>
            /// <param name="recipes">Список рецептов</param>
            private static void AddRecipes(List<RichTextComponentBase> components, ICoreClientAPI capi, IEnumerable<dynamic> recipes)
            {
                components.Add(new ClearFloatTextComponent(capi, SmallPadding));

                foreach (var recipe in recipes)
                {
                    // Добавляем отступ между рецептами
                    if (components.Count > 0 && components.Last() is not ClearFloatTextComponent)
                    {
                        components.Add(new ClearFloatTextComponent(capi, RecipeSpacing));
                    }

                    // Ингредиенты
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

                    // Результат крафта
                    var outputStack = ResolveStack(recipe.Output.Code, (int)recipe.Output.Quantity, capi.World);
                    if (outputStack != null)
                    {
                        components.Add(CreateItemStackComponent(capi, outputStack));
                    }

                    // Информация о потребляемой энергии
                    components.Add(new RichTextComponent(capi, 
                        $"\n{Lang.Get("electricalprogressive:energy-required", recipe.EnergyOperation)}\n", 
                        CairoFont.WhiteSmallText()));
                }
            }

            /// <summary>
            /// Добавляет заголовок секции рецептов
            /// </summary>
            private static void AddHeading(List<RichTextComponentBase> components, ICoreClientAPI capi, string langCode, ref bool haveText)
            {
                if (haveText)
                    components.Add(new ClearFloatTextComponent(capi, LineSpacing));

                haveText = true;
                components.Add(new RichTextComponent(capi, $"{Lang.Get(langCode)}\n", 
                    CairoFont.WhiteSmallText().WithWeight(FontWeight.Bold)));
            }

            /// <summary>
            /// Создает компонент для отображения предмета
            /// </summary>
            private static ItemstackTextComponent CreateItemStackComponent(ICoreClientAPI capi, ItemStack stack)
            {
                return new ItemstackTextComponent(capi, stack, ItemSize, 10.0, EnumFloat.Inline, null)
                {
                    ShowStacksize = true,
                    VerticalAlign = EnumVerticalAlign.Middle
                };
            }

            /// <summary>
            /// Преобразует код предмета в ItemStack
            /// </summary>
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

            /// <summary>
            /// Проверяет, участвует ли предмет в рецепте
            /// </summary>
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

                // Проверяем результат крафта
                var outputStack = ResolveStack(recipe.Output.Code, (int)recipe.Output.Quantity, _capi.World);
                return outputStack != null && outputStack.Collectible.Code == stack.Collectible.Code;
            }
        }
    }
}