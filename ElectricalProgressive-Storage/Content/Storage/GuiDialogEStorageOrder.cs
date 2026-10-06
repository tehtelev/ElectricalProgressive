using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Storage;

/// <summary>
/// Количество и план заказа. Рисуется внутри уже открытого терминала, не отдельным окном.
/// </summary>
public sealed class EStorageOrderPane
{
    private const double TitleH = 22;
    private const double CloseS = 16;

    private readonly ICoreClientAPI _capi;
    private readonly Action<int, bool> _send;
    private readonly Action _closePane;
    private readonly Action _rebuild;
    private readonly string _title;
    private readonly List<PlanRow> _rows = new();
    private int _count = 1;
    private int _page;
    private int _bytes;
    private int _scroll;
    private bool _busy;
    private bool _noCpu;
    private bool _failed;
    private bool _suppress;
    private string _note = "";
    private ElementBounds? _grid;
    private GuiComposer? _compo;

    public EStorageOrderPane(ICoreClientAPI capi, string title, Action<int, bool> send, Action closePane, Action rebuild)
    {
        _capi = capi;
        _title = title;
        _send = send;
        _closePane = closePane;
        _rebuild = rebuild;
        EStorageOrderSync.Current = this;
    }

    public ElementBounds? Close { get; private set; }

    public double WinW { get; private set; }

    public double WinH { get; private set; }

    public void Apply(EStorageOrderReply reply)
    {
        if (reply.Started)
        {
            _closePane();
            return;
        }

        _busy = false;
        _bytes = reply.Bytes;
        _noCpu = reply.NoCpu;
        _failed = reply.Failed;
        _note = reply.Text ?? "";
        _rows.Clear();
        ReadCells(reply.Cells);
        _scroll = 0;
        _page = 1;
        _rebuild();
    }

    public GuiComposer Compose()
    {
        return _page == 0 ? ComposeAmount() : ComposePlan();
    }

    public bool OnWheel(MouseWheelEventArgs args)
    {
        if (_page != 1 || _grid == null || MaxScroll() <= 0)
            return false;
        if (!_grid.PointInside(_capi.Input.MouseX, _capi.Input.MouseY))
            return false;

        var row = Math.Clamp(_scroll - Math.Sign(args.delta), 0, MaxScroll());
        if (row != _scroll)
        {
            _scroll = row;
            _rebuild();
        }

        args.SetHandled();
        return true;
    }

    private GuiComposer ComposeAmount()
    {
        var edge = GuiElementTermFrame.Inset;
        var pad = TermChrome.Pad;
        var gutter = TermChrome.Gutter;
        var blockW = 280.0;
        var headerH = pad + TitleH + pad;
        var qtyH = pad + 30 + pad;
        var qtyTop = edge + headerH + gutter;
        var btnY = qtyTop + qtyH + gutter;
        var contentH = btnY + 28 + 8 + 36 - edge;
        WinW = edge + blockW + edge;
        WinH = edge + contentH + edge;
        var closeX = edge + blockW - CloseS;
        var closeY = edge + pad + (TitleH - CloseS) / 2.0;
        Close = ElementBounds.Fixed(closeX, closeY, CloseS, CloseS);
        var title = ElementBounds.Fixed(edge + pad, edge + pad, closeX - 8 - (edge + pad), TitleH);
        var header = ElementBounds.Fixed(edge, edge, closeX - 8 - edge, headerH);
        var qtyY = qtyTop + pad;
        var minus = ElementBounds.Fixed(edge + pad, qtyY, 36, 30);
        var field = ElementBounds.Fixed(edge + pad + 44, qtyY, 140, 30);
        var plus = ElementBounds.Fixed(edge + pad + 192, qtyY, 36, 30);
        var qtyTray = ElementBounds.Fixed(edge, qtyTop, blockW, qtyH);
        var ok = ElementBounds.Fixed(edge + pad, btnY, 128, 28);
        var cancel = ElementBounds.Fixed(edge + pad + 136, btnY, 110, 28);
        var hint = ElementBounds.Fixed(edge + pad, btnY + 36, blockW - pad * 2, 36);
        var frame = ElementBounds.Fixed(0, 0, WinW, WinH);
        var bg = ElementBounds.Fill.WithFixedPadding(0);
        bg.BothSizing = ElementSizing.FitToChildren;
        bg.WithChildren(frame, Close);

        var compo = _capi.Gui
            .CreateCompo("estorageorder", DialogBounds())
            .BeginChildElements(bg)
            .AddStaticElement(new GuiElementTermFrame(_capi, frame, closeX, closeY, CloseS))
            .AddStaticElement(new GuiElementTermPanel(_capi, header))
            .AddStaticElement(new GuiElementTermPanel(_capi, qtyTray))
            .AddStaticText(_title, CairoFont.WhiteDetailText().WithFontSize(16), title)
            .AddSmallButton("−", () => Bump(-1), minus, EnumButtonStyle.Normal, "minus")
            .AddStaticElement(new GuiElementTermWell(_capi, field))
            .AddTextInput(field, OnQty, CairoFont.WhiteDetailText(), "qty")
            .AddSmallButton("+", () => Bump(1), plus, EnumButtonStyle.Normal, "plus")
            .AddSmallButton(Lang.Get("electricalprogressivestorage:estorage-order-next"), Ask, ok, EnumButtonStyle.Normal, "ok")
            .AddSmallButton(Lang.Get("electricalprogressivestorage:estorage-order-cancel"), () => { _closePane(); return true; }, cancel, EnumButtonStyle.Normal, "cancel")
            .AddDynamicText(_busy ? Lang.Get("electricalprogressivestorage:estorage-order-wait") : Lang.Get("electricalprogressivestorage:estorage-order-hint"), CairoFont.WhiteDetailText(), hint, "hint")
            .EndChildElements()
            .Compose();

        compo.GetTextInput("qty").SetValue(_count.ToString(), false);
        _compo = compo;
        return compo;
    }

