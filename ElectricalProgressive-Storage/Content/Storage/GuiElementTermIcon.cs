using System;
using Cairo;
using Vintagestory.API.Client;

namespace ElectricalProgressive.Content.Storage;

public enum TermIcon
{
    Search,
    Mod,
    Name,
    Count,
    Channels,
    Power,
    Items,
    Liquid,
    Types,
    Craft,
    Process,
    Substitute,
    Arrow
}

/// <summary>
/// Линейная иконка в том же приёме, что значки редактора квестов: круглая обводка, тонкий штрих.
/// У кнопок рейки своя квадратная подложка, у шкал общая прямоугольная.
/// </summary>
public sealed class GuiElementTermIcon : GuiElement
{
    private static readonly double[] Ink = [0.86, 0.82, 0.74];
    private static readonly double[] InkHot = [0.98, 0.90, 0.62];

    private readonly TermIcon _icon;
    private readonly bool _plate;
    private readonly bool _pressed;
    private readonly bool _overlay;
    private readonly bool _solid;
    private readonly Action? _onClick;
    private LoadedTexture? _glass;

    public GuiElementTermIcon(ICoreClientAPI capi, ElementBounds bounds, TermIcon icon, bool plate, bool pressed, Action? onClick, bool overlay = false, bool solid = false)
        : base(capi, bounds)
    {
        _icon = icon;
        _plate = plate;
        _pressed = pressed;
        _onClick = onClick;
        _overlay = overlay;
        _solid = solid;
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        if (_overlay)
        {
            BuildOverlay();
            return;
        }

        if (_plate)
            TermChrome.Raised(ctx, Bounds.drawX, Bounds.drawY, Bounds.InnerWidth, Bounds.InnerHeight, _pressed);

        Paint(ctx, Bounds.drawX, Bounds.drawY, Math.Min(Bounds.InnerWidth, Bounds.InnerHeight));
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (_glass == null || _glass.TextureId <= 0)
            return;

        api.Render.Render2DTexturePremultipliedAlpha(
            _glass.TextureId, Bounds.renderX, Bounds.renderY, Bounds.OuterWidth, Bounds.OuterHeight, 80f);
    }

    public override void OnMouseDown(ICoreClientAPI api, MouseEvent args)
    {
        if (_onClick == null || args.Handled || !Bounds.PointInside(api.Input.MouseX, api.Input.MouseY))
            return;

        args.Handled = true;
        _onClick();
    }

    public override void Dispose()
    {
        _glass?.Dispose();
        _glass = null;
        base.Dispose();
    }

    private void BuildOverlay()
    {
        var w = Math.Max(1, (int)Math.Ceiling(Bounds.OuterWidth));
        var h = Math.Max(1, (int)Math.Ceiling(Bounds.OuterHeight));
        var surface = new ImageSurface(Format.Argb32, w, h);
        var ctx = new Context(surface);
        Paint(ctx, 0, 0, Math.Min(w, h));
        ctx.Dispose();
        var tex = new LoadedTexture(api);
        generateTexture(surface, ref tex, true);
        _glass = tex;
        surface.Dispose();
    }

    private void Paint(Context ctx, double x, double y, double side)
    {
        if (side < 4)
            return;

        if (_solid && _icon == TermIcon.Arrow)
        {
            DrawSolidArrow(ctx, x, y, side);
            return;
        }

        var color = _pressed ? InkHot : Ink;
        ctx.Save();
        ctx.Antialias = Antialias.Default;
        ctx.Translate(x + side * 0.12, y + side * 0.12);
        ctx.Scale(side * 0.76 / 24.0, side * 0.76 / 24.0);
        ctx.LineWidth = 1.7;
        ctx.LineCap = LineCap.Round;
        ctx.LineJoin = LineJoin.Round;
        ctx.SetSourceRGBA(color[0], color[1], color[2], 1);
        Draw(_icon, ctx, color);
        ctx.Restore();
    }

    private static void DrawSolidArrow(Context ctx, double x, double y, double side)
    {
        ctx.Save();
        ctx.Antialias = Antialias.Default;
        var pad = side * 0.06;
        var w = side - pad * 2;
        var h = side - pad * 2;
        var mid = h * 0.5;
        var shaft = h * 0.46;
        var head = w * 0.48;
        ctx.Translate(x + pad, y + pad);
        ctx.MoveTo(0, mid - shaft * 0.5);
        ctx.LineTo(w - head, mid - shaft * 0.5);
        ctx.LineTo(w - head, mid - h * 0.48);
        ctx.LineTo(w, mid);
        ctx.LineTo(w - head, mid + h * 0.48);
        ctx.LineTo(w - head, mid + shaft * 0.5);
        ctx.LineTo(0, mid + shaft * 0.5);
        ctx.ClosePath();
        ctx.SetSourceRGBA(Ink[0], Ink[1], Ink[2], 1);
        ctx.Fill();
        ctx.Restore();
    }

