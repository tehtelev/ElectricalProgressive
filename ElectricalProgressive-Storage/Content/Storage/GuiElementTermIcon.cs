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
    Clear,
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
/// Иконка терминала. У кнопок рейки своя квадратная подложка, у шкал общая прямоугольная.
/// </summary>
public sealed class GuiElementTermIcon : GuiElement
{
    private readonly TermIcon _icon;
    private readonly bool _plate;
    private readonly bool _pressed;
    private readonly bool _overlay;
    private readonly Action? _onClick;
    private LoadedTexture? _glass;

    public GuiElementTermIcon(ICoreClientAPI capi, ElementBounds bounds, TermIcon icon, bool plate, bool pressed, Action? onClick, bool overlay = false)
        : base(capi, bounds)
    {
        _icon = icon;
        _plate = plate;
        _pressed = pressed;
        _onClick = onClick;
        _overlay = overlay;
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
            PaintPlate(ctx);

        PaintIcon(ctx, Bounds.drawX, Bounds.drawY, Bounds.InnerWidth, Bounds.InnerHeight);
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (_glass == null || _glass.TextureId <= 0)
            return;

        Bounds.CalcWorldBounds();
        api.Render.Render2DTexturePremultipliedAlpha(_glass.TextureId, Bounds.renderX, Bounds.renderY, Bounds.OuterWidth, Bounds.OuterHeight, 80f);
    }

    public override void Dispose()
    {
        _glass?.Dispose();
        _glass = null;
        base.Dispose();
    }

    private void BuildOverlay()
    {
        var w = Math.Max(1, (int)Math.Round(Bounds.OuterWidth));
        var h = Math.Max(1, (int)Math.Round(Bounds.OuterHeight));
        var surface = new ImageSurface(Format.Argb32, w, h);
        var ctx = new Context(surface);
        PaintIcon(ctx, 0, 0, w, h);
        var tex = _glass ?? new LoadedTexture(api);
        generateTexture(surface, ref tex, false);
        _glass = tex;
        ctx.Dispose();
        surface.Dispose();
    }

    private void PaintPlate(Context ctx)
    {
        var x = Bounds.drawX;
        var y = Bounds.drawY;
        var w = Bounds.InnerWidth;
        var h = Bounds.InnerHeight;
        ctx.SetSourceRGBA(_pressed ? 0.48 : 0.23, _pressed ? 0.35 : 0.16, _pressed ? 0.22 : 0.11, 1);
        ctx.Rectangle(x, y, w, h);
        ctx.Fill();
        ctx.SetSourceRGBA(0.62, 0.48, 0.30, _pressed ? 1 : 0.8);
        ctx.LineWidth = 1;
        ctx.Rectangle(x + 0.5, y + 0.5, Math.Max(1, w - 1), Math.Max(1, h - 1));
        ctx.Stroke();
    }

    private void PaintIcon(Context ctx, double x, double y, double w, double h)
    {
        if (w < 1 || h < 1)
            return;

        ctx.Save();
        ctx.NewPath();
        ctx.Antialias = Antialias.Default;
        ctx.LineCap = LineCap.Round;
        ctx.LineJoin = LineJoin.Round;
        Draw(ctx, _icon, x, y, w, h);
        ctx.Restore();
    }

    public override void OnMouseDown(ICoreClientAPI api, MouseEvent args)
    {
        if (_onClick == null || args.Handled || !Bounds.PointInside(api.Input.MouseX, api.Input.MouseY))
            return;

        args.Handled = true;
        _onClick();
    }

