using ElectricalProgressive.RecipeSystem.Recipe;

namespace ElectricalProgressive.RecipeSystem.Recipe;

public class CrusherRecipe : BaseMultiRecipe<CrusherRecipe>
{
    protected override CrusherRecipe CreateInstance() => new();
}