    private static void Draw(TermIcon icon, Context ctx, double[] color)
    {
        switch (icon)
        {
            case TermIcon.Search:
                ctx.Arc(10, 10, 5.4, 0, Math.PI * 2);
                ctx.Stroke();
                ctx.MoveTo(14.2, 14.2);
                ctx.LineTo(20.6, 20.6);
                ctx.Stroke();
                break;
            case TermIcon.Mod:
                ctx.Arc(12, 12, 4.1, 0, Math.PI * 2);
                ctx.Stroke();
                ctx.Arc(12, 12, 1.5, 0, Math.PI * 2);
                ctx.Stroke();
                for (var i = 0; i < 8; i++)
                {
                    var a = i * Math.PI / 4.0;
                    var c = Math.Cos(a);
                    var s = Math.Sin(a);
                    ctx.MoveTo(12 + c * 5.5, 12 + s * 5.5);
                    ctx.LineTo(12 + c * 8.4, 12 + s * 8.4);
                }

                ctx.Stroke();
                break;
            case TermIcon.Name:
                Round(ctx, 4, 4, 16, 16, 1.6);
                ctx.Stroke();
                ctx.MoveTo(12, 5.2);
                ctx.LineTo(12, 18.8);
                ctx.MoveTo(6.4, 8.2);
                ctx.LineTo(9.6, 8.2);
                ctx.MoveTo(6.4, 11.2);
                ctx.LineTo(9.6, 11.2);
                ctx.MoveTo(14.4, 8.2);
                ctx.LineTo(17.6, 8.2);
                ctx.MoveTo(14.4, 11.2);
                ctx.LineTo(17.6, 11.2);
                ctx.Stroke();
                break;
            case TermIcon.Count:
                ctx.Rectangle(4, 14, 4.2, 6);
                ctx.Rectangle(9.9, 9, 4.2, 11);
                ctx.Rectangle(15.8, 4, 4.2, 16);
                ctx.Stroke();
                break;
            case TermIcon.Channels:
                ctx.MoveTo(6, 4.5);
                ctx.LineTo(6, 19.5);
                ctx.MoveTo(6, 6.5);
                ctx.LineTo(16, 6.5);
                ctx.MoveTo(6, 12);
                ctx.LineTo(14, 12);
                ctx.MoveTo(6, 17.5);
                ctx.LineTo(12, 17.5);
                ctx.Stroke();
                Dot(ctx, color, 6, 4.5, 1.7);
                Dot(ctx, color, 17.2, 6.5, 1.7);
                Dot(ctx, color, 15.2, 12, 1.7);
                Dot(ctx, color, 13.2, 17.5, 1.7);
                break;
            case TermIcon.Power:
                ctx.MoveTo(13.5, 2.5);
                ctx.LineTo(7.5, 12);
                ctx.LineTo(11.6, 12);
                ctx.LineTo(9.2, 21.5);
                ctx.LineTo(17.4, 10.2);
                ctx.LineTo(13.2, 10.2);
                ctx.ClosePath();
                ctx.Stroke();
                break;
            case TermIcon.Items:
                Round(ctx, 4, 8.5, 16, 11.5, 1.5);
                ctx.Stroke();
                ctx.MoveTo(4.4, 13.2);
                ctx.LineTo(19.6, 13.2);
                ctx.Stroke();
                Round(ctx, 10.4, 11.4, 3.2, 3.4, 0.6);
                ctx.Stroke();
                break;
            case TermIcon.Liquid:
                ctx.MoveTo(8.2, 3.2);
                ctx.LineTo(15.8, 3.2);
                ctx.MoveTo(9.4, 3.2);
                ctx.LineTo(9.4, 7.2);
                ctx.MoveTo(14.6, 3.2);
                ctx.LineTo(14.6, 7.2);
                ctx.MoveTo(9.4, 7.4);
                ctx.CurveTo(4.2, 9.2, 3.2, 13, 4.4, 16.4);
                ctx.CurveTo(5.6, 20.2, 8.4, 21.4, 12, 21.4);
                ctx.CurveTo(15.6, 21.4, 18.4, 20.2, 19.6, 16.4);
                ctx.CurveTo(20.8, 13, 19.8, 9.2, 14.6, 7.4);
                ctx.MoveTo(7.2, 16.2);
                ctx.CurveTo(9.4, 17.6, 14.2, 14.8, 16.8, 16.2);
                ctx.Stroke();
                break;
            case TermIcon.Types:
                ctx.Arc(6.2, 6.2, 2.5, 0, Math.PI * 2);
                ctx.Rectangle(15.2, 3.6, 5.2, 5.2);
                ctx.MoveTo(6.2, 14.6);
                ctx.LineTo(9.4, 20.4);
                ctx.LineTo(3, 20.4);
                ctx.ClosePath();
                ctx.MoveTo(18, 14.8);
                ctx.LineTo(21.6, 18.4);
                ctx.LineTo(18, 22);
                ctx.LineTo(14.4, 18.4);
                ctx.ClosePath();
                ctx.Stroke();
                break;
            case TermIcon.Craft:
                Round(ctx, 3, 5, 15, 5.2, 1.2);
                ctx.Stroke();
                ctx.MoveTo(10.4, 10.2);
                ctx.LineTo(10.4, 20.4);
                ctx.Stroke();
                break;
            case TermIcon.Process:
                const double end = 5.4;
                ctx.Arc(12, 12, 6.6, 0.9, end);
                ctx.Stroke();
                var px = 12 + Math.Cos(end) * 6.6;
                var py = 12 + Math.Sin(end) * 6.6;
                var tx = -Math.Sin(end);
                var ty = Math.Cos(end);
                ctx.MoveTo(px - tx * 3.4 + Math.Cos(end) * 2.1, py - ty * 3.4 + Math.Sin(end) * 2.1);
                ctx.LineTo(px, py);
                ctx.LineTo(px - tx * 3.4 - Math.Cos(end) * 2.1, py - ty * 3.4 - Math.Sin(end) * 2.1);
                ctx.Stroke();
                break;
            case TermIcon.Substitute:
                ctx.MoveTo(3.2, 8);
                ctx.LineTo(15.2, 8);
                ctx.MoveTo(15.2, 8);
                ctx.LineTo(11.4, 4.8);
                ctx.MoveTo(15.2, 8);
                ctx.LineTo(11.4, 11.2);
                ctx.MoveTo(20.8, 16);
                ctx.LineTo(8.8, 16);
                ctx.MoveTo(8.8, 16);
                ctx.LineTo(12.6, 12.8);
                ctx.MoveTo(8.8, 16);
                ctx.LineTo(12.6, 19.2);
                ctx.Stroke();
                break;
            default:
                ctx.MoveTo(4, 12);
                ctx.LineTo(14, 12);
                ctx.MoveTo(10, 7);
                ctx.LineTo(17, 12);
                ctx.LineTo(10, 17);
                ctx.Stroke();
                break;
        }
    }

