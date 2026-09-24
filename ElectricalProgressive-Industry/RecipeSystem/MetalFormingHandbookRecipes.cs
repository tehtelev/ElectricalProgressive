using System;
using System.Collections.Generic;
using ElectricalProgressive.Content.Block.EMetalForming;
using ElectricalProgressive.RecipeSystem.Recipe;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace ElectricalProgressive.RecipeSystem;

/// <summary>
/// Справочник металлоформовки: кузнечные рецепты, у которых материал — слиток.
/// </summary>
public static class MetalFormingHandbookRecipes
{
    public const string MachineKey = "emetalforming-";
    public const string MachineCode = "electricalprogressiveindustry:emetalforming-formed-north";
    private const int EnergyPerIngot = 5000;

    public static List<MetalFormingRecipe> Build(ICoreAPI api)
    {
        var result = new List<MetalFormingRecipe>();
        var smithing = api.GetSmithingRecipes();
        if (smithing == null)
            return result;

        foreach (var recipe in smithing)
        {
            if (!BlockEntityEMetalForming.IsIngotRecipe(recipe))
                continue;

            var output = recipe.Output?.ResolvedItemstack;
            var ingot = recipe.Ingredient?.ResolvedItemStack;
            if (output?.Collectible?.Code == null || ingot?.Collectible?.Code == null)
                continue;

            var voxelsPer = Math.Max(1, ingot.Collectible.GetCollectibleInterface<IAnvilWorkable>()
                ?.VoxelCountForHandbook(ingot) ?? BlockEntityEMetalForming.VoxelsPerIngot);
            var voxels = BlockEntityEMetalForming.CountRecipeVoxels(recipe);
            var needed = Math.Max(1, (int)Math.Ceiling(voxels / (double)voxelsPer));
            var leftover = Math.Max(0, needed * voxelsPer - voxels) * BlockEntityEMetalForming.BitsPerIngot
                           / BlockEntityEMetalForming.VoxelsPerIngot;

            var outputs = new List<RecipeOutput>
            {
                new()
                {
                    Type = output.Class == EnumItemClass.Block ? "block" : "item",
                    Code = output.Collectible.Code.Clone(),
                    StackSize = Math.Max(1, output.StackSize)
                }
            };

            if (leftover > 0)
            {
                var metal = ingot.Collectible.LastCodePart();
                outputs.Add(new RecipeOutput
                {
                    Type = "item",
                    Code = new AssetLocation(ingot.Collectible.Code.Domain, "metalbit-" + metal),
                    StackSize = leftover
                });
            }

            var product = BlockEntityEMetalForming.ProductKey(recipe);
            result.Add(new MetalFormingRecipe
            {
                Code = string.IsNullOrEmpty(product) ? output.Collectible.Code.Path : product,
                Enabled = true,
                Name = new AssetLocation("electricalprogressiveindustry", "emetalforming"),
                EnergyOperation = needed * EnergyPerIngot,
                Ingredients =
                [
                    new CraftingRecipeIngredient
                    {
                        Type = EnumItemClass.Item,
                        Code = ingot.Collectible.Code.Clone(),
                        Quantity = needed
                    }
                ],
                Outputs = outputs.ToArray()
            });
        }

        return result;
    }
}
