using Cairo;
using Vintagestory.API.Client;

namespace ElectricalProgressive.Content;

/// <summary>
/// Shared layout metrics and colors for pipe filter dialogs.
/// </summary>
public static class PipeFilterGuiStyle
{
    // Soft industrial palette (copper / warm gray on dark VS dialogs).
    public static readonly double[] TextPrimary = [0.90, 0.88, 0.84, 1.0];
    public static readonly double[] TextMuted = [0.62, 0.64, 0.66, 1.0];
    public static readonly double[] TextAccent = [0.86, 0.68, 0.38, 1.0];
    public static readonly double[] TextValue = [0.96, 0.94, 0.88, 1.0];
    public static readonly double[] TextOk = [0.55, 0.78, 0.52, 1.0];

    public const double OuterPad = 18;
    public const double RowGap = 8;
    public const double SectionGap = 14;
    public const double ScrollWidth = 18;
    public const double ScrollGap = 8;
    public const double ButtonHeight = 30;
    public const double ButtonGap = 10;
    public const double SearchHeight = 28;
    public const double LabelHeight = 20;
    public const double SwitchRowHeight = 28;
    public const double MinContentWidth = 360;

    /// <summary>Selected filter slot highlight (matches accent family).</summary>
    public const string SelectedSlotColor = "#6a9e4a";

    public static CairoFont HeaderFont()
        => CairoFont.WhiteSmallishText().WithWeight(FontWeight.Bold).WithColor(TextAccent);

    public static CairoFont LabelFont()
        => CairoFont.WhiteDetailText().WithColor(TextPrimary);

    public static CairoFont MutedFont()
        => CairoFont.WhiteDetailText().WithColor(TextMuted);

    public static CairoFont ValueFont(float size = 18)
        => CairoFont.WhiteDetailText().WithFontSize(size).WithWeight(FontWeight.Bold).WithColor(TextValue);

    public static CairoFont SearchFont()
        => CairoFont.WhiteSmallText().WithColor(TextPrimary);

    public static CairoFont CountFont()
        => CairoFont.WhiteDetailText().WithColor(TextOk);
}