    private static void Round(Context ctx, double x, double y, double w, double h, double r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2);
        var right = x + w;
        var bottom = y + h;
        ctx.NewPath();
        ctx.MoveTo(x + r, y);
        ctx.Arc(right - r, y + r, r, -Math.PI / 2, 0);
        ctx.Arc(right - r, bottom - r, r, 0, Math.PI / 2);
        ctx.Arc(x + r, bottom - r, r, Math.PI / 2, Math.PI);
        ctx.Arc(x + r, y + r, r, Math.PI, 3 * Math.PI / 2);
        ctx.ClosePath();
    }

    private static void Dot(Context ctx, double[] color, double x, double y, double r)
    {
        ctx.SetSourceRGBA(color[0], color[1], color[2], 1);
        ctx.Arc(x, y, r, 0, Math.PI * 2);
        ctx.Fill();
    }
}

/// <summary>
/// Прямоугольная подложка одной шкалы: иконка, полоска и число лежат поверх неё.
/// </summary>
public sealed class GuiElementMeterPlate : GuiElement
{
    public GuiElementMeterPlate(ICoreClientAPI capi, ElementBounds bounds)
        : base(capi, bounds)
    {
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        TermChrome.Raised(ctx, Bounds.drawX, Bounds.drawY, Bounds.InnerWidth, Bounds.InnerHeight, false);
    }
}
