using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage;

/// <summary>
/// Своя рамка сети. Оттенок — цвет темпоральной шестерни, не фиолетовый AE2. Слоты — обычного размера Vintage Story:
/// инвентарь игрока в этом окне не рисуется, он и так открыт отдельно.
/// </summary>
public static class Ae2Gui
{
    public const double Header = 42;
    public const double Margin = 12;
    public const double Gap = 12;
    public const double ScrollW = 16;

    public static readonly double[] Ink = [63 / 255.0, 84 / 255.0, 78 / 255.0, 1];
    public static readonly double[] Paper = [0.949, 0.949, 0.949, 1];
    public static readonly double[] Warn = [206 / 255.0, 36 / 255.0, 1 / 255.0, 1];
    public static readonly double[] FieldInk = [154 / 255.0, 180 / 255.0, 172 / 255.0, 1];
    public static readonly double[] Well = [173 / 255.0, 196 / 255.0, 189 / 255.0, 1];
    public static readonly double[] Muted = [135 / 255.0, 165 / 255.0, 156 / 255.0, 1];

    private static readonly Dictionary<string, ImageSurface> Cache = new();

    public static ElementBounds Box(double x, double y, double w, double h)
        => ElementBounds.Fixed(x, y, w, h);

    public static ElementBounds Slots(double x, double y, int cols, int rows)
        => ElementStdBounds.SlotGrid(EnumDialogArea.None, x, y, cols, rows);

    public static GuiElementAe2Sheet Window(ICoreClientAPI capi, ElementBounds frame, double closeX, double closeY, double closeS)
    {
        var sheet = new GuiElementAe2Sheet(capi, frame, closeX, closeY, closeS);
        sheet.Fill(0, 0, frame.fixedWidth, frame.fixedHeight, 203 / 255.0, 204 / 255.0, 212 / 255.0);
        sheet.Own(Header);
        return sheet;
    }

    public static void Plate(Context ctx, double x, double y, double w, double h, bool pressed)
    {
        ctx.Save();
        ctx.Antialias = Antialias.None;
        ctx.SetSourceRGB(Ink[0], Ink[1], Ink[2]);
        ctx.Rectangle(x, y, w, h);
        ctx.Fill();
        ctx.SetSourceRGB(pressed ? 184 / 255.0 : FieldInk[0], pressed ? 214 / 255.0 : FieldInk[1], pressed ? 205 / 255.0 : FieldInk[2]);
        ctx.Rectangle(x + 1, y + 1, Math.Max(0, w - 2), Math.Max(0, h - 2));
        ctx.Fill();
        ctx.Restore();
    }

    public static ElementBounds Dialog()
        => ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

    public static CairoFont Title() => CairoFont.WhiteDetailText().WithFontSize(15).WithColor(Ink);

    public static CairoFont Label() => CairoFont.WhiteDetailText().WithFontSize(14).WithColor(Ink);

    public static CairoFont FieldFont() => CairoFont.WhiteDetailText().WithFontSize(14).WithColor(Paper);

    public static CairoFont ButtonFont() => CairoFont.WhiteDetailText().WithFontSize(13).WithColor(Ink);

    public static ImageSurface? Cached(string file)
        => Cache.TryGetValue(file, out var surface) ? surface : null;

    public static void Remember(string file, ImageSurface surface)
    {
        if (!Cache.ContainsKey(file))
            Cache[file] = surface;
    }

    public static void Blit(Context ctx, ImageSurface img, int sx, int sy, int sw, int sh, double dx, double dy, double dw, double dh)
    {
        if (sw < 1 || sh < 1 || dw < 1 || dh < 1)
            return;

        ctx.Save();
        ctx.Antialias = Antialias.None;
        ctx.NewPath();
        ctx.Rectangle(dx, dy, dw, dh);
        ctx.Clip();
        ctx.Translate(dx, dy);
        ctx.Scale(dw / sw, dh / sh);
        using var pattern = new SurfacePattern(img);
        pattern.Filter = Filter.Nearest;
        ctx.SetSource(pattern);
        ctx.Paint();
        ctx.Restore();
    }
}

public struct Ae2Slice
{
    public string File;
    public int SX, SY, SW, SH;
    public double X, Y, DW, DH;
    public int Nine;
}

public sealed class GuiElementAe2Sheet : GuiElement
{
    private readonly List<Ae2Slice> _slices;
    private readonly List<(double X, double Y, double W, double H, double R, double G, double B)> _fills;
    private readonly double _closeX;
    private readonly double _closeY;
    private readonly double _closeS;
    private bool _own;
    private double _rule = -1;

