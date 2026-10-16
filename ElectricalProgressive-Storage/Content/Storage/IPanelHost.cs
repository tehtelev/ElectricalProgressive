using ElectricalProgressive.Utils;

namespace ElectricalProgressive.Content.Storage;

/// <summary>
/// Кабель, который панель впитала: грань, жилы и код предмета.
/// </summary>
public interface IPanelHost
{
    Facing Facing { get; set; }
    Facing Cable { get; set; }
    int[] CableLines { get; }
    string CableCode { get; set; }
}
