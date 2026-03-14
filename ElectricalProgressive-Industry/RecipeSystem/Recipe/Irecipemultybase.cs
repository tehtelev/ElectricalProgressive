using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ElectricalProgressive.RecipeSystem.Recipe
{
    public interface IRecipeMultyBase
    {
        AssetLocation Name { get; set; }
        bool Enabled { get; set; }
        IRecipeIngredient[] Ingredients { get; }
        IRecipeOutput[] Outputs { get; }
        Dictionary<string, string[]> GetNameToCodeMapping(IWorldAccessor world);
        bool Resolve(IWorldAccessor world, string sourceForErrorLogging);
    }
}