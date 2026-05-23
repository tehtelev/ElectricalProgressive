using ElectricalProgressive.RecipeSystem.Recipe;

namespace ElectricalProgressive.RecipeSystem.Recipe;

public class BlastFurnaceRecipe : BaseMultiRecipe<BlastFurnaceRecipe>
{
    protected override BlastFurnaceRecipe CreateInstance() => new();
}