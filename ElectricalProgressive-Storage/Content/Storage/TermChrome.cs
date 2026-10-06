using System;
using Cairo;
using Vintagestory.API.Client;

namespace ElectricalProgressive.Content.Storage;

/// <summary>
/// Фаски панелей терминала: выпуклое дерево с латунной кромкой и утопленные колодцы.
/// </summary>
internal static class TermChrome
{
    public static void Raised(Context ctx, double x, double y, double w, double h, bool pressed)
    {
        if (w < 2 || h < 2)
            return;

        var top = pressed ? 0.16 : 0.38;
        var bot = pressed ? 0.08 : 0.13;
        FillVertical(ctx, x, y, w, h, top, top * 0.72, top * 0.42, bot, bot * 0.68, bot * 0.38);
        Edge(ctx, x, y, w, 1, pressed ? 0.22 : 0.86, pressed ? 0.16 : 0.70, pressed ? 0.09 : 0.40, pressed ? 0.35 : 0.95);
        Edge(ctx, x, y + h - 1, w, 1, 0.05, 0.03, 0.02, pressed ? 0.35 : 0.8);
        Edge(ctx, x, y, 1, h, pressed ? 0.10 : 0.55, pressed ? 0.07 : 0.42, pressed ? 0.04 : 0.24, 0.9);
        Edge(ctx, x + w - 1, y, 1, h, 0.05, 0.03, 0.02, 0.75);
        if (w > 8 && h > 8)
        {
            ctx.SetSourceRGBA(0.62, 0.46, 0.24, pressed ? 0.4 : 0.75);
            ctx.LineWidth = 1;
            ctx.Rectangle(x + 2.5, y + 2.5, w - 5, h - 5);
            ctx.Stroke();
        }
    }

    public static void Inset(Context ctx, double x, double y, double w, double h, double r, double g, double b)
    {
        if (w < 2 || h < 2)
            return;

        FillVertical(ctx, x, y, w, h, r * 0.45, g * 0.45, b * 0.45, r, g, b);
        Edge(ctx, x, y, w, 1, 0, 0, 0, 0.72);
        Edge(ctx, x, y, 1, h, 0, 0, 0, 0.5);
        Edge(ctx, x, y + h - 1, w, 1, 0.62, 0.48, 0.28, 0.55);
        Edge(ctx, x + w - 1, y, 1, h, 0.45, 0.34, 0.20, 0.4);
    }

    public static void Gloss(Context ctx, double x, double y, double w, double h, double r, double g, double b)
    {
        if (w < 1 || h < 1)
            return;

        FillVertical(ctx, x, y, w, h,
            Math.Min(1, r + 0.28), Math.Min(1, g + 0.20), Math.Min(1, b + 0.10),
            r * 0.55, g * 0.55, b * 0.55);
        var sheen = Math.Max(1, h * 0.34);
        ctx.SetSourceRGBA(1, 1, 1, 0.22);
        ctx.Rectangle(x, y, w, sheen);
        ctx.Fill();
    }

    public static void Steel(Context ctx, double x, double y, double w, double h)
    {
        if (w < 2 || h < 2)
            return;

        FillVertical(ctx, x, y, w, h, 0.78, 0.76, 0.72, 0.36, 0.35, 0.32);
        Edge(ctx, x, y, w, 1, 0.92, 0.91, 0.88, 1);
        Edge(ctx, x, y + h - 1, w, 1, 0.16, 0.15, 0.14, 1);
        Edge(ctx, x, y, 1, h, 0.70, 0.68, 0.64, 1);
        Edge(ctx, x + w - 1, y, 1, h, 0.18, 0.17, 0.16, 1);
    }

    public const double Pad = 6;
    public const double Gutter = 12;

    public static void Tray(Context ctx, double x, double y, double w, double h)
    {
        if (w < 4 || h < 4)
            return;

        FillVertical(ctx, x, y, w, h, 0.10, 0.078, 0.058, 0.045, 0.034, 0.026);
        Edge(ctx, x, y, w, 1, 0.78, 0.62, 0.34, 0.95);
        Edge(ctx, x, y + h - 1, w, 1, 0.02, 0.015, 0.01, 0.9);
        Edge(ctx, x, y, 1, h, 0.50, 0.38, 0.20, 0.9);
        Edge(ctx, x + w - 1, y, 1, h, 0.06, 0.04, 0.025, 0.9);
        ctx.SetSourceRGBA(0, 0, 0, 0.28);
        ctx.Rectangle(x + 2, y + 2, Math.Max(1, w - 4), Math.Min(6, h - 4));
        ctx.Fill();
    }

    public static void Field(Context ctx, double x, double y, double w, double h)
    {
        FillVertical(ctx, x, y, w, h, 0.12, 0.10, 0.085, 0.045, 0.036, 0.030);
    }

    public static void Recess(Context ctx, double x, double y, double w, double h, double lip)
    {
        if (w < lip * 2 + 4 || h < lip * 2 + 4)
            return;

        var shade = Math.Min(10, lip * 0.45);
        var innerX = x + lip;
        var innerY = y + lip;
        var innerW = w - lip * 2;
        var innerH = h - lip * 2;
        Shade(ctx, innerX, innerY, innerW, shade, true);
        Shade(ctx, innerX, innerY + innerH - shade, innerW, shade, false);
        ctx.SetSourceRGBA(0.55, 0.40, 0.22, 0.85);
        ctx.LineWidth = 1;
        ctx.Rectangle(innerX + 0.5, innerY + 0.5, innerW - 1, innerH - 1);
        ctx.Stroke();
    }

    private static void Shade(Context ctx, double x, double y, double w, double h, bool fromTop)
    {
        using var tone = new LinearGradient(x, y, x, y + h);
        tone.AddColorStop(fromTop ? 0 : 1, new Color(0, 0, 0, 0.5));
        tone.AddColorStop(fromTop ? 1 : 0, new Color(0, 0, 0, 0));
        ctx.SetSource(tone);
        ctx.Rectangle(x, y, w, h);
        ctx.Fill();
    }

    private static void FillVertical(Context ctx, double x, double y, double w, double h, double r0, double g0, double b0, double r1, double g1, double b1)
    {
        using var tone = new LinearGradient(x, y, x, y + h);
        tone.AddColorStop(0, new Color(r0, g0, b0));
        tone.AddColorStop(1, new Color(r1, g1, b1));
        ctx.SetSource(tone);
        ctx.Rectangle(x, y, w, h);
        ctx.Fill();
    }

    private static void Edge(Context ctx, double x, double y, double w, double h, double r, double g, double b, double a)
    {
        ctx.SetSourceRGBA(r, g, b, a);
        ctx.Rectangle(x, y, w, h);
        ctx.Fill();
    }
}

internal sealed class GuiElementTermPanel : GuiElement
{
    public GuiElementTermPanel(ICoreClientAPI capi, ElementBounds bounds)
        : base(capi, bounds)
    {
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        TermChrome.Tray(ctx, Bounds.drawX, Bounds.drawY, Bounds.InnerWidth, Bounds.InnerHeight);
    }
}

internal sealed class GuiElementTermWell : GuiElement
{
    public GuiElementTermWell(ICoreClientAPI capi, ElementBounds bounds)
        : base(capi, bounds)
    {
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        TermChrome.Inset(ctx, Bounds.drawX, Bounds.drawY, Bounds.InnerWidth, Bounds.InnerHeight, 0.10, 0.075, 0.05);
    }
}
