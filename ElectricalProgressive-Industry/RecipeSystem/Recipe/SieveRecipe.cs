using ElectricalProgressive.RecipeSystem.Recipe;

namespace ElectricalProgressive.RecipeSystem.Recipe;

public class SieveRecipe : BaseMultiRecipe<SieveRecipe>
{
    protected override SieveRecipe CreateInstance() => new();
}