using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ElectricalProgressive.Content.Storage;

/// <summary>
/// Рамка терминала. Борт тонкий, скобы уменьшены, узор по краям не растягивается.
/// </summary>
public sealed class GuiElementTermFrame : GuiElement
{
    public const double Inset = 26;

    private const double Border = 20;
    private const double SrcBorder = 36;
    private const double SrcCorner = 140;
    private const double SrcW = 781;
    private const double SrcH = 596;

    private static readonly AssetLocation FrameTex = new("electricalprogressivestorage:gui/terminal-frame.png");
    private static readonly AssetLocation FrameTexFull = new("electricalprogressivestorage:textures/gui/terminal-frame.png");

    private readonly double _closeX;
    private readonly double _closeY;
    private readonly double _closeS;

    public GuiElementTermFrame(ICoreClientAPI capi, ElementBounds bounds, double closeX, double closeY, double closeS)
        : base(capi, bounds)
    {
        _closeX = closeX;
        _closeY = closeY;
        _closeS = closeS;
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        var x = Bounds.drawX;
        var y = Bounds.drawY;
        var w = Bounds.OuterWidth;
        var h = Bounds.OuterHeight;
        ctx.SetSourceRGBA(23 / 255.0, 23 / 255.0, 23 / 255.0, 1);
        ctx.Rectangle(x, y, w, h);
        ctx.Fill();

        var img = getImageSurfaceFromAsset(api, FrameTex, 255) ?? getImageSurfaceFromAsset(api, FrameTexFull, 255);
        if (img != null)
        {
            var kx = img.Width / SrcW;
            var ky = img.Height / SrcH;
            var srcBorderX = Math.Max(1, (int)Math.Round(SrcBorder * kx));
            var srcBorderY = Math.Max(1, (int)Math.Round(SrcBorder * ky));
            var srcCornerX = Math.Max(srcBorderX + 1, (int)Math.Round(SrcCorner * kx));
            var srcCornerY = Math.Max(srcBorderY + 1, (int)Math.Round(SrcCorner * ky));
            var border = scaled(Border);
            var cornerX = border * srcCornerX / srcBorderX;
            var cornerY = border * srcCornerY / srcBorderY;

            TileX(ctx, img, srcCornerX, 0, img.Width - 2 * srcCornerX, srcBorderY, x + cornerX, y, w - 2 * cornerX, border);
            TileX(ctx, img, srcCornerX, img.Height - srcBorderY, img.Width - 2 * srcCornerX, srcBorderY, x + cornerX, y + h - border, w - 2 * cornerX, border);
            TileY(ctx, img, 0, srcCornerY, srcBorderX, img.Height - 2 * srcCornerY, x, y + cornerY, border, h - 2 * cornerY);
            TileY(ctx, img, img.Width - srcBorderX, srcCornerY, srcBorderX, img.Height - 2 * srcCornerY, x + w - border, y + cornerY, border, h - 2 * cornerY);

            Blit(ctx, img, 0, 0, srcCornerX, srcCornerY, x, y, cornerX, cornerY);
            Blit(ctx, img, img.Width - srcCornerX, 0, srcCornerX, srcCornerY, x + w - cornerX, y, cornerX, cornerY);
            Blit(ctx, img, 0, img.Height - srcCornerY, srcCornerX, srcCornerY, x, y + h - cornerY, cornerX, cornerY);
            Blit(ctx, img, img.Width - srcCornerX, img.Height - srcCornerY, srcCornerX, srcCornerY, x + w - cornerX, y + h - cornerY, cornerX, cornerY);
            img.Dispose();
        }

        var cx = x + scaled(_closeX);
        var cy = y + scaled(_closeY);
        var pad = scaled(3);
        var size = scaled(_closeS);
        ctx.SetSourceRGBA(0.86, 0.84, 0.8, 1);
        ctx.LineWidth = Math.Max(1.6, scaled(1.7));
        ctx.MoveTo(cx + pad, cy + pad);
        ctx.LineTo(cx + size - pad, cy + size - pad);
        ctx.MoveTo(cx + size - pad, cy + pad);
        ctx.LineTo(cx + pad, cy + size - pad);
        ctx.Stroke();
    }

    private static void TileX(Context ctx, ImageSurface img, int sx, int sy, int sw, int sh, double dx, double dy, double dw, double dh)
    {
        if (sw < 1 || sh < 1 || dw < 1 || dh < 1)
            return;
        var tileW = dh * sw / sh;
        var x = dx;
        var end = dx + dw;
        while (x < end - 0.5)
        {
            var piece = Math.Min(tileW, end - x);
            Blit(ctx, img, sx, sy, Math.Max(1, (int)Math.Round(sw * piece / tileW)), sh, x, dy, piece, dh);
            x += piece;
        }
    }

    private static void TileY(Context ctx, ImageSurface img, int sx, int sy, int sw, int sh, double dx, double dy, double dw, double dh)
    {
        if (sw < 1 || sh < 1 || dw < 1 || dh < 1)
            return;
        var tileH = dw * sh / sw;
        var y = dy;
        var end = dy + dh;
        while (y < end - 0.5)
        {
            var piece = Math.Min(tileH, end - y);
            Blit(ctx, img, sx, sy, sw, Math.Max(1, (int)Math.Round(sh * piece / tileH)), dx, y, dw, piece);
            y += piece;
        }
    }

    private static void Blit(Context ctx, ImageSurface img, int sx, int sy, int sw, int sh, double dx, double dy, double dw, double dh)
    {
        if (sw < 1 || sh < 1 || dw < 1 || dh < 1)
            return;

        ctx.Save();
        ctx.NewPath();
        ctx.Rectangle(dx, dy, dw, dh);
        ctx.Clip();
        ctx.Translate(dx, dy);
        ctx.Scale(dw / sw, dh / sh);
        ctx.SetSourceSurface(img, -sx, -sy);
        ctx.Paint();
        ctx.Restore();
    }
}