    private GuiComposer ComposePlan()
    {
        var edge = GuiElementTermFrame.Inset;
        var pad = TermChrome.Pad;
        var gutter = TermChrome.Gutter;
        var gridW = GuiElementPlanGrid.Cols * GuiElementPlanGrid.CellW + (GuiElementPlanGrid.Cols - 1) * GuiElementPlanGrid.Gap;
        var gridH = VisibleHeight();
        var scrollW = 14.0;
        var blockW = Math.Max(280, gridW + 8 + scrollW + pad * 2);
        var showGrid = _rows.Count > 0 && !_failed;
        var headerH = pad + TitleH + pad;
        var bodyInner = showGrid ? gridH : 72;
        var bodyH = pad + bodyInner + pad;
        var bodyTop = edge + headerH + gutter;
        var gridY = bodyTop + pad;
        var cpuY = bodyTop + bodyH + gutter;
        var simY = cpuY + 26;
        var btnY = simY + 22;
        var contentH = btnY + 28 - edge;
        WinW = edge + blockW + edge;
        WinH = edge + contentH + edge;
        var closeX = edge + blockW - CloseS;
        var closeY = edge + pad + (TitleH - CloseS) / 2.0;
        Close = ElementBounds.Fixed(closeX, closeY, CloseS, CloseS);
        var title = ElementBounds.Fixed(edge + pad, edge + pad, closeX - 8 - (edge + pad), TitleH);
        var header = ElementBounds.Fixed(edge, edge, closeX - 8 - edge, headerH);
        _grid = ElementBounds.Fixed(edge + pad, gridY, gridW, gridH);
        var scroll = ElementBounds.Fixed(edge + pad + gridW + 8, gridY, scrollW, gridH);
        var bodyTray = ElementBounds.Fixed(edge, bodyTop, blockW, bodyH);
        var cpu = ElementBounds.Fixed(edge + pad, cpuY, blockW - pad * 2, 22);
        var sim = ElementBounds.Fixed(edge + pad, simY, blockW - pad * 2, 18);
        var cancel = ElementBounds.Fixed(edge + pad, btnY, 120, 28);
        ElementBounds? start = null;
        if (!_noCpu && !_failed)
            start = ElementBounds.Fixed(edge + blockW - pad - 120, btnY, 120, 28);
        var note = ElementBounds.Fixed(edge + pad, bodyTop + pad, blockW - pad * 2, 64);
        var frame = ElementBounds.Fixed(0, 0, WinW, WinH);
        var bg = ElementBounds.Fill.WithFixedPadding(0);
        bg.BothSizing = ElementSizing.FitToChildren;
        var kids = new List<ElementBounds> { frame, Close, title, cpu, sim, cancel };
        if (showGrid)
        {
            kids.Add(_grid);
            kids.Add(scroll);
        }
        else
            kids.Add(note);
        if (start != null)
            kids.Add(start);
        bg.WithChildren(kids.ToArray());

        var heading = Lang.Get("electricalprogressivestorage:estorage-order-plan", Group(_bytes));
        var cpuText = _noCpu
            ? Lang.Get("electricalprogressivestorage:estorage-order-nocpu")
            : Lang.Get("electricalprogressivestorage:estorage-order-cpu");
        var cpuFont = _noCpu
            ? CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center).WithColor([0.92, 0.38, 0.32, 1])
            : CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center);

        var compo = _capi.Gui
            .CreateCompo("estorageorder", DialogBounds())
            .BeginChildElements(bg)
            .AddStaticElement(new GuiElementTermFrame(_capi, frame, closeX, closeY, CloseS))
            .AddStaticElement(new GuiElementTermPanel(_capi, header))
            .AddStaticElement(new GuiElementTermPanel(_capi, bodyTray))
            .AddStaticText(heading, CairoFont.WhiteDetailText().WithFontSize(16), title);

        if (showGrid)
        {
            var visible = VisibleRows();
            compo.AddInteractiveElement(new GuiElementPlanGrid(_capi, _grid, visible));
            AddLabels(compo, _grid.fixedX, gridY, visible);
            compo.AddVerticalScrollbar(OnPlanScroll, scroll, "planScroll");
        }
        else
            compo.AddStaticText(string.IsNullOrEmpty(_note) ? cpuText : _note, CairoFont.WhiteDetailText(), note);

        compo.AddStaticElement(new GuiElementSolid(_capi, cpu, 0.06, 0.06, 0.06));
        compo.AddStaticText(cpuText, cpuFont, cpu);
        compo.AddStaticText(
            Lang.Get("electricalprogressivestorage:estorage-order-sim"),
            CairoFont.WhiteDetailText().WithOrientation(EnumTextOrientation.Center),
            sim);
        compo.AddSmallButton(Lang.Get("electricalprogressivestorage:estorage-order-cancel"), () => { _closePane(); return true; }, cancel, EnumButtonStyle.Normal, "cancel");
        if (start != null)
            compo.AddSmallButton(Lang.Get("electricalprogressivestorage:estorage-order-start"), Confirm, start, EnumButtonStyle.Normal, "ok");

        var composed = compo.EndChildElements().Compose();
        if (!showGrid)
            return composed;

        _suppress = true;
        var bar = composed.GetScrollbar("planScroll");
        var total = TotalHeight();
        bar?.SetHeights((float)gridH, (float)Math.Max(gridH, total));
        bar?.SetScrollbarPosition((int)(_scroll * RowStride()));
        _suppress = false;
        _compo = composed;
        return composed;
    }

    private void AddLabels(GuiComposer compo, double edge, double gridY, List<PlanRow> visible)
    {
        var textW = GuiElementPlanGrid.CellW - GuiElementPlanGrid.CellH - 8;
        for (var i = 0; i < visible.Count; i++)
        {
            var row = visible[i];
            var count = StackSizeTextPatch.Count(row.Count);
            string text;
            CairoFont font;
            if (row.Kind == 1)
            {
                text = Lang.Get("electricalprogressivestorage:estorage-order-make") + " " + count;
                font = CairoFont.WhiteDetailText().WithFontSize(14);
            }
            else if (row.Kind == 2)
            {
                text = Lang.Get("electricalprogressivestorage:estorage-order-miss", count);
                font = CairoFont.WhiteDetailText().WithFontSize(14).WithColor([0.92, 0.38, 0.32, 1]);
            }
            else if (row.Kind == 3)
            {
                text = Lang.Get("electricalprogressivestorage:estorage-order-liquid-have", LiquidCraft.LitresText(row.Count));
                font = CairoFont.WhiteDetailText().WithFontSize(14);
            }
            else if (row.Kind == 4)
            {
                text = Lang.Get("electricalprogressivestorage:estorage-order-liquid-miss", LiquidCraft.LitresText(row.Count));
                font = CairoFont.WhiteDetailText().WithFontSize(14).WithColor([0.92, 0.38, 0.32, 1]);
            }
            else
            {
                text = Lang.Get("electricalprogressivestorage:estorage-order-have", count);
                font = CairoFont.WhiteDetailText().WithFontSize(14);
            }

            var x = edge + (i % GuiElementPlanGrid.Cols) * (GuiElementPlanGrid.CellW + GuiElementPlanGrid.Gap);
            var y = gridY + (i / GuiElementPlanGrid.Cols) * RowStride();
            compo.AddStaticText(text, font, ElementBounds.Fixed(x + 6, y + 6, textW, GuiElementPlanGrid.CellH - 8));
        }
    }

    private List<PlanRow> VisibleRows()
    {
        var start = _scroll * GuiElementPlanGrid.Cols;
        var take = Math.Min(_rows.Count - start, GuiElementPlanGrid.VisRows * GuiElementPlanGrid.Cols);
        var list = new List<PlanRow>(Math.Max(0, take));
        for (var i = 0; i < take; i++)
            list.Add(_rows[start + i]);
        return list;
    }

    private static ElementBounds DialogBounds()
        => ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

    private static double VisibleHeight()
        => GuiElementPlanGrid.VisRows * GuiElementPlanGrid.CellH + (GuiElementPlanGrid.VisRows - 1) * GuiElementPlanGrid.Gap;

    private static double RowStride() => GuiElementPlanGrid.CellH + GuiElementPlanGrid.Gap;

    private double TotalHeight()
    {
        var rows = _rows.Count == 0 ? 1 : (_rows.Count + GuiElementPlanGrid.Cols - 1) / GuiElementPlanGrid.Cols;
        return rows * GuiElementPlanGrid.CellH + Math.Max(0, rows - 1) * GuiElementPlanGrid.Gap;
    }

    private int MaxScroll()
    {
        var rows = _rows.Count == 0 ? 0 : (_rows.Count + GuiElementPlanGrid.Cols - 1) / GuiElementPlanGrid.Cols;
        return Math.Max(0, rows - GuiElementPlanGrid.VisRows);
    }

    private void OnPlanScroll(float value)
    {
        if (_suppress)
            return;

        var row = Math.Clamp((int)Math.Round(value / RowStride()), 0, MaxScroll());
        if (row == _scroll)
            return;

        _scroll = row;
        _rebuild();
    }

    private static string Group(int value)
    {
        var text = Math.Max(0, value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (var i = text.Length - 3; i > 0; i -= 3)
            text = text.Insert(i, " ");
        return text;
    }

    private void ReadCells(byte[]? data)
    {
        if (data == null || data.Length == 0)
            return;

        var tree = new TreeAttribute();
        try
        {
            tree.FromBytes(data);
        }
        catch
        {
            return;
        }

        var n = tree.GetInt("n");
        for (var i = 0; i < n && i < 60; i++)
        {
            if (tree["s" + i] is not ItemstackAttribute { value: { } stack })
                continue;
            stack.ResolveBlockOrItem(_capi.World);
            if (stack.Collectible == null)
                continue;
            stack.StackSize = 1;
            _rows.Add(new PlanRow
            {
                Stack = stack,
                Count = Math.Max(1, tree.GetInt("c" + i)),
                Kind = tree.GetInt("k" + i)
            });
        }
    }

    private void OnQty(string text)
    {
        if (int.TryParse(text.Trim(), out var count))
            _count = Math.Clamp(count, 1, 100000);
    }

    private bool Bump(int delta)
    {
        ReadField();
        _count = Math.Clamp(_count + delta, 1, 100000);
        _rebuild();
        return true;
    }

    private bool Ask()
    {
        if (_busy)
            return true;

        ReadField();
        _busy = true;
        _rebuild();
        _send(_count, false);
        return true;
    }

    private bool Confirm()
    {
        if (_busy || _noCpu || _failed)
            return true;

        _busy = true;
        _send(_count, true);
        return true;
    }

    private void ReadField()
    {
        var text = _compo?.GetTextInput("qty")?.GetText() ?? "";
        if (int.TryParse(text.Trim(), out var count))
            _count = Math.Clamp(count, 1, 100000);
    }
}
