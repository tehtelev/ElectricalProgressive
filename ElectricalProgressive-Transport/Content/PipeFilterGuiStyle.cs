using System;
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

    /// <summary>
    /// Browser strip: inset + slot grid + scrollbar.
    /// Grid uses pure SlotGrid size (no FixedGrow) and is centered on the inset with equal padding.
    /// </summary>
    public readonly struct BrowserLayout
    {
        public double ContentWidth { get; init; }
        public double InsetWidth { get; init; }
        public double GridWidth { get; init; }
        public double GridHeight { get; init; }
        public double InsetHeight { get; init; }
        public ElementBounds InsetBounds { get; init; }
        public ElementBounds GridBounds { get; init; }
        public ElementBounds ScrollbarBounds { get; init; }
        /// <summary>Search field: same left/width as the inset (подложка).</summary>
        public ElementBounds SearchBounds { get; init; }
        /// <summary>Result count, same column as the scrollbar.</summary>
        public ElementBounds SearchResultsBounds { get; init; }
    }

    public static BrowserLayout MeasureBrowser(double left, double searchY, double browserY, int cols, int rows)
    {
        double pad = GuiElementItemSlotGridBase.unscaledSlotPadding;

        // Pure slot lattice size. FixedGrow is intentionally omitted: slots are origin-aligned
        // inside the element, so growing the box only creates empty space on the right/bottom.
        ElementBounds gridMeasure = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, cols, rows);
        double gridW = gridMeasure.fixedWidth;
        double gridH = gridMeasure.fixedHeight;

        double insetH = gridH + 2.0 * pad;
        double contentW = Math.Max(
            gridW + 2.0 * pad + ScrollGap + ScrollWidth,
            MinContentWidth);

        // Inset spans content up to the scrollbar; grid is centered inside with equal margins.
        double insetW = contentW - ScrollWidth - ScrollGap;
        double marginX = (insetW - gridW) / 2.0;
        double marginY = pad;
        double scrollLeft = left + contentW - ScrollWidth;

        ElementBounds insetBounds = ElementBounds.Fixed(left, browserY, insetW, insetH);
        ElementBounds gridBounds = ElementBounds.Fixed(left + marginX, browserY + marginY, gridW, gridH);
        ElementBounds scrollbarBounds = ElementBounds.Fixed(scrollLeft, browserY, ScrollWidth, insetH);

        // Search = inset width; count sits above the scrollbar (slightly wider so multi-digit fits).
        ElementBounds searchBounds = ElementBounds.Fixed(left, searchY, insetW, SearchHeight);
        const double countW = 40;
        double countLeft = scrollLeft + (ScrollWidth - countW) / 2.0 + 4;
        ElementBounds searchResultsBounds = ElementBounds.Fixed(
            countLeft, searchY + 4, countW, SearchHeight - 4);

        return new BrowserLayout
        {
            ContentWidth = contentW,
            InsetWidth = insetW,
            GridWidth = gridW,
            GridHeight = gridH,
            InsetHeight = insetH,
            InsetBounds = insetBounds,
            GridBounds = gridBounds,
            ScrollbarBounds = scrollbarBounds,
            SearchBounds = searchBounds,
            SearchResultsBounds = searchResultsBounds
        };
    }

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