    private static void Draw(Context ctx, TermIcon icon, double x, double y, double w, double h)
    {
        if (icon == TermIcon.Arrow)
        {
            DrawRightArrow(ctx, x, y, w, h);
            return;
        }

        var s = Math.Min(w, h) * 0.86;
        var box = new Box(x + (w - s) / 2, y + (h - s) / 2, s);
        ctx.LineWidth = Math.Max(1.15, s * 0.09);
        switch (icon)
        {
            case TermIcon.Search:
                Ink(ctx, 0.86, 0.8, 0.68);
                ctx.Arc(box.Px(0.4), box.Py(0.4), s * 0.24, 0, Math.PI * 2);
                ctx.Stroke();
                ctx.MoveTo(box.Px(0.58), box.Py(0.58));
                ctx.LineTo(box.Px(0.86), box.Py(0.86));
                ctx.Stroke();
                break;
            case TermIcon.Mod:
                Card(ctx, box, 0.28, 0.08);
                Card(ctx, box, 0.16, 0.26);
                Card(ctx, box, 0.04, 0.44);
                break;
            case TermIcon.Name:
                Ink(ctx, 0.9, 0.84, 0.7);
                RoundRect(ctx, box.Px(0.12), box.Py(0.06), s * 0.76, s * 0.88, s * 0.08);
                ctx.Stroke();
                ctx.LineWidth = Math.Max(1.0, s * 0.07);
                for (var i = 0; i < 3; i++)
                {
                    var ly = box.Py(0.32 + i * 0.2);
                    ctx.MoveTo(box.Px(0.26), ly);
                    ctx.LineTo(box.Px(i == 2 ? 0.58 : 0.74), ly);
                    ctx.Stroke();
                }
                break;
            case TermIcon.Count:
                Ink(ctx, 0.9, 0.84, 0.7);
                Bar(ctx, box, 0.08, 0.62, 0.22);
                Bar(ctx, box, 0.38, 0.38, 0.22);
                Bar(ctx, box, 0.68, 0.12, 0.22);
                break;
            case TermIcon.Clear:
                Ink(ctx, 0.75, 0.28, 0.22);
                ctx.LineWidth = Math.Max(1.6, s * 0.14);
                ctx.MoveTo(box.Px(0.22), box.Py(0.22));
                ctx.LineTo(box.Px(0.78), box.Py(0.78));
                ctx.MoveTo(box.Px(0.78), box.Py(0.22));
                ctx.LineTo(box.Px(0.22), box.Py(0.78));
                ctx.Stroke();
                break;
            case TermIcon.Channels:
                DrawChannels(ctx, box, s);
                break;
            case TermIcon.Power:
                DrawPower(ctx, box, s);
                break;
            case TermIcon.Items:
                DrawItems(ctx, box, s);
                break;
            case TermIcon.Liquid:
                DrawLiquid(ctx, box, s);
                break;
            case TermIcon.Types:
                DrawTypes(ctx, box, s);
                break;
            case TermIcon.Craft:
                DrawCraft(ctx, box, s);
                break;
            case TermIcon.Process:
                DrawProcess(ctx, box, s);
                break;
            case TermIcon.Substitute:
                DrawSubstitute(ctx, box, s);
                break;
        }
    }

    private readonly struct Box(double x, double y, double s)
    {
        public double Px(double u) => x + s * u;
        public double Py(double v) => y + s * v;
    }

    private static void Ink(Context ctx, double r, double g, double b) => ctx.SetSourceRGBA(r, g, b, 1);

    private static void Card(Context ctx, Box box, double x, double y)
    {
        Ink(ctx, 0.9, 0.84, 0.7);
        var s = box.Px(1) - box.Px(0);
        RoundRect(ctx, box.Px(x), box.Py(y), s * 0.62, s * 0.42, s * 0.04);
        ctx.Stroke();
    }

    private static void Bar(Context ctx, Box box, double x, double y, double w)
    {
        var s = box.Px(1) - box.Px(0);
        ctx.Rectangle(box.Px(x), box.Py(y), s * w, box.Py(0.96) - box.Py(y));
        ctx.Fill();
    }

    private static void Node(Context ctx, Box box, double x, double y, double radius)
    {
        var s = box.Px(1) - box.Px(0);
        ctx.Arc(box.Px(x), box.Py(y), Math.Max(1.1, s * radius), 0, Math.PI * 2);
        ctx.Fill();
    }

