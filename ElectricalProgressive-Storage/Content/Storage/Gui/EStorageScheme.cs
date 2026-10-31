using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ElectricalProgressive.Content.Storage.Gui;

/// <summary>
/// Схема пособия: прямоугольники и линии на одной сетке, без моделей предметов.
/// </summary>
public static class EStorageScheme
{
    public static void Register(ICoreClientAPI api)
    {
        api.RegisterVtmlTagConverter("escheme", Convert);
    }

    private static RichTextComponentBase Convert(ICoreClientAPI capi, VtmlTagToken token, Stack<CairoFont> fontStack, Action<LinkTextComponent> didClick)
    {
        var id = "net";
        if (token.Attributes != null && token.Attributes.TryGetValue("id", out var value) && !string.IsNullOrEmpty(value))
            id = value;
        return new SchemeComponent(capi, id);
    }

    private readonly struct Grid
    {
        public readonly double Ox;
        public readonly double Oy;
        public readonly double S;

        public Grid(double ox, double oy, double s)
        {
            Ox = ox;
            Oy = oy;
            S = s;
        }

        public double X(double x) => Ox + x * S;
        public double Y(double y) => Oy + y * S;
    }

    private sealed class SchemeComponent : RichTextComponentBase
    {
        private readonly string _id;
        private readonly double _designW;
        private readonly double _designH;

        public SchemeComponent(ICoreClientAPI capi, string id) : base(capi)
        {
            _id = id;
            Float = EnumFloat.None;
            VerticalAlign = EnumVerticalAlign.Top;
            (_designW, _designH) = id switch
            {
                "mount" => (168d, 166d),
                "order" => (424d, 192d),
                _ => (424d, 130d)
            };
        }

        public override EnumCalcBoundsResult CalcBounds(TextFlowPath[] flowPath, double currentLineHeight, double offsetX, double lineY, out double nextOffsetX)
        {
            var flow = GetCurrentFlowPathSection(flowPath, lineY) ?? new TextFlowPath(0, lineY, GuiElement.scaled(_designW), lineY + 10);
            var avail = Math.Max(1, flow.X2 - flow.X1);
            var fit = Math.Min(1d, avail / GuiElement.scaled(_designW));
            var width = GuiElement.scaled(_designW) * fit;
            var height = GuiElement.scaled(_designH) * fit;
            var x = flow.X1 + (avail - width) / 2;
            BoundsPerLine = [new LineRectangled(x, lineY, width, height)];
            nextOffsetX = flow.X1;
            return EnumCalcBoundsResult.Continue;
        }

        public override void ComposeElements(Context ctx, ImageSurface surface)
        {
            if (BoundsPerLine == null || BoundsPerLine.Length == 0 || BoundsPerLine[0].Width < 2)
                return;

            var box = BoundsPerLine[0];
            var grid = new Grid(box.X, box.Y, box.Width / _designW);
            ctx.Save();
            ctx.LineCap = LineCap.Butt;
            switch (_id)
            {
                case "mount":
                    DrawMount(ctx, grid);
                    break;
                case "order":
                    DrawOrder(ctx, grid);
                    break;
                default:
                    DrawNet(ctx, grid);
                    break;
            }

            ctx.Restore();
        }

        private static void DrawNet(Context ctx, Grid g)
        {
            const double y1 = 10;
            const double y2 = 84;
            const double h = 34;
            var controller = BoxAt(16, "escheme-controller");
            var cable = BoxAt(152, "escheme-cable");
            var drive = BoxAt(288, "escheme-drive");
            var terminals = BoxAt(16, "escheme-terminals");
            var face = BoxAt(152, "escheme-interface");
            var buses = BoxAt(288, "escheme-buses");
            LinkH(ctx, g, controller, cable, y1 + h / 2);
            LinkH(ctx, g, cable, drive, y1 + h / 2);
            var rail = y1 + h + 18;
            LinkV(ctx, g, Center(cable), y1 + h, rail);
            Line(ctx, g, Center(terminals), rail, Center(buses), rail);
            LinkV(ctx, g, Center(terminals), rail, y2);
            LinkV(ctx, g, Center(face), rail, y2);
            LinkV(ctx, g, Center(buses), rail, y2);
            DrawBox(ctx, g, controller, y1, h);
            DrawBox(ctx, g, cable, y1, h);
            DrawBox(ctx, g, drive, y1, h);
            DrawBox(ctx, g, terminals, y2, h);
            DrawBox(ctx, g, face, y2, h);
            DrawBox(ctx, g, buses, y2, h);
        }

        private static void DrawMount(Context ctx, Grid g)
        {
            const double x = 24;
            const double h = 34;
            const double y1 = 10;
            const double y2 = 66;
            const double y3 = 122;
            LinkV(ctx, g, 84, y1 + h, y2);
            LinkV(ctx, g, 84, y2 + h, y3);
            DrawBox(ctx, g, BoxAt(x, "escheme-panel"), y1, h);
            DrawBox(ctx, g, BoxAt(x, "escheme-strand"), y2, h);
            DrawBox(ctx, g, BoxAt(x, "escheme-machine"), y3, h);
        }

