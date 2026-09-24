using System;
using Cairo;
using Vintagestory.API.Client;

namespace ElectricalProgressive.Content.Block.EBlastFurnace;

public static class BlastFurnaceSlotIcons
{
    public const string Ore = "eblastore";
    public const string Mold = "eblastmold";
    public const string Chance = "eblastpct";

    public static void Register(ICoreClientAPI capi)
    {
        capi.Gui.Icons.CustomIcons[Ore] = DrawOre;
        capi.Gui.Icons.CustomIcons[Mold] = DrawMold;
        capi.Gui.Icons.CustomIcons[Chance] = DrawPercent;
    }

    private static void DrawOre(Context cr, int x, int y, float width, float height, double[] rgba)
    {
        cr.Save();
        cr.SetSourceRGBA(rgba[0], rgba[1], rgba[2], rgba[3]);
        var s = Math.Min(width, height);
        var ox = x + (width - s) * 0.5;
        var oy = y + (height - s) * 0.5;

        Rock(cr, rgba, ox + s * 0.18, oy + s * 0.42, s * 0.38, s * 0.38);
        Rock(cr, rgba, ox + s * 0.48, oy + s * 0.40, s * 0.40, s * 0.40);
        Rock(cr, rgba, ox + s * 0.32, oy + s * 0.22, s * 0.34, s * 0.32);
        cr.Restore();
    }

    private static void Rock(Context cr, double[] rgba, double x, double y, double w, double h)
    {
        cr.NewPath();
        cr.MoveTo(x + w * 0.15, y + h * 0.75);
        cr.LineTo(x + w * 0.05, y + h * 0.45);
        cr.LineTo(x + w * 0.35, y + h * 0.12);
        cr.LineTo(x + w * 0.78, y + h * 0.22);
        cr.LineTo(x + w * 0.95, y + h * 0.55);
        cr.LineTo(x + w * 0.70, y + h * 0.88);
        cr.ClosePath();
        cr.SetSourceRGBA(rgba[0], rgba[1], rgba[2], rgba[3]);
        cr.FillPreserve();
        cr.SetSourceRGBA(0, 0, 0, 0.35);
        cr.LineWidth = 1;
        cr.Stroke();
    }

    private static void DrawMold(Context cr, int x, int y, float width, float height, double[] rgba)
    {
        cr.Save();
        cr.SetSourceRGBA(rgba[0], rgba[1], rgba[2], rgba[3]);
        var s = Math.Min(width, height);
        var ox = x + (width - s) * 0.5;
        var oy = y + (height - s) * 0.5;
        var t = s * 0.14;

        cr.NewPath();
        cr.MoveTo(ox + s * 0.18, oy + s * 0.22);
        cr.LineTo(ox + s * 0.18, oy + s * 0.78);
        cr.LineTo(ox + s * 0.82, oy + s * 0.78);
        cr.LineTo(ox + s * 0.82, oy + s * 0.22);
        cr.LineTo(ox + s * 0.82 - t, oy + s * 0.22);
        cr.LineTo(ox + s * 0.82 - t, oy + s * 0.78 - t);
        cr.LineTo(ox + s * 0.18 + t, oy + s * 0.78 - t);
        cr.LineTo(ox + s * 0.18 + t, oy + s * 0.22);
        cr.ClosePath();
        cr.Fill();
        cr.Restore();
    }

    private static void DrawPercent(Context cr, int x, int y, float width, float height, double[] rgba)
    {
        cr.Save();
        var s = Math.Min(width, height);
        cr.SetSourceRGBA(rgba[0], rgba[1], rgba[2], rgba[3]);
        cr.SelectFontFace("sans-serif", FontSlant.Normal, FontWeight.Bold);
        cr.SetFontSize(s * 0.72);
        var text = "%";
        var ext = cr.TextExtents(text);
        cr.MoveTo(x + (width - ext.Width) * 0.5 - ext.XBearing, y + (height + ext.Height) * 0.5);
        cr.ShowText(text);
        cr.Restore();
    }
}