    private static void DrawChannels(Context ctx, Box box, double s)
    {
        Ink(ctx, 0.42, 0.26, 0.1);
        RoundRect(ctx, box.Px(0.4), box.Py(0.08), s * 0.2, s * 0.84, s * 0.06);
        ctx.Fill();
        RoundRect(ctx, box.Px(0.08), box.Py(0.4), s * 0.84, s * 0.2, s * 0.06);
        ctx.Fill();
        Ink(ctx, 0.86, 0.58, 0.24);
        RoundRect(ctx, box.Px(0.44), box.Py(0.14), s * 0.12, s * 0.72, s * 0.04);
        ctx.Fill();
        RoundRect(ctx, box.Px(0.14), box.Py(0.44), s * 0.72, s * 0.12, s * 0.04);
        ctx.Fill();
        Ink(ctx, 0.96, 0.78, 0.46);
        ctx.Rectangle(box.Px(0.47), box.Py(0.2), s * 0.035, s * 0.22);
        ctx.Fill();
        ctx.Rectangle(box.Px(0.2), box.Py(0.47), s * 0.22, s * 0.035);
        ctx.Fill();
        Ink(ctx, 0.55, 0.32, 0.12);
        Node(ctx, box, 0.5, 0.5, 0.13);
        Ink(ctx, 0.98, 0.82, 0.5);
        Node(ctx, box, 0.5, 0.5, 0.07);
        Ink(ctx, 0.95, 0.7, 0.32);
        Node(ctx, box, 0.5, 0.1, 0.08);
        Node(ctx, box, 0.5, 0.9, 0.08);
        Node(ctx, box, 0.1, 0.5, 0.08);
        Node(ctx, box, 0.9, 0.5, 0.08);
        Ink(ctx, 0.35, 0.2, 0.08);
        Node(ctx, box, 0.5, 0.1, 0.035);
        Node(ctx, box, 0.5, 0.9, 0.035);
        Node(ctx, box, 0.1, 0.5, 0.035);
        Node(ctx, box, 0.9, 0.5, 0.035);
    }

    private static void DrawPower(Context ctx, Box box, double s)
    {
        Ink(ctx, 0.16, 0.24, 0.12);
        ctx.Arc(box.Px(0.5), box.Py(0.52), s * 0.42, 0, Math.PI * 2);
        ctx.Fill();
        Ink(ctx, 0.28, 0.46, 0.2);
        ctx.LineWidth = Math.Max(1.2, s * 0.06);
        ctx.Arc(box.Px(0.5), box.Py(0.52), s * 0.34, 0, Math.PI * 2);
        ctx.Stroke();
        Ink(ctx, 0.12, 0.22, 0.08);
        Bolt(ctx, box, 0.03, 0.03);
        ctx.Fill();
        Ink(ctx, 0.55, 0.82, 0.32);
        Bolt(ctx, box, 0, 0);
        ctx.Fill();
        Ink(ctx, 0.86, 0.96, 0.62);
        ctx.MoveTo(box.Px(0.52), box.Py(0.2));
        ctx.LineTo(box.Px(0.36), box.Py(0.48));
        ctx.LineTo(box.Px(0.48), box.Py(0.48));
        ctx.LineTo(box.Px(0.42), box.Py(0.68));
        ctx.LineTo(box.Px(0.6), box.Py(0.36));
        ctx.LineTo(box.Px(0.48), box.Py(0.36));
        ctx.ClosePath();
        ctx.Fill();
    }

    private static void Bolt(Context ctx, Box box, double dx, double dy)
    {
        ctx.MoveTo(box.Px(0.58 + dx), box.Py(0.1 + dy));
        ctx.LineTo(box.Px(0.3 + dx), box.Py(0.5 + dy));
        ctx.LineTo(box.Px(0.48 + dx), box.Py(0.5 + dy));
        ctx.LineTo(box.Px(0.38 + dx), box.Py(0.9 + dy));
        ctx.LineTo(box.Px(0.76 + dx), box.Py(0.4 + dy));
        ctx.LineTo(box.Px(0.54 + dx), box.Py(0.4 + dy));
        ctx.ClosePath();
    }

    private static void DrawLiquid(Context ctx, Box box, double s)
    {
        Ink(ctx, 0.1, 0.28, 0.42);
        ctx.MoveTo(box.Px(0.5), box.Py(0.06));
        ctx.CurveTo(box.Px(0.78), box.Py(0.28), box.Px(0.92), box.Py(0.48), box.Px(0.5), box.Py(0.94));
        ctx.CurveTo(box.Px(0.08), box.Py(0.48), box.Px(0.22), box.Py(0.28), box.Px(0.5), box.Py(0.06));
        ctx.ClosePath();
        ctx.Fill();
        Ink(ctx, 0.28, 0.62, 0.82);
        ctx.MoveTo(box.Px(0.5), box.Py(0.16));
        ctx.CurveTo(box.Px(0.7), box.Py(0.32), box.Px(0.8), box.Py(0.48), box.Px(0.5), box.Py(0.82));
        ctx.CurveTo(box.Px(0.2), box.Py(0.48), box.Px(0.3), box.Py(0.32), box.Px(0.5), box.Py(0.16));
        ctx.ClosePath();
        ctx.Fill();
        Ink(ctx, 0.82, 0.94, 1);
        ctx.MoveTo(box.Px(0.4), box.Py(0.28));
        ctx.CurveTo(box.Px(0.34), box.Py(0.4), box.Px(0.36), box.Py(0.52), box.Px(0.42), box.Py(0.58));
        ctx.CurveTo(box.Px(0.34), box.Py(0.46), box.Px(0.32), box.Py(0.36), box.Px(0.4), box.Py(0.28));
        ctx.ClosePath();
        ctx.Fill();
    }