    public GuiElementAe2Sheet(ICoreClientAPI capi, ElementBounds bounds)
        : base(capi, bounds)
    {
        _slices = [];
        _fills = [];
        _closeS = -1;
    }

    public GuiElementAe2Sheet(ICoreClientAPI capi, ElementBounds bounds, double closeX, double closeY, double closeS)
        : this(capi, bounds)
    {
        _closeX = closeX;
        _closeY = closeY;
        _closeS = closeS;
    }

    public void Own(double header)
    {
        _own = true;
        _rule = header;
    }

    public void Fill(double x, double y, double w, double h, double r, double g, double b)
        => _fills.Add((x, y, w, h, r, g, b));

    public void Blit(string file, int sx, int sy, int sw, int sh, double x, double y, double dw = 0, double dh = 0, int nine = 0)
    {
        _slices.Add(new Ae2Slice
        {
            File = file,
            SX = sx,
            SY = sy,
            SW = sw,
            SH = sh,
            X = x,
            Y = y,
            DW = dw <= 0 ? sw : dw,
            DH = dh <= 0 ? sh : dh,
            Nine = nine
        });
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        foreach (var fill in _fills)
        {
            ctx.SetSourceRGB(fill.R, fill.G, fill.B);
            ctx.Rectangle(Bounds.drawX + scaled(fill.X), Bounds.drawY + scaled(fill.Y), scaled(fill.W), scaled(fill.H));
            ctx.Fill();
        }

        var opened = new Dictionary<string, ImageSurface>();
        foreach (var slice in _slices)
        {
            if (!opened.TryGetValue(slice.File, out var img))
            {
                img = Load(slice.File);
                if (img == null)
                    continue;
                opened[slice.File] = img;
                Ae2Gui.Remember(slice.File, img);
            }

            var dx = Bounds.drawX + scaled(slice.X);
            var dy = Bounds.drawY + scaled(slice.Y);
            var dw = scaled(slice.DW);
            var dh = scaled(slice.DH);
            if (slice.Nine > 0)
                Nine(ctx, img, slice.SX, slice.SY, slice.SW, slice.SH, dx, dy, dw, dh, slice.Nine);
            else
                Ae2Gui.Blit(ctx, img, slice.SX, slice.SY, slice.SW, slice.SH, dx, dy, dw, dh);
        }

        if (_own)
        {
            var edge = Math.Max(1, scaled(2));
            var prevAliased = ctx.Antialias;
            ctx.Antialias = Antialias.None;
            ctx.SetSourceRGB(Ae2Gui.Ink[0], Ae2Gui.Ink[1], Ae2Gui.Ink[2]);
            ctx.Rectangle(Bounds.drawX, Bounds.drawY, Bounds.OuterWidth, edge);
            ctx.Rectangle(Bounds.drawX, Bounds.drawY + Bounds.OuterHeight - edge, Bounds.OuterWidth, edge);
            ctx.Rectangle(Bounds.drawX, Bounds.drawY, edge, Bounds.OuterHeight);
            ctx.Rectangle(Bounds.drawX + Bounds.OuterWidth - edge, Bounds.drawY, edge, Bounds.OuterHeight);
            ctx.Fill();
            if (_rule >= 0)
            {
                ctx.SetSourceRGB(Ae2Gui.Muted[0], Ae2Gui.Muted[1], Ae2Gui.Muted[2]);
                ctx.Rectangle(Bounds.drawX, Bounds.drawY + scaled(_rule), Bounds.OuterWidth, Math.Max(1, scaled(1)));
                ctx.Fill();
            }

            ctx.Antialias = prevAliased;
        }

        if (_closeS <= 0)
            return;

        var x = Bounds.drawX + scaled(_closeX);
        var y = Bounds.drawY + scaled(_closeY);
        var s = scaled(_closeS);
        var t = Math.Max(1.0, Math.Round(scaled(1)));
        var pad = t * 2;
        var n = Math.Max(3, (int)Math.Round((s - 2 * pad) / t));
        var prev = ctx.Antialias;
        ctx.Antialias = Antialias.None;
        ctx.SetSourceRGBA(Ae2Gui.Ink[0], Ae2Gui.Ink[1], Ae2Gui.Ink[2], 1);
        for (var i = 0; i < n; i++)
        {
            var py = y + pad + i * t;
            ctx.Rectangle(x + pad + i * t, py, t, t);
            ctx.Rectangle(x + s - pad - (i + 1) * t, py, t, t);
        }

        ctx.Fill();
        ctx.Antialias = prev;
    }