        private static void DrawOrder(Context ctx, Grid g)
        {
            const double h = 34;
            const double y0 = 8;
            const double y1 = 78;
            const double y2 = 148;
            var pattern = BoxAt(16, "escheme-pattern");
            var encode = BoxAt(152, "escheme-templates");
            var face = BoxAt(288, "escheme-interface");
            var assembler = BoxAt(16, "escheme-assembler");
            var machine = BoxAt(288, "escheme-machine");
            var cpu = BoxAt(152, "escheme-processor");
            LinkH(ctx, g, pattern, encode, y0 + h / 2);
            LinkH(ctx, g, encode, face, y0 + h / 2);
            var fork = y0 + h + 16;
            LinkV(ctx, g, Center(face), y0 + h, fork);
            Line(ctx, g, Center(assembler), fork, Center(machine), fork);
            LinkV(ctx, g, Center(assembler), fork, y1);
            LinkV(ctx, g, Center(machine), fork, y1);
            var join = y1 + h + 16;
            LinkV(ctx, g, Center(assembler), y1 + h, join);
            LinkV(ctx, g, Center(machine), y1 + h, join);
            Line(ctx, g, Center(assembler), join, Center(machine), join);
            LinkV(ctx, g, Center(cpu), join, y2);
            DrawBox(ctx, g, pattern, y0, h);
            DrawBox(ctx, g, encode, y0, h);
            DrawBox(ctx, g, face, y0, h);
            DrawBox(ctx, g, assembler, y1, h);
            DrawBox(ctx, g, machine, y1, h);
            DrawBox(ctx, g, cpu, y2, h);
        }

        private static (double X, string Key) BoxAt(double x, string key) => (x, key);

        private static double Center((double X, string Key) node) => node.X + 60;

        private static void LinkH(Context ctx, Grid g, (double X, string Key) left, (double X, string Key) right, double y)
            => Line(ctx, g, left.X + 120, y, right.X, y);

        private static void LinkV(Context ctx, Grid g, double x, double y1, double y2)
            => Line(ctx, g, x, y1, x, y2);

        private static void Line(Context ctx, Grid g, double x1, double y1, double x2, double y2)
        {
            ctx.NewPath();
            ctx.SetSourceRGBA(0.65, 0.93, 0.84, 1);
            ctx.LineWidth = Math.Max(1, 1.5 * g.S);
            ctx.MoveTo(g.X(x1), g.Y(y1));
            ctx.LineTo(g.X(x2), g.Y(y2));
            ctx.Stroke();
        }

        private static void DrawBox(Context ctx, Grid g, (double X, string Key) node, double y, double h)
        {
            var x = g.X(node.X);
            var top = g.Y(y);
            var w = 120 * g.S;
            var height = h * g.S;
            var radius = Math.Max(2, 4 * g.S);
            Round(ctx, x, top, w, height, radius);
            ctx.SetSourceRGBA(0.08, 0.14, 0.13, 0.92);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(0.65, 0.93, 0.84, 1);
            ctx.LineWidth = Math.Max(1, 1.25 * g.S);
            ctx.Stroke();
            Label(ctx, x, top, w, height, Lang.Get("electricalprogressivestorage:" + node.Key), g.S);
        }

        private static void Label(Context ctx, double x, double y, double w, double h, string text, double scale)
        {
            var pad = Math.Max(4, 8 * scale);
            var gui = RuntimeEnv.GUIScale <= 0 ? 1 : RuntimeEnv.GUIScale;
            TextExtents ext = default;
            for (var size = 14d; size >= 8; size -= 0.5)
            {
                var font = CairoFont.WhiteDetailText().WithFontSize((float)(size * scale / gui));
                font.SetupContext(ctx);
                ext = ctx.TextExtents(text);
                if (ext.Width <= w - pad || size <= 8)
                    break;
            }

            ctx.SetSourceRGBA(1, 1, 1, 1);
            ctx.MoveTo(x + (w - ext.Width) / 2 - ext.XBearing, y + (h - ext.Height) / 2 - ext.YBearing);
            ctx.ShowText(text);
        }

        private static void Round(Context ctx, double x, double y, double w, double h, double r)
        {
            ctx.NewPath();
            ctx.Arc(x + w - r, y + r, r, -Math.PI / 2, 0);
            ctx.Arc(x + w - r, y + h - r, r, 0, Math.PI / 2);
            ctx.Arc(x + r, y + h - r, r, Math.PI / 2, Math.PI);
            ctx.Arc(x + r, y + r, r, Math.PI, 3 * Math.PI / 2);
            ctx.ClosePath();
        }
    }
}
