using Cairo;
using ElectricalProgressive.RecipeSystem;
using ElectricalProgressive.RecipeSystem.Recipe;
using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
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

        // Точный поиск метода по имени и типам параметров
        var originalMethod = AccessTools.Method(
            typeof(CollectibleBehaviorHandbookTextAndExtraInfo),
            "GetHandbookInfo",
            new[] {
                typeof(ItemSlot),
                typeof(ICoreClientAPI),
                typeof(ItemStack[]),
                typeof(ActionConsumable<string>)
            }
        );

        if (originalMethod == null)
        {
            _capi.Logger.Error("[ElectricalProgressive] Could not find original GetHandbookInfo method. Skipping patch.");
            return;
        }

        var postfixMethod = AccessTools.Method(typeof(HandbookPageComposer), "AddRecipeInfoPostfix");


        // Задаём повышенный приоритет, чтобы постфикс выполнялся после аналогичных патчей других модов
        var postfix = new HarmonyMethod(postfixMethod) { priority = Priority.Last};
        harmony.Patch(originalMethod, postfix: postfix);

    }

    public static class HandbookPageComposer
    {
        private const float ItemSize = 40.0f;
        private const float LineSpacing = 14f;
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
                if (stack == null)
                    return;

                var components = new List<RichTextComponentBase>(__result);
                var haveText = components.Count > 0;

                // Секция «Сборка» — в Core (HandbookConstructionPatch), для Basics + Industry
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
            if (stack == null || stack.Collectible == null)
                return;

            if (ElectricalProgressiveRecipeManager.machines == null)
                return;

            var machine = ElectricalProgressiveRecipeManager.machines
                .Where(m => m.Key != null)
                .FirstOrDefault(m => stack.Collectible.Code.Path.StartsWith(m.Key));

            if (machine.Value != default)
            {
                if (machine.Value.recipes != null)
                {
                    AddMachineInfo(components, capi, machine.Value.code,
                        GetCachedTranslation("electricalprogressive:produced-in"),
                        openDetailPageFor,
                        ref haveText);
                    if (machine.Key == MetalFormingHandbookRecipes.MachineKey)
                        AddMetalFormingRecipes(components, capi, machine.Value.recipes, openDetailPageFor);
                    else
                        AddRecipes(components, capi, machine.Value.recipes, openDetailPageFor);
                }
            }
            else
            {
                foreach (var m in ElectricalProgressiveRecipeManager.machines)
                {
                    if (m.Value.recipes == null)
                        continue;

                    var relevantRecipes = m.Value.recipes
                        .Where(r => r != null && IsItemInRecipe(stack, r))
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

        private static void AddMetalFormingRecipes(
            List<RichTextComponentBase> components,
            ICoreClientAPI capi,
            IEnumerable<dynamic> recipes,
            ActionConsumable<string> openDetailPageFor)
        {
            var rows = new Dictionary<string, List<(ItemStack Ingot, ItemStack Output, double Energy)>>();
            foreach (dynamic recipe in recipes)
            {
                ItemStack ingot = null;
                ItemStack output = null;
                try
                {
                    if (recipe.Ingredients != null && recipe.Ingredients.Length > 0)
                        ingot = GetOrCreateStack((AssetLocation)recipe.Ingredients[0].Code,
                            (int)recipe.Ingredients[0].Quantity, capi.World);
                    if (recipe.Outputs != null && recipe.Outputs.Length > 0)
                        output = GetOrCreateStack((AssetLocation)recipe.Outputs[0].Code,
                            (int)recipe.Outputs[0].StackSize, capi.World);
                }
                catch
                {
                    continue;
                }

                if (ingot?.Collectible?.Code == null || output?.Collectible?.Code == null)
                    continue;

                var metal = ingot.Collectible.LastCodePart();
                var path = output.Collectible.Code.Path;
                if (!string.IsNullOrEmpty(metal) &&
                    path.EndsWith("-" + metal, StringComparison.OrdinalIgnoreCase))
                    path = path[..^(metal.Length + 1)];
                var key = output.Collectible.Code.Domain + ":" + path;
                if (!rows.TryGetValue(key, out var list))
                {
                    list = [];
                    rows[key] = list;
                }

                list.Add((ingot, output, (double)recipe.EnergyOperation));
            }

            components.Add(new ClearFloatTextComponent(capi, SmallPadding));
            foreach (var row in rows.OrderBy(r => r.Key))
            {
                var pairs = row.Value
                    .GroupBy(p => p.Ingot.Collectible.Code.ToString() + ">" + p.Output.Collectible.Code.ToString())
                    .Select(g => g.First())
                    .OrderBy(p => p.Ingot.Collectible.Code.ToString())
                    .ToList();
                if (pairs.Count == 0)
                    continue;

                components.Add(new ClearFloatTextComponent(capi, RecipeSpacing));
                var groupKey = "emf-" + row.Key;
                components.Add(new SyncedSlideshowItemstackTextComponent(
                    capi,
                    pairs.Select(p => p.Ingot).ToArray(),
                    40.0,
                    EnumFloat.Inline,
                    cs => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs)),
                    groupKey)
                {
                    ShowStackSize = true,
                    VerticalAlign = EnumVerticalAlign.Middle
                });
                components.Add(new RichTextComponent(capi, " → ",
                    CairoFont.WhiteMediumText().WithWeight(FontWeight.Bold))
                {
                    VerticalAlign = EnumVerticalAlign.Middle
                });
                components.Add(new SyncedSlideshowItemstackTextComponent(
                    capi,
                    pairs.Select(p => p.Output).ToArray(),
                    40.0,
                    EnumFloat.Inline,
                    cs => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs)),
                    groupKey)
                {
                    ShowStackSize = true,
                    VerticalAlign = EnumVerticalAlign.Middle
                });
                components.Add(new RichTextComponent(capi,
                    $"\n{GetCachedTranslation("electricalprogressive:energy-required")}: {pairs[0].Energy} {GetCachedTranslation("electricalprogressive:energy-unit")}",
                    CairoFont.WhiteSmallText()));
            }
        }

        private static void AddRecipes(
            List<RichTextComponentBase> components,
            ICoreClientAPI capi,
            IEnumerable<dynamic> recipes,
            ActionConsumable<string> openDetailPageFor)
        {
            components.Add(new ClearFloatTextComponent(capi, SmallPadding));

            var groupedRecipes = recipes
                .Select(r => (dynamic)r)
                .GroupBy(r => (string)r.Code)
                .ToList();

            foreach (var group in groupedRecipes)
            {
                var recipeList = group.ToList();
                if (recipeList.Count == 0 || recipeList[0].Code == "default_perish")
                    continue;

                components.Add(new ClearFloatTextComponent(capi, RecipeSpacing));

                if (recipeList.Count == 1)
                {
                    AddSingleRecipe(components, capi, recipeList[0], openDetailPageFor);
                    continue;
                }

                var groupKey = group.Key;

                // Собираем все ингредиенты и выходы для слайд-шоу
                var allIngredients = new List<List<ItemStack>>();
                var allOutputs = new List<List<ItemStack>>();

                foreach (var recipe in recipeList)
                {
                    var ingIndex = 0;
                    foreach (var ing in recipe.Ingredients)
                    {
                        if (ingIndex >= allIngredients.Count)
                            allIngredients.Add([]);

                        var qty = (int)ing.Quantity;
                        string[] variants = null;
                        try { variants = ing.AllowedVariants; } catch { /* dynamic */ }

                        if (variants != null && variants.Length > 0)
                        {
                            var domain = ((AssetLocation)ing.Code).Domain ?? "game";
                            foreach (var v in variants)
                            {
                                var resolvedVar = GetOrCreateStack(new AssetLocation(domain, v), qty, capi.World);
                                if (resolvedVar != null)
                                    allIngredients[ingIndex].Add(resolvedVar);
                            }
                        }
                        else
                        {
                            foreach (var resolved in GetMatchingStacks((AssetLocation)ing.Code, qty, capi.World))
                                allIngredients[ingIndex].Add(resolved);
                        }

                        ingIndex++;
                    }

                    // Обрабатываем выходы
                    var outputIndex = 0;
                    foreach (var output in recipe.Outputs)
                    {
                        var outputStack = GetOrCreateStack((AssetLocation)output.Code, output.StackSize, capi.World);
                        if (outputStack != null)
                        {
                            if (outputIndex >= allOutputs.Count)
                            {
                                allOutputs.Add([]);
                            }
                            allOutputs[outputIndex].Add(outputStack);
                            outputIndex++;
                        }
                    }
                }

                // Отображаем ингредиенты с слайд-шоу
                var firstIngredient = true;
                foreach (var ingredientOptions in allIngredients)
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

                    var uniqueOptions = ingredientOptions
                        .Where(s => s?.Collectible?.Code != null)
                        .GroupBy(s => s.Collectible.Code.ToString())
                        .Select(g => g.First())
                        .ToArray();
                    if (uniqueOptions.Length == 0)
                        continue;

                    var ingredientSlideShow = new SyncedSlideshowItemstackTextComponent(
                        capi,
                        uniqueOptions,
                        40.0,
                        EnumFloat.Inline,
                        (Action<ItemStack>)(cs => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs))),
                        groupKey
                    )
                    {
                        ShowStackSize = true,
                        VerticalAlign = EnumVerticalAlign.Middle
                    };
                    components.Add(ingredientSlideShow);

                    firstIngredient = false;
                }

                // Стрелка
                var arrow = new RichTextComponent(capi, " → ",
                    CairoFont.WhiteMediumText().WithWeight(FontWeight.Bold))
                {
                    VerticalAlign = EnumVerticalAlign.Middle
                };
                components.Add(arrow);

                // Отображаем выходы с слайд-шоу и шансами
                var firstOutput = true;
                for (int i = 0; i < allOutputs.Count; i++)
                {
                    if (!firstOutput)
                    {
                        var plus = new RichTextComponent(capi, " + ",
                            CairoFont.WhiteMediumText().WithWeight(FontWeight.Bold))
                        {
                            VerticalAlign = EnumVerticalAlign.Middle
                        };
                        components.Add(plus);
                    }

                    var outputSlideShow = new SyncedSlideshowItemstackTextComponent(
                        capi,
                        allOutputs[i].ToArray(),
                        40.0,
                        EnumFloat.Inline,
                        (Action<ItemStack>)(cs => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs))),
                        groupKey
                    )
                    {
                        ShowStackSize = true,
                        VerticalAlign = EnumVerticalAlign.Middle
                    };

                    components.Add(outputSlideShow);

                    // У рецептов одной группы разное число выходов (изделие и обрезки).
                    var chance = 1.0f;
                    var firstOutputs = recipeList[0].Outputs;
                    if (firstOutputs != null && i < firstOutputs.Length)
                        chance = firstOutputs[i].Chance;
                    if (chance < 1.0f)
                    {
                        components.Add(new RichTextComponent(capi,
                            $" ({GetCachedTranslation("electricalprogressive:chance")}: {(int)(chance * 100)}%)",
                            CairoFont.WhiteSmallText())
                        { VerticalAlign = EnumVerticalAlign.Middle });
                    }

                    firstOutput = false;
                }

                // Добавляем информацию об энергии
                components.Add(new RichTextComponent(capi,
                    $"\n{GetCachedTranslation("electricalprogressive:energy-required")}: {recipeList[0].EnergyOperation} {GetCachedTranslation("electricalprogressive:energy-unit")}",
                    CairoFont.WhiteSmallText()));
            }
        }

        private static void AddSingleRecipe(
            List<RichTextComponentBase> components,
            ICoreClientAPI capi,
            dynamic recipe,
            ActionConsumable<string> openDetailPageFor)
        {
            components.Add(new ClearFloatTextComponent(capi, RecipeSpacing));

            // Отображаем ингредиенты
            var firstIngredient = true;
            foreach (var ing in recipe.Ingredients)
            {
                var stacks = GetMatchingStacks((AssetLocation)ing.Code, (int)ing.Quantity, capi.World);
                if (stacks.Count == 0)
                    continue;

                if (!firstIngredient)
                {
                    var plus = new RichTextComponent(capi, "+ ",
                        CairoFont.WhiteMediumText().WithWeight(FontWeight.Bold))
                    {
                        VerticalAlign = EnumVerticalAlign.Middle
                    };
                    components.Add(plus);
                }

                if (stacks.Count == 1)
                    components.Add(CreateItemStackComponent(capi, stacks[0], openDetailPageFor));
                else
                {
                    components.Add(new SyncedSlideshowItemstackTextComponent(
                        capi,
                        stacks.ToArray(),
                        40.0,
                        EnumFloat.Inline,
                        (Action<ItemStack>)(cs => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs))),
                        (string)recipe.Code)
                    {
                        ShowStackSize = true,
                        VerticalAlign = EnumVerticalAlign.Middle
                    });
                }

                firstIngredient = false;
            }

            var arrow = new RichTextComponent(capi, "→ ",
                CairoFont.WhiteMediumText().WithWeight(FontWeight.Bold))
            {
                VerticalAlign = EnumVerticalAlign.Middle
            };
            components.Add(arrow);

            // Отображаем выходы с шансами
            var firstOutput = true;
            foreach (var output in recipe.Outputs)
            {
                if (!firstOutput)
                {
                    var plus = new RichTextComponent(capi, "+ ",
                        CairoFont.WhiteMediumText().WithWeight(FontWeight.Bold))
                    {
                        VerticalAlign = EnumVerticalAlign.Middle
                    };
                    components.Add(plus);
                }

                var outputStack = GetOrCreateStack((AssetLocation)output.Code, output.StackSize, capi.World);
                if (outputStack != null)
                {
                    components.Add(CreateItemStackComponent(capi, outputStack, openDetailPageFor));
                }

                // Добавляем шанс если он меньше 100%
                if (output.Chance < 1.0f)
                {
                    components.Add(new RichTextComponent(capi,
                        $"({GetCachedTranslation("electricalprogressive:chance")}: {(int)(output.Chance * 100)}%)",
                        CairoFont.WhiteSmallText())
                    {
                        VerticalAlign = EnumVerticalAlign.Middle
                    });
                }

                firstOutput = false;
            }

            components.Add(new RichTextComponent(capi,
                $"\n{GetCachedTranslation("electricalprogressive:energy-required")}: {recipe.EnergyOperation} {GetCachedTranslation("electricalprogressive:energy-unit")}",
                CairoFont.WhiteSmallText()));
        }

        private static ItemstackTextComponent CreateItemStackComponent(
            ICoreClientAPI capi,
            ItemStack stack,
            ActionConsumable<string> openDetailPageFor)
        {
            // Не просвечивать сквозь креатив / GUI поверх handbook
            var component = new global::ElectricalProgressive.Patch.HandbookItemstackTextComponent(
                capi, stack, ItemSize, 10.0, EnumFloat.Inline,
                onStackClicked: (cs) => openDetailPageFor(GuiHandbookItemStackPage.PageCodeForStack(cs)))
            {
                ShowStacksize = true,
                VerticalAlign = EnumVerticalAlign.Middle
            };
            return component;
        }

        private static List<ItemStack> GetMatchingStacks(AssetLocation code, int quantity, IWorldAccessor world)
        {
            var result = new List<ItemStack>();
            if (code == null)
                return result;

            var qty = Math.Max(quantity, 1);
            if (!code.Path.Contains('*'))
            {
                var one = ResolveExactStack(code, qty, world);
                if (one != null)
                    result.Add(one);
                return result;
            }

            foreach (var it in world.Items)
            {
                if (it?.Code == null || it.IsMissing)
                    continue;
                if (!WildcardUtil.Match(code, it.Code))
                    continue;
                result.Add(new ItemStack(it, qty));
            }

            foreach (var bl in world.Blocks)
            {
                if (bl?.Code == null || bl.IsMissing)
                    continue;
                if (!WildcardUtil.Match(code, bl.Code))
                    continue;
                result.Add(new ItemStack(bl, qty));
            }

            return result;
        }

        private static ItemStack GetOrCreateStack(AssetLocation code, int quantity, IWorldAccessor world)
        {
            var matches = GetMatchingStacks(code, quantity, world);
            return matches.Count > 0 ? matches[0] : null;
        }

        private static ItemStack ResolveExactStack(AssetLocation code, int quantity, IWorldAccessor world)
        {
            if (code == null)
                return null;

            var cacheKey = $"{code}-{quantity}";
            if (_stackCache.TryGetValue(cacheKey, out var cachedStack))
                return cachedStack;

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
            }
            catch (Exception ex)
            {
                _capi?.Logger.Error($"Error resolving item {code}: {ex}");
            }

            return null;
        }

        private static bool IsItemInRecipe(ItemStack stack, dynamic recipe)
        {
            if (stack == null || stack.Collectible == null || recipe == null)
                return false;

            // Проверяем ингредиенты
            foreach (var ing in recipe.Ingredients)
            {
                var resolved = GetOrCreateStack(ing.Code, (int)ing.Quantity, _capi.World);
                if (resolved != null && resolved.Collectible != null && resolved.Collectible.Code == stack.Collectible.Code)
                    return true;
            }

            // Проверяем все выходы
            foreach (var output in recipe.Outputs)
            {
                var outputStack = GetOrCreateStack(output.Code, output.StackSize, _capi.World);
                if (outputStack != null && outputStack.Collectible != null && outputStack.Collectible.Code == stack.Collectible.Code)
                    return true;
            }

            return false;
        }
    }

    public class SyncedSlideshowItemstackTextComponent : SlideshowItemstackTextComponent
    {
        private int CurItemIndex;

        private class GroupState
        {
            public int Index = 0;
            public double LastSwitchTime = 0;
            public int HoverCount = 0;
        }

        private static readonly Dictionary<string, GroupState> groups = new();
        private readonly string groupKey;
        private bool isHovered;

        public SyncedSlideshowItemstackTextComponent(
            ICoreClientAPI capi,
            ItemStack[] stacks,
            double size,
            EnumFloat floatType,
            Action<ItemStack> onStackClick,
            string groupKey
        ) : base(capi, stacks, size, floatType, onStackClick)
        {
            this.groupKey = groupKey;
            if (!groups.ContainsKey(groupKey))
            {
                groups[groupKey] = new GroupState();
            }
        }

        public override void RenderInteractiveElements(float deltaTime, double renderX, double renderY, double renderZ)
        {
            var state = groups[groupKey];
            var rect = this.BoundsPerLine[0];

            var x = (int)(this.api.Input.MouseX - renderX + this.renderOffset.X);
            var y = (int)(this.api.Input.MouseY - renderY + this.renderOffset.Y);
            var nowHovered = rect.PointInside(x, y);

            if (nowHovered && !isHovered)
            {
                state.HoverCount++;
                isHovered = true;
            }
            else if (!nowHovered && isHovered)
            {
                state.HoverCount--;
                isHovered = false;
            }

            if (state.HoverCount == 0 && api.World.ElapsedMilliseconds - state.LastSwitchTime > 1000)
            {
                state.Index++;
                state.LastSwitchTime = api.World.ElapsedMilliseconds;
            }

            CurItemIndex = state.Index % (this.Itemstacks.Length == 0 ? 1 : this.Itemstacks.Length);

            var itemStack = this.Itemstacks[CurItemIndex];
            if (this.overrideCurrentItemStack != null)
                itemStack = this.overrideCurrentItemStack();

            this.slot.Itemstack = itemStack;

            var bounds = ElementBounds.FixedSize(
                (int)(rect.Width / (double)RuntimeEnv.GUIScale),
                (int)(rect.Height / (double)RuntimeEnv.GUIScale)
            );
            bounds.ParentBounds = this.capi.Gui.WindowBounds;
            bounds.CalcWorldBounds();
            bounds.absFixedX = renderX + rect.X + this.renderOffset.X;
            bounds.absFixedY = renderY + rect.Y + this.renderOffset.Y;
            bounds.absInnerWidth *= this.renderSize / 0.58f;
            bounds.absInnerHeight *= this.renderSize / 0.58f;

            this.api.Render.PushScissor(bounds, true);

            if (this.slot.Itemstack != null && this.slot.Itemstack.Collectible != null && slot.Itemstack.Id != 0)
            {
                this.api.Render.RenderItemstackToGui(
                    this.slot,
                    renderX + rect.X + rect.Width * 0.5 + this.renderOffset.X + this.offX,
                    renderY + rect.Y + rect.Height * 0.5 + this.renderOffset.Y + this.offY,
                    100.0 + this.renderOffset.Z,
                    (float)rect.Width * this.renderSize,
                    -1,
                    showStackSize: this.ShowStackSize
                );
            }

            this.api.Render.PopScissor();

            if (nowHovered && this.ShowTooltip && this.slot != null && this.slot.Itemstack != null && this.slot.Itemstack.Id != 0)
            {
                this.RenderItemstackTooltip(this.slot, renderX + x, renderY + y, deltaTime);
            }
        }

        public override void Dispose()
        {
            groups?.Clear();
            _stackCache?.Clear(); // TODO: надо разобраться когда кеши очищать таки
            base.Dispose();
        }
    }
}