    private static void DrawItems(Context ctx, Box box, double s)
    {
        Ink(ctx, 0.18, 0.28, 0.36);
        RoundRect(ctx, box.Px(0.12), box.Py(0.4), s * 0.76, s * 0.5, s * 0.06);
        ctx.Fill();
        Ink(ctx, 0.36, 0.54, 0.66);
        RoundRect(ctx, box.Px(0.16), box.Py(0.44), s * 0.68, s * 0.4, s * 0.04);
        ctx.Fill();
        Ink(ctx, 0.22, 0.36, 0.48);
        RoundRect(ctx, box.Px(0.1), box.Py(0.16), s * 0.8, s * 0.28, s * 0.06);
        ctx.Fill();
        Ink(ctx, 0.55, 0.74, 0.84);
        RoundRect(ctx, box.Px(0.14), box.Py(0.2), s * 0.72, s * 0.16, s * 0.04);
        ctx.Fill();
        Ink(ctx, 0.72, 0.58, 0.28);
        ctx.Rectangle(box.Px(0.22), box.Py(0.18), s * 0.07, s * 0.64);
        ctx.Fill();
        ctx.Rectangle(box.Px(0.71), box.Py(0.18), s * 0.07, s * 0.64);
        ctx.Fill();
        Ink(ctx, 0.95, 0.82, 0.42);
        RoundRect(ctx, box.Px(0.4), box.Py(0.46), s * 0.2, s * 0.18, s * 0.03);
        ctx.Fill();
        Ink(ctx, 0.28, 0.18, 0.08);
        ctx.Arc(box.Px(0.5), box.Py(0.56), Math.Max(1.1, s * 0.035), 0, Math.PI * 2);
        ctx.Fill();
        Ink(ctx, 0.9, 0.95, 0.98);
        ctx.Rectangle(box.Px(0.22), box.Py(0.24), s * 0.4, Math.Max(1, s * 0.035));
        ctx.Fill();
    }

    private static void DrawTypes(Context ctx, Box box, double s)
    {
        Chip(ctx, box, s, 0.08, 0.08, 0.82, 0.48, 0.22, 0);
        Chip(ctx, box, s, 0.52, 0.08, 0.36, 0.58, 0.74, 1);
        Chip(ctx, box, s, 0.08, 0.52, 0.42, 0.7, 0.32, 2);
        Chip(ctx, box, s, 0.52, 0.52, 0.72, 0.4, 0.58, 3);
    }

    private static void DrawRightArrow(Context ctx, double x, double y, double w, double h)
    {
        Ink(ctx, 0.9, 0.78, 0.4);
        var head = w * 0.42;
        var shaftH = Math.Max(3, Math.Min(w, h) * 0.16);
        var shaftLeft = x + w * 0.12;
        var neck = x + w - head;
        ctx.Rectangle(shaftLeft, y + (h - shaftH) / 2.0, Math.Max(1, neck - shaftLeft), shaftH);
        ctx.Fill();
        ctx.MoveTo(neck, y + h * 0.2);
        ctx.LineTo(x + w * 0.88, y + h * 0.5);
        ctx.LineTo(neck, y + h * 0.8);
        ctx.ClosePath();
        ctx.Fill();
    }

