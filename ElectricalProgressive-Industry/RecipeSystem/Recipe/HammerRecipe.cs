using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Newtonsoft.Json;

namespace ElectricalProgressive.RecipeSystem.Recipe;

public class HammerRecipe : BaseMultiRecipe<HammerRecipe>
{
    protected override HammerRecipe CreateInstance() => new();
}
