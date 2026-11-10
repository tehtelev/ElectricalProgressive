using System;
using System.Collections.Generic;
using Cairo;
using ElectricalProgressive.Patch;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Storage.Gui;

/// <summary>
/// Схема пособия: прямоугольники и линии. В квадрате иконка предмета, клик открывает страницу.
/// </summary>
public static class EStorageScheme
{
    private const string Domain = "electricalprogressivestorage:";

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

    private readonly struct Node
    {
        public readonly double X;
        public readonly double Y;
        public readonly double H;
        public readonly string Label;
        public readonly string[] Codes;

        public Node(double x, double y, double h, string label, params string[] codes)
        {
            X = x;
            Y = y;
            H = h;
            Label = label;
            Codes = codes;
        }

        public double Center => X + 60;
        public double Right => X + 120;
    }

    private sealed class SchemeComponent : RichTextComponentBase
    {
        private readonly string _id;
        private readonly double _designW;
        private readonly double _designH;
        private readonly Node[] _nodes;
        private List<HandbookItemstackTextComponent>[]? _icons;

        public SchemeComponent(ICoreClientAPI capi, string id) : base(capi)
        {
            _id = id;
            Float = EnumFloat.None;
            VerticalAlign = EnumVerticalAlign.Top;
            MouseOverCursor = "link";
            _nodes = Layout(id);
            (_designW, _designH) = id switch
            {
                "mount" => (168d, 192d),
                "order" => (424d, 208d),
                _ => (424d, 142d)
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
            Place(new Grid(x, lineY, width / _designW));
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

        public override void RenderInteractiveElements(float deltaTime, double renderX, double renderY, double renderZ)
        {
            if (_icons == null)
                return;
            foreach (var row in _icons)
            {
                foreach (var icon in row)
                    icon.RenderInteractiveElements(deltaTime, renderX, renderY, renderZ);
            }
        }

        public override void OnMouseDown(MouseEvent args)
        {
            var hit = Hit(args.X, args.Y);
            if (hit == null)
                return;
            hit.OnMouseDown(args);
            args.Handled = true;
        }

        public override bool UseMouseOverCursor(ElementBounds richtextBounds)
        {
            var relx = (int)(api.Input.MouseX - richtextBounds.absX);
            var rely = (int)(api.Input.MouseY - richtextBounds.absY);
            return Hit(relx, rely) != null;
        }

        public override void Dispose()
        {
            ClearIcons();
            base.Dispose();
        }

        private HandbookItemstackTextComponent? Hit(double x, double y)
        {
            if (_icons == null)
                return null;
            foreach (var row in _icons)
            {
                foreach (var icon in row)
                {
                    var line = icon.BoundsPerLine is { Length: > 0 } lines ? lines[0] : null;
                    if (line != null && line.PointInside(x, y))
                        return icon;
                }
            }

            return null;
        }

        private void Open(ItemStack stack)
        {
            var code = GuiHandbookItemStackPage.PageCodeForStack(stack);
            foreach (var dlg in api.Gui.OpenedGuis)
            {
                if (dlg is GuiDialogHandbook handbook)
                {
                    handbook.OpenDetailPageFor(code);
                    return;
                }
            }
        }

        private void Place(Grid g)
        {
            ClearIcons();
            var gui = RuntimeEnv.GUIScale <= 0 ? 1d : RuntimeEnv.GUIScale;
            var capi = (ICoreClientAPI)api;
            _icons = new List<HandbookItemstackTextComponent>[_nodes.Length];
            for (var i = 0; i < _nodes.Length; i++)
            {
                var node = _nodes[i];
                var stacks = new List<ItemStack>();
                foreach (var code in node.Codes)
                {
                    var stack = Resolve(capi, code);
                    if (stack != null)
                        stacks.Add(stack);
                }

                var list = new List<HandbookItemstackTextComponent>(stacks.Count);
                _icons[i] = list;
                if (stacks.Count == 0)
                    continue;

                var boxW = 120 * g.S;
                var boxH = node.H * g.S;
                var gap = 4 * g.S;
                var icon = Math.Min(boxH - 6 * g.S, (boxW - gap * (stacks.Count + 1)) / stacks.Count);
                if (icon < 8)
                    icon = 8;
                var row = icon * stacks.Count + gap * (stacks.Count - 1);
                var x0 = g.X(node.X) + (boxW - row) / 2;
                var y0 = g.Y(node.Y) + (boxH - icon) / 2;
                // Плоские панели заполняют квадрат: сетка равна ножницам.
                // Куб и жернова оставляем на 0.58 слота, как в справочнике, иначе изометрия срезается.
                var unscaled = Volumetric(node.Codes[0]) ? icon / gui : icon / (gui * 0.58);
                for (var n = 0; n < stacks.Count; n++)
                {
                    var comp = new HandbookItemstackTextComponent(capi, stacks[n], unscaled, 0, EnumFloat.Inline, Open);
                    var line = comp.BoundsPerLine[0];
                    line.X = x0 + n * (icon + gap);
                    line.Y = y0;
                    line.Width = icon;
                    line.Height = icon;
                    list.Add(comp);
                }
            }
        }

        private void ClearIcons()
        {
            if (_icons == null)
                return;
            foreach (var row in _icons)
            {
                foreach (var icon in row)
                    icon.Dispose();
            }

            _icons = null;
        }

        private void DrawNet(Context ctx, Grid g)
        {
            var controller = _nodes[0];
            var cable = _nodes[1];
            var drive = _nodes[2];
            var terminals = _nodes[3];
            var face = _nodes[4];
            var buses = _nodes[5];
            var mid = controller.Y + controller.H / 2;
            LinkH(ctx, g, controller, cable, mid);
            LinkH(ctx, g, cable, drive, mid);
            var rail = controller.Y + controller.H + 14;
            LinkV(ctx, g, cable.Center, controller.Y + controller.H, rail);
            Line(ctx, g, terminals.Center, rail, buses.Center, rail);
            LinkV(ctx, g, terminals.Center, rail, terminals.Y);
            LinkV(ctx, g, face.Center, rail, face.Y);
            LinkV(ctx, g, buses.Center, rail, buses.Y);
            DrawNode(ctx, g, 0);
            DrawNode(ctx, g, 1);
            DrawNode(ctx, g, 2);
            DrawNode(ctx, g, 3);
            DrawNode(ctx, g, 4);
            DrawNode(ctx, g, 5);
        }

        private void DrawMount(Context ctx, Grid g)
        {
            var panel = _nodes[0];
            var strand = _nodes[1];
            var machine = _nodes[2];
            LinkV(ctx, g, panel.Center, panel.Y + panel.H, strand.Y);
            LinkV(ctx, g, strand.Center, strand.Y + strand.H, machine.Y);
            DrawNode(ctx, g, 0);
            DrawNode(ctx, g, 1);
            DrawNode(ctx, g, 2);
        }

        private void DrawOrder(Context ctx, Grid g)
        {
            var pattern = _nodes[0];
            var encode = _nodes[1];
            var face = _nodes[2];
            var assembler = _nodes[3];
            var machine = _nodes[4];
            var cpu = _nodes[5];
            var mid = pattern.Y + pattern.H / 2;
            LinkH(ctx, g, pattern, encode, mid);
            LinkH(ctx, g, encode, face, mid);
            var fork = pattern.Y + pattern.H + 12;
            LinkV(ctx, g, face.Center, pattern.Y + pattern.H, fork);
            Line(ctx, g, assembler.Center, fork, machine.Center, fork);
            LinkV(ctx, g, assembler.Center, fork, assembler.Y);
            LinkV(ctx, g, machine.Center, fork, machine.Y);
            var join = assembler.Y + assembler.H + 12;
            LinkV(ctx, g, assembler.Center, assembler.Y + assembler.H, join);
            LinkV(ctx, g, machine.Center, machine.Y + machine.H, join);
            Line(ctx, g, assembler.Center, join, machine.Center, join);
            LinkV(ctx, g, cpu.Center, join, cpu.Y);
            for (var i = 0; i < _nodes.Length; i++)
                DrawNode(ctx, g, i);
        }

        private void DrawNode(Context ctx, Grid g, int index)
        {
            var node = _nodes[index];
            var x = g.X(node.X);
            var top = g.Y(node.Y);
            var w = 120 * g.S;
            var height = node.H * g.S;
            var radius = Math.Max(2, 4 * g.S);
            Round(ctx, x, top, w, height, radius);
            ctx.SetSourceRGBA(0.08, 0.14, 0.13, 0.92);
            ctx.FillPreserve();
            ctx.SetSourceRGBA(0.65, 0.93, 0.84, 1);
            ctx.LineWidth = Math.Max(1, 1.25 * g.S);
            ctx.Stroke();
            if (_icons == null || index >= _icons.Length || _icons[index].Count == 0)
                Label(ctx, x, top, w, height, Lang.Get("electricalprogressivestorage:" + node.Label), g.S);
        }
    }

    private static Node[] Layout(string id) => id switch
    {
        "mount" => Mount(),
        "order" => Order(),
        _ => Net()
    };

    private static Node[] Net()
    {
        const double h = 48;
        return
        [
            new Node(16, 8, h, "escheme-controller", Domain + "estoragecontroller-offline"),
            new Node(152, 8, h, "escheme-cable", Domain + "estoragecable"),
            new Node(288, 8, h, "escheme-drive", Domain + "estoragedrive-south"),
            new Node(16, 84, h, "escheme-terminals", Domain + "estorageterminal", Domain + "estorageliquidterminal", Domain + "estoragepatternterminal", Domain + "estoragecraftterminal"),
            new Node(152, 84, h, "escheme-interface", Domain + "estorageinterface"),
            new Node(288, 84, h, "escheme-buses", Domain + "estorageimport", Domain + "estorageexport")
        ];
    }

    private static Node[] Mount()
    {
        const double h = 48;
        return
        [
            new Node(24, 8, h, "escheme-panel", Domain + "estorageinterface"),
            new Node(24, 72, h, "escheme-strand", Domain + "estoragecable"),
            new Node(24, 136, h, "escheme-machine", "machine")
        ];
    }

    private static Node[] Order()
    {
        const double h = 48;
        return
        [
            new Node(16, 8, h, "escheme-pattern", Domain + "estoragepattern"),
            new Node(152, 8, h, "escheme-templates", Domain + "estoragepatternterminal"),
            new Node(288, 8, h, "escheme-interface", Domain + "estorageinterface"),
            new Node(16, 80, h, "escheme-assembler", Domain + "estorageassembler"),
            new Node(288, 80, h, "escheme-machine", "machine"),
            new Node(152, 152, h, "escheme-processor", Domain + "estorageprocessor-1k")
        ];
    }

    private static bool Volumetric(string code)
    {
        if (code == "machine")
            return true;
        return code.Contains("estoragecontroller")
            || code.Contains("estoragedrive")
            || code.Contains("estorageprocessor")
            || code.Contains("estorageassembler");
    }

    private static ItemStack? Resolve(ICoreClientAPI capi, string code)
    {
        if (code == "machine")
            return Machine(capi);

        var loc = new AssetLocation(code);
        var block = capi.World.GetBlock(loc);
        if (block != null && block.Id != 0)
            return new ItemStack(block);
        var item = capi.World.GetItem(loc);
        if (item != null && item.Id != 0)
            return new ItemStack(item);
        return null;
    }

    private static ItemStack? Machine(ICoreClientAPI capi)
    {
        var found = capi.World.SearchBlocks(new AssetLocation("game:quern-*"));
        if (found is { Length: > 0 } && found[0] != null && found[0].Id != 0)
            return new ItemStack(found[0]);
        var chest = capi.World.GetBlock(new AssetLocation("game:chest-east"));
        if (chest != null && chest.Id != 0)
            return new ItemStack(chest);
        return null;
    }

    private static void LinkH(Context ctx, Grid g, Node left, Node right, double y)
        => Line(ctx, g, left.Right, y, right.X, y);

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
