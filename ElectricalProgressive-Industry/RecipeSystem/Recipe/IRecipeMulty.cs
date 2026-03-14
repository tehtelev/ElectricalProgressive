using Vintagestory.API.Common;

namespace ElectricalProgressive.RecipeSystem.Recipe
{
    public interface IRecipeMulty<T> : IRecipeMultyBase
    {
        T Clone();
    }
}