    private ImageSurface? Load(string file)
    {
        if (Ae2Gui.Cached(file) is { } ready)
            return ready;

        var full = new AssetLocation("electricalprogressivestorage:textures/gui/ae2/" + file);
        var shortPath = new AssetLocation("electricalprogressivestorage:gui/ae2/" + file);
        return getImageSurfaceFromAsset(api, full, 255) ?? getImageSurfaceFromAsset(api, shortPath, 255);
    }

    private void Nine(Context ctx, ImageSurface img, int sx, int sy, int sw, int sh, double dx, double dy, double dw, double dh, int border)
    {
        var b = Math.Max(1, Math.Min(border, Math.Min(sw, sh) / 2 - 1));
        var bw = scaled(b);
        var bh = scaled(b);
        if (bw * 2 > dw)
            bw = dw / 3;
        if (bh * 2 > dh)
            bh = dh / 3;

        Ae2Gui.Blit(ctx, img, sx, sy, b, b, dx, dy, bw, bh);
        Ae2Gui.Blit(ctx, img, sx + sw - b, sy, b, b, dx + dw - bw, dy, bw, bh);
        Ae2Gui.Blit(ctx, img, sx, sy + sh - b, b, b, dx, dy + dh - bh, bw, bh);
        Ae2Gui.Blit(ctx, img, sx + sw - b, sy + sh - b, b, b, dx + dw - bw, dy + dh - bh, bw, bh);
        Ae2Gui.Blit(ctx, img, sx + b, sy, sw - 2 * b, b, dx + bw, dy, dw - 2 * bw, bh);
        Ae2Gui.Blit(ctx, img, sx + b, sy + sh - b, sw - 2 * b, b, dx + bw, dy + dh - bh, dw - 2 * bw, bh);
        Ae2Gui.Blit(ctx, img, sx, sy + b, b, sh - 2 * b, dx, dy + bh, bw, dh - 2 * bh);
        Ae2Gui.Blit(ctx, img, sx + sw - b, sy + b, b, sh - 2 * b, dx + dw - bw, dy + bh, bw, dh - 2 * bh);
        Ae2Gui.Blit(ctx, img, sx + b, sy + b, sw - 2 * b, sh - 2 * b, dx + bw, dy + bh, dw - 2 * bw, dh - 2 * bh);
    }
}

public sealed class GuiElementAe2Button : GuiElement
{
    private readonly string _text;
    private readonly Action _click;
    private readonly CairoFont _font;

    private readonly bool _pressed;

    public GuiElementAe2Button(ICoreClientAPI capi, ElementBounds bounds, string text, Action click, CairoFont? font = null, bool pressed = false)
        : base(capi, bounds)
    {
        _text = text;
        _click = click;
        _font = font ?? Ae2Gui.ButtonFont();
        _pressed = pressed;
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        Ae2Gui.Plate(ctx, Bounds.drawX, Bounds.drawY, Bounds.InnerWidth, Bounds.InnerHeight, _pressed);
        _font.SetupContext(ctx);
        var ext = ctx.TextExtents(_text);
        ctx.MoveTo(Bounds.drawX + (Bounds.InnerWidth - ext.Width) / 2, Bounds.drawY + (Bounds.InnerHeight + ext.Height) / 2 - 1);
        ctx.ShowText(_text);
    }

    public override void OnMouseDown(ICoreClientAPI api, MouseEvent args)
    {
        if (args.Handled || !Bounds.PointInside(api.Input.MouseX, api.Input.MouseY))
            return;

        args.Handled = true;
        _click();
    }
}

public sealed class GuiElementAe2Stack : GuiElement
{
    private readonly DummySlot _slot;

    public GuiElementAe2Stack(ICoreClientAPI capi, ElementBounds bounds, ItemStack stack)
        : base(capi, bounds)
    {
        var copy = stack.Clone();
        copy.StackSize = 1;
        _slot = new DummySlot(copy);
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        Bounds.CalcWorldBounds();
        var icon = (float)scaled(GuiElementPassiveItemSlot.unscaledItemSize);
        api.Render.RenderItemstackToGui(
            _slot,
            Bounds.renderX + Bounds.OuterWidth / 2,
            Bounds.renderY + Bounds.OuterHeight / 2,
            90,
            icon,
            ColorUtil.WhiteArgb,
            true,
            false,
            false);
    }
}

