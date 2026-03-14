using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace ElectricalProgressive.RecipeSystem.Recipe;

public class DrawingRecipe : BaseMultiRecipe<DrawingRecipe>
{
    protected override DrawingRecipe CreateInstance() => new();
}