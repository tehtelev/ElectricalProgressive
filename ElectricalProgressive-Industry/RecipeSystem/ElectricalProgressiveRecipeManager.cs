using ElectricalProgressive.RecipeSystem.Recipe;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace ElectricalProgressive.RecipeSystem;

public class ElectricalProgressiveRecipeManager : ModSystem
{
    public static List<CentrifugeRecipe> CentrifugeRecipes;
    public static List<HammerRecipe> HammerRecipes;
    public static List<PressRecipe> PressRecipes;
    public static List<DrawingRecipe> DrawingRecipes;


    public static Dictionary<string, (string code, IEnumerable<IRecipeMultyBase> recipes)> machines;

    private ICoreServerAPI api;

    public override void StartServerSide(ICoreServerAPI api)
    {
        this.api = api;

        api.Event.SaveGameLoaded += LoadCentrifugeRecipes;
        api.Event.SaveGameLoaded += LoadHammerRecipes;
        api.Event.SaveGameLoaded += LoadPressRecipes;
        api.Event.SaveGameLoaded += LoadDrawingRecipes;

        machines = new Dictionary<string, (string, IEnumerable<IRecipeMultyBase>)>(4);
    }

    private void LoadCentrifugeRecipes()
    {
        CentrifugeRecipes = [];
        LoadRecipes<CentrifugeRecipe>("Centrifuge Recipe", "recipes/electric/centrifugerecipe", CentrifugeRecipes.Add);
        api.World.Logger.Debug(Lang.Get("electricalprogressiveindustry:recipeloading"));
        machines.Add("ecentrifuge-", ("electricalprogressiveindustry:ecentrifuge-north", CentrifugeRecipes));
    }

    private void LoadHammerRecipes()
    {
        HammerRecipes = [];
        LoadRecipes<HammerRecipe>("Hammer Recipe", "recipes/electric/hammerrecipe", HammerRecipes.Add);
        api.World.Logger.Debug(Lang.Get("electricalprogressiveindustry:recipeloading"));
        machines.Add("ehammer-", ("electricalprogressiveindustry:ehammer-north", HammerRecipes));
    }

    private void LoadPressRecipes()
    {
        PressRecipes = [];
        LoadRecipes<PressRecipe>("Press Recipe", "recipes/electric/pressrecipe", PressRecipes.Add);
        api.World.Logger.Debug(Lang.Get("electricalprogressiveindustry:recipeloading"));
        machines.Add("epress-", ("electricalprogressiveindustry:epress-north", PressRecipes));
    }

    private void LoadDrawingRecipes()
    {
        DrawingRecipes = [];
        LoadRecipes<DrawingRecipe>("Drawing Recipe", "recipes/electric/drawingrecipe", DrawingRecipes.Add);
        api.World.Logger.Debug(Lang.Get("electricalprogressiveindustry:recipeloading"));
        machines.Add("edrawing-", ("electricalprogressiveindustry:edrawing-north", DrawingRecipes));
    }

    private void LoadRecipes<T>(string name, string path, Action<T> registerMethod)
        where T : class, IRecipeMulty<T>
    {
        var assets = api.Assets.GetMany<JToken>(api.Server.Logger, path);
        var quantityRegistered = 0;
        var quantityIgnored = 0;

        foreach (var kvp in assets)
        {
            if (kvp.Value is JObject)
            {
                LoadGenericRecipe(name, kvp.Key, kvp.Value.ToObject<T>(kvp.Key.Domain), registerMethod, ref quantityRegistered, ref quantityIgnored);
            }
            else if (kvp.Value is JArray array)
            {
                foreach (var token in array)
                    LoadGenericRecipe(name, kvp.Key, token.ToObject<T>(kvp.Key.Domain), registerMethod, ref quantityRegistered, ref quantityIgnored);
            }
        }

        api.World.Logger.Event(
            "{0} {1}s loaded{2}",
            quantityRegistered,
            name,
            quantityIgnored > 0 ? $" ({quantityIgnored} could not be resolved)" : ""
        );
    }

    private void LoadGenericRecipe<T>(
        string className,
        AssetLocation path,
        T recipe,
        Action<T> registerMethod,
        ref int quantityRegistered,
        ref int quantityIgnored)
        where T : class, IRecipeMulty<T>
    {
        if (!recipe.Enabled)
            return;

        if (recipe.Name == null)
            recipe.Name = path;

        var nameToCodeMapping = recipe.GetNameToCodeMapping(api.World);

        if (nameToCodeMapping.Count > 0)
        {
            List<T> expanded = [];

            var num = 1;
            var first = true;
            foreach (var kvp in nameToCodeMapping)
            {
                num = first ? kvp.Value.Length : num * kvp.Value.Length;
                first = false;
            }

            var firstKey = true;
            foreach (var kvp in nameToCodeMapping)
            {
                var key = kvp.Key;
                var variants = kvp.Value;

                for (var index = 0; index < num; index++)
                {
                    T entry;
                    if (firstKey)
                    {
                        expanded.Add(entry = (T)recipe.Clone());
                    }
                    else
                    {
                        entry = expanded[index];
                    }

                    var variant = variants[index % variants.Length];

                    if (entry.Ingredients != null)
                    {
                        foreach (var ingredient in entry.Ingredients)
                        {
                            if (ingredient.Name == key)
                                ingredient.Code = ingredient.Code.CopyWithPath(ingredient.Code.Path.Replace("*", variant));
                        }
                    }

                    if (entry.Outputs != null)
                    {
                        foreach (var output in entry.Outputs)
                            output.FillPlaceHolder(key, variant);
                    }
                }

                firstKey = false;
            }

            if (expanded.Count == 0)
            {
                api.World.Logger.Warning(
                    "{1} file {0} makes use of wildcards, but no blocks or items matching those wildcards were found.",
                    path, className
                );
            }

            foreach (var entry in expanded)
            {
                if (!entry.Resolve(api.World, $"{className} {path}"))
                    quantityIgnored++;
                else
                {
                    registerMethod(entry);
                    quantityRegistered++;
                }
            }
        }
        else
        {
            if (!recipe.Resolve(api.World, $"{className} {path}"))
                quantityIgnored++;
            else
            {
                registerMethod(recipe);
                quantityRegistered++;
            }
        }
    }

    public override void Dispose()
    {
        base.Dispose();

        CentrifugeRecipes?.Clear();
        HammerRecipes?.Clear();
        PressRecipes?.Clear();
        DrawingRecipes?.Clear();
        machines?.Clear();

        CentrifugeRecipes = null;
        HammerRecipes = null;
        PressRecipes = null;
        DrawingRecipes = null;
        machines = null;


    }
}