public static class Ae2Composer
{
    public static GuiComposer AddSlots(this GuiComposer composer, ICoreClientAPI capi, IInventory inventory, Action<object> sendPacket, int columns, int[] slots, ElementBounds bounds, string key, Ae2SlotMark mark = Ae2SlotMark.None)
    {
        if (!composer.Composed)
        {
            composer.AddInteractiveElement(new GuiElementAe2Slots(capi, inventory, sendPacket, columns, slots, bounds, mark), key);
            GuiElementItemSlotGridBase.UpdateLastSlotGridFlag(composer);
        }

        return composer;
    }

    public static GuiComposer AddScroll(this GuiComposer composer, Action<float> onScroll, ElementBounds bounds, string key)
    {
        if (!composer.Composed)
            composer.AddInteractiveElement(new GuiElementAe2Scroll(composer.Api, onScroll, bounds), key);

        return composer;
    }
}

public enum Ae2SlotMark
{
    None,
    Arrow,
    Pattern
}

/// <summary>Сетка слотов без коричневой ячейки Vintage Story. Стрелка и ромб шаблона рисуются внутри ячейки.</summary>
public sealed class GuiElementAe2Slots : GuiElementItemSlotGrid
{
    private readonly Ae2SlotMark _mark;

    public GuiElementAe2Slots(ICoreClientAPI capi, IInventory inventory, Action<object> sendPacket, int columns, int[] slots, ElementBounds bounds, Ae2SlotMark mark = Ae2SlotMark.None)
        : base(capi, inventory, sendPacket, columns, slots, bounds)
    {
        _mark = mark;
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        base.ComposeElements(ctx, surface);
        if (SlotBounds == null || SlotBounds.Length == 0)
            return;

        var w = Math.Max(1, SlotBounds[0].OuterWidthInt);
        var h = Math.Max(1, SlotBounds[0].OuterHeightInt);
        var well = new ImageSurface(Format.Argb32, w, h);
        var paint = genContext(well);
        paint.Antialias = Antialias.None;
        paint.SetSourceRGB(Ae2Gui.Well[0], Ae2Gui.Well[1], Ae2Gui.Well[2]);
        paint.Rectangle(0, 0, w, h);
        paint.Fill();
        paint.SetSourceRGBA(Ae2Gui.Ink[0], Ae2Gui.Ink[1], Ae2Gui.Ink[2], 0.55);
        paint.Rectangle(0, 0, w, 1);
        paint.Rectangle(0, h - 1, w, 1);
        paint.Rectangle(0, 0, 1, h);
        paint.Rectangle(w - 1, 0, 1, h);
        paint.Fill();
        if (_mark == Ae2SlotMark.Arrow)
            ArrowMark(paint, w, h);
        else if (_mark == Ae2SlotMark.Pattern)
            PatternMark(paint, w, h);
        generateTexture(well, ref slotTexture, false);
        paint.Dispose();
        well.Dispose();

        var hover = new ImageSurface(Format.Argb32, w + 4, h + 4);
        var hoverCtx = genContext(hover);
        hoverCtx.SetSourceRGBA(1, 1, 1, 0.35);
        hoverCtx.Rectangle(0, 0, w + 4, h + 4);
        hoverCtx.Fill();
        generateTexture(hover, ref highlightSlotTexture, false);
        hoverCtx.Dispose();
        hover.Dispose();
    }

    private static void ArrowMark(Context paint, int w, int h)
    {
        var cx = w / 2.0;
        var top = h * 0.20;
        var neck = h * 0.50;
        var bot = h * 0.84;
        var head = w * 0.34;
        var shaft = Math.Max(2, w * 0.12);
        paint.Antialias = Antialias.Default;
        paint.SetSourceRGBA(Ae2Gui.Ink[0], Ae2Gui.Ink[1], Ae2Gui.Ink[2], 0.9);
        paint.MoveTo(cx - shaft / 2, top);
        paint.LineTo(cx + shaft / 2, top);
        paint.LineTo(cx + shaft / 2, neck);
        paint.LineTo(cx + head / 2, neck);
        paint.LineTo(cx, bot);
        paint.LineTo(cx - head / 2, neck);
        paint.LineTo(cx - shaft / 2, neck);
        paint.ClosePath();
        paint.Fill();
        paint.LineWidth = Math.Max(1.5, w * 0.035);
        paint.SetSourceRGBA(1, 1, 1, 0.45);
        paint.MoveTo(cx - shaft / 2, top);
        paint.LineTo(cx - shaft / 2, neck);
        paint.LineTo(cx - head / 2, neck);
        paint.LineTo(cx, bot);
        paint.Stroke();
    }