    private static void DrawCraft(Context ctx, Box box, double s)
    {
        Ink(ctx, 0.86, 0.8, 0.68);
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                ctx.Rectangle(box.Px(0.12 + col * 0.28), box.Py(0.12 + row * 0.28), s * 0.2, s * 0.2);
                ctx.Stroke();
            }
        }
    }

    private static void DrawProcess(Context ctx, Box box, double s)
    {
        Ink(ctx, 0.55, 0.72, 0.84);
        ctx.Rectangle(box.Px(0.1), box.Py(0.28), s * 0.28, s * 0.44);
        ctx.Fill();
        Ink(ctx, 0.9, 0.78, 0.4);
        ctx.MoveTo(box.Px(0.46), box.Py(0.28));
        ctx.LineTo(box.Px(0.7), box.Py(0.5));
        ctx.LineTo(box.Px(0.46), box.Py(0.72));
        ctx.ClosePath();
        ctx.Fill();
        Ink(ctx, 0.46, 0.78, 0.42);
        ctx.Rectangle(box.Px(0.72), box.Py(0.28), s * 0.18, s * 0.44);
        ctx.Fill();
    }

    private static void DrawSubstitute(Context ctx, Box box, double s)
    {
        Ink(ctx, 0.9, 0.72, 0.36);
        ctx.LineWidth = Math.Max(1.4, s * 0.1);
        ctx.MoveTo(box.Px(0.18), box.Py(0.32));
        ctx.LineTo(box.Px(0.78), box.Py(0.32));
        ctx.Stroke();
        ctx.MoveTo(box.Px(0.62), box.Py(0.16));
        ctx.LineTo(box.Px(0.82), box.Py(0.32));
        ctx.LineTo(box.Px(0.62), box.Py(0.48));
        ctx.Stroke();
        ctx.MoveTo(box.Px(0.82), box.Py(0.68));
        ctx.LineTo(box.Px(0.22), box.Py(0.68));
        ctx.Stroke();
        ctx.MoveTo(box.Px(0.38), box.Py(0.52));
        ctx.LineTo(box.Px(0.18), box.Py(0.68));
        ctx.LineTo(box.Px(0.38), box.Py(0.84));
        ctx.Stroke();
    }

    private static void Chip(Context ctx, Box box, double s, double x, double y, double r, double g, double b, int mark)
    {
        Ink(ctx, r * 0.45, g * 0.45, b * 0.45);
        RoundRect(ctx, box.Px(x), box.Py(y), s * 0.4, s * 0.4, s * 0.06);
        ctx.Fill();
        Ink(ctx, r, g, b);
        RoundRect(ctx, box.Px(x + 0.04), box.Py(y + 0.04), s * 0.32, s * 0.32, s * 0.05);
        ctx.Fill();
        Ink(ctx, Math.Min(1, r + 0.28), Math.Min(1, g + 0.28), Math.Min(1, b + 0.28));
        var cx = x + 0.2;
        var cy = y + 0.2;
        switch (mark)
        {
            case 0:
                ctx.MoveTo(box.Px(cx), box.Py(cy - 0.08));
                ctx.LineTo(box.Px(cx + 0.07), box.Py(cy + 0.06));
                ctx.LineTo(box.Px(cx - 0.07), box.Py(cy + 0.06));
                ctx.ClosePath();
                ctx.Fill();
                break;
            case 1:
                ctx.Arc(box.Px(cx), box.Py(cy), s * 0.07, 0, Math.PI * 2);
                ctx.Fill();
                break;
            case 2:
                ctx.Rectangle(box.Px(cx - 0.08), box.Py(cy - 0.025), s * 0.16, s * 0.05);
                ctx.Fill();
                ctx.Rectangle(box.Px(cx - 0.025), box.Py(cy - 0.08), s * 0.05, s * 0.16);
                ctx.Fill();
                break;
            default:
                ctx.Rectangle(box.Px(cx - 0.07), box.Py(cy - 0.07), s * 0.14, s * 0.14);
                ctx.Fill();
                break;
        }
    }

    private static void RoundRect(Context ctx, double x, double y, double w, double h, double r)
    {
        ctx.NewPath();
        ctx.Arc(x + r, y + r, r, Math.PI, Math.PI * 1.5);
        ctx.Arc(x + w - r, y + r, r, Math.PI * 1.5, 0);
        ctx.Arc(x + w - r, y + h - r, r, 0, Math.PI * 0.5);
        ctx.Arc(x + r, y + h - r, r, Math.PI * 0.5, Math.PI);
        ctx.ClosePath();
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
        var x = Bounds.drawX;
        var y = Bounds.drawY;
        var w = Bounds.InnerWidth;
        var h = Bounds.InnerHeight;
        ctx.SetSourceRGBA(0.23, 0.16, 0.11, 1);
        ctx.Rectangle(x, y, w, h);
        ctx.Fill();
        ctx.SetSourceRGBA(0.62, 0.48, 0.30, 1);
        ctx.LineWidth = Math.Max(1, scaled(1));
        ctx.Rectangle(x + 0.5, y + 0.5, Math.Max(1, w - 1), Math.Max(1, h - 1));
        ctx.Stroke();
    }
}
