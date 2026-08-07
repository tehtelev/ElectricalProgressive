using Newtonsoft.Json;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Construction;

/// <summary>
/// Уровень сборки (attributes.construction.levels).
/// </summary>
public class ConstructionLevel
{
    /// <summary>Shape этапа (base / base2 / … / full). Последний — full blueprint.</summary>
    [JsonProperty("shape")]
    public string? Shape { get; set; }

    [JsonProperty("actionLangCode")]
    public string? ActionLangCode { get; set; }

    /// <summary>Ресурсы этапа. У 0-го (place) обычно пусто.</summary>
    [JsonProperty("requireStacks")]
    public ConstructionIngredient[]? RequireStacks { get; set; }

    /// <summary>После этапа → state=formed.</summary>
    [JsonProperty("formMachine")]
    public bool FormMachine { get; set; }

    public ConstructionStage ToConstructionStage()
    {
        return new ConstructionStage
        {
            ActionLangCode = string.IsNullOrEmpty(ActionLangCode) ? "rollers-construct" : ActionLangCode,
            RequireStacks = RequireStacks ?? System.Array.Empty<ConstructionIngredient>()
        };
    }
}