    private static void PatternMark(Context paint, int w, int h)
    {
        paint.Antialias = Antialias.Default;
        Facet(paint, w / 2.0, h / 2.0, w * 0.32, h * 0.36, 0);
        Facet(paint, w / 2.0, h / 2.0, w * 0.16, h * 0.18, 0.12);
    }

    private static void Facet(Context paint, double cx, double cy, double rx, double ry, double lift)
    {
        var topX = cx;
        var topY = cy - ry;
        var rightX = cx + rx;
        var rightY = cy;
        var botX = cx;
        var botY = cy + ry;
        var leftX = cx - rx;
        var leftY = cy;
        FillTri(paint, cx, cy, topX, topY, leftX, leftY, 209 / 255.0 + lift, 230 / 255.0 + lift, 224 / 255.0 + lift);
        FillTri(paint, cx, cy, topX, topY, rightX, rightY, 163 / 255.0 + lift, 194 / 255.0 + lift, 185 / 255.0 + lift);
        FillTri(paint, cx, cy, botX, botY, leftX, leftY, 128 / 255.0 + lift, 158 / 255.0 + lift, 149 / 255.0 + lift);
        FillTri(paint, cx, cy, botX, botY, rightX, rightY, 87 / 255.0 + lift, 117 / 255.0 + lift, 108 / 255.0 + lift);
        paint.LineWidth = Math.Max(1.25, rx * 0.12);
        paint.SetSourceRGBA(1, 1, 1, 0.55);
        paint.MoveTo(topX, topY);
        paint.LineTo(leftX, leftY);
        paint.LineTo(botX, botY);
        paint.Stroke();
        paint.SetSourceRGBA(56 / 255.0, 82 / 255.0, 74 / 255.0, 0.75);
        paint.MoveTo(topX, topY);
        paint.LineTo(rightX, rightY);
        paint.LineTo(botX, botY);
        paint.Stroke();
    }

    private static void FillTri(Context paint, double ax, double ay, double bx, double by, double cx, double cy, double r, double g, double b)
    {
        paint.SetSourceRGB(Math.Min(1, r), Math.Min(1, g), Math.Min(1, b));
        paint.MoveTo(ax, ay);
        paint.LineTo(bx, by);
        paint.LineTo(cx, cy);
        paint.ClosePath();
        paint.Fill();
    }
}

/// <summary>Ползунок цвета AE2. Дорожка уже нарисована в текстуре окна, здесь только бегунок.</summary>
public sealed class GuiElementAe2Scroll : GuiElementScrollbar
{
    private int _inset;

    public GuiElementAe2Scroll(ICoreClientAPI capi, Action<float> onScroll, ElementBounds bounds)
        : base(capi, onScroll, bounds)
    {
    }

    public override void ComposeElements(Context ctxStatic, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        RecomposeHandle();
    }

    public override void RecomposeHandle()
    {
        Bounds.CalcWorldBounds();
        _inset = Math.Max(1, (int)Math.Round(scaled(2)));
        var w = Math.Max(1, (int)Bounds.InnerWidth - 2 * _inset);
        var h = Math.Max(1, (int)Math.Round(currentHandleHeight));
        var surface = new ImageSurface(Format.Argb32, w, h);
        var ctx = genContext(surface);
        ctx.Antialias = Antialias.None;
        ctx.SetSourceRGB(139 / 255.0, 167 / 255.0, 159 / 255.0);
        ctx.Rectangle(0, 0, w, h);
        ctx.Fill();
        if (w > 2 && h > 2)
        {
            ctx.SetSourceRGB(208 / 255.0, 222 / 255.0, 218 / 255.0);
            ctx.Rectangle(1, 1, w - 2, h - 2);
            ctx.Fill();
            ctx.SetSourceRGB(0.97, 0.97, 0.99);
            ctx.Rectangle(1, 1, w - 2, 1);
            ctx.Fill();
        }

        generateTexture(surface, ref handleTexture, false);
        ctx.Dispose();
        surface.Dispose();
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (handleTexture.TextureId == 0 || currentHandleHeight < 1)
            return;

        api.Render.Render2DTexturePremultipliedAlpha(
            handleTexture.TextureId,
            (int)(Bounds.renderX + _inset),
            (int)(Bounds.renderY + currentHandlePosition),
            handleTexture.Width,
            (int)currentHandleHeight,
            200);
    }
}
