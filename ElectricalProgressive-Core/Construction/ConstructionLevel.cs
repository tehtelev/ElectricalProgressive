using Vintagestory.GameContent;

namespace ElectricalProgressive.Construction;

/// <summary>
/// Уровень сборки машины (blocktypes → attributes.construction.levels).
/// </summary>
public class ConstructionLevel
{
    /// <summary>Модель уровня (shapes path без shapes/ и .json).</summary>
    public string? Shape { get; set; }

    /// <summary>Lang-ключ подсказки / handbook.</summary>
    public string? ActionLangCode { get; set; }

    /// <summary>Ресурсы для перехода <b>на</b> этот уровень (ПКМ). У 0-го обычно пусто.</summary>
    public ConstructionIngredient[]? RequireStacks { get; set; }

    /// <summary>После уровня — сменить state на formed (и Multiblock, если прописан).</summary>
    public bool FormMachine { get; set; }

    public ConstructionStage ToConstructionStage()
    {
        return new ConstructionStage
        {
            ActionLangCode = string.IsNullOrEmpty(ActionLangCode) ? "rollers-construct" : ActionLangCode,
            // null ломает RightClickConstruction — всегда массив
            RequireStacks = RequireStacks ?? System.Array.Empty<ConstructionIngredient>()
        };
    }
}

