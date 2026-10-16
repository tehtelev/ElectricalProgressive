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
    private readonly ICoreClientAPI _capi;
    private readonly Action<int, bool> _send;
    private readonly Action _closePane;
    private readonly Action _rebuild;
    private readonly string _title;
    private readonly ItemStack _stack;
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

    public EStorageOrderPane(ICoreClientAPI capi, string title, ItemStack stack, Action<int, bool> send, Action closePane, Action rebuild)
    {
        _capi = capi;
        _title = title;
        _stack = stack;
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
        WinW = 380;
        WinH = 174;
        Close = Ae2Gui.Box(WinW - Ae2Gui.Margin - 20, 11, 20, 20);
        var title = Ae2Gui.Box(Ae2Gui.Margin + 2, 8, 280, 26);
        var hint = Ae2Gui.Box(Ae2Gui.Margin, Ae2Gui.Header + Ae2Gui.Gap, WinW - Ae2Gui.Margin * 2, 22);
        var minus = Ae2Gui.Box(Ae2Gui.Margin, 92, 40, 36);
        var plus = Ae2Gui.Box(Ae2Gui.Margin + 48, 92, 40, 36);
        var icon = Ae2Gui.Box(Ae2Gui.Margin + 102, 86, 48, 48);
        var field = Ae2Gui.Box(Ae2Gui.Margin + 162, 98, 90, 28);
        var next = Ae2Gui.Box(WinW - Ae2Gui.Margin - 100, 92, 100, 36);
        var frame = ElementBounds.Fixed(0, 0, WinW, WinH);
        var bg = ElementBounds.Fill.WithFixedPadding(0);
        bg.BothSizing = ElementSizing.FitToChildren;
        bg.WithChildren(frame, Close, field, next);

        var sheet = Ae2Gui.Window(_capi, frame, Close.fixedX, Close.fixedY, Close.fixedWidth);
        sheet.Fill(icon.fixedX - 1, icon.fixedY - 1, icon.fixedWidth + 2, icon.fixedHeight + 2, Ae2Gui.Ink[0], Ae2Gui.Ink[1], Ae2Gui.Ink[2]);
        sheet.Fill(icon.fixedX, icon.fixedY, icon.fixedWidth, icon.fixedHeight, Ae2Gui.Well[0], Ae2Gui.Well[1], Ae2Gui.Well[2]);
        var fieldSheet = new GuiElementAe2Sheet(_capi, frame);
        fieldSheet.Fill(field.fixedX - 2, field.fixedY - 2, field.fixedWidth + 4, field.fixedHeight + 4, Ae2Gui.FieldInk[0], Ae2Gui.FieldInk[1], Ae2Gui.FieldInk[2]);

        var compo = _capi.Gui
            .CreateCompo("estorageorder", DialogBounds())
            .BeginChildElements(bg)
            .AddStaticElement(sheet)
            .AddStaticText(_title, Ae2Gui.Title(), title)
            .AddDynamicText(_busy ? Lang.Get("electricalprogressivestorage:estorage-order-wait") : Lang.Get("electricalprogressivestorage:estorage-order-hint"), Ae2Gui.Label(), hint, "hint")
            .AddInteractiveElement(new GuiElementAe2Button(_capi, minus, "−", () => Bump(-1)), "minus")
            .AddInteractiveElement(new GuiElementAe2Button(_capi, plus, "+", () => Bump(1)), "plus")
            .AddTextInput(field, OnQty, Ae2Gui.FieldFont(), "qty")
            .AddStaticElement(fieldSheet)
            .AddInteractiveElement(new GuiElementAe2Stack(_capi, icon, _stack))
            .AddInteractiveElement(new GuiElementAe2Button(_capi, next, Lang.Get("electricalprogressivestorage:estorage-order-next"), () => Ask()), "ok")
            .EndChildElements()
            .Compose(false);

        compo.GetTextInput("qty").SetValue(_count.ToString(), false);
        _compo = compo;
        return compo;
    }

    private GuiComposer ComposePlan()
    {
        var gridW = GuiElementPlanGrid.Cols * GuiElementPlanGrid.CellW;
        var gridH = VisibleHeight();
        var showGrid = _rows.Count > 0 && !_failed;
        var scrollX = Ae2Gui.Margin + gridW + 8;
        WinW = scrollX + Ae2Gui.ScrollW + Ae2Gui.Margin;
        var top = Ae2Gui.Header + Ae2Gui.Gap;
        WinH = top + gridH + 8 + 32 + Ae2Gui.Margin;
        Close = Ae2Gui.Box(WinW - Ae2Gui.Margin - 20, 11, 20, 20);
        var title = Ae2Gui.Box(Ae2Gui.Margin + 2, 8, WinW - 80, 26);
        var gridBounds = showGrid ? Ae2Gui.Box(Ae2Gui.Margin, top, gridW, gridH) : null;
        _grid = gridBounds;
        var scroll = Ae2Gui.Box(scrollX, top, Ae2Gui.ScrollW, gridH);
        var note = Ae2Gui.Box(Ae2Gui.Margin, top + 12, WinW - Ae2Gui.Margin * 2, 120);
        var foot = top + gridH + 8;
        var cancel = Ae2Gui.Box(Ae2Gui.Margin, foot, 130, 32);
        ElementBounds? start = null;
        if (!_noCpu && !_failed)
            start = Ae2Gui.Box(WinW - Ae2Gui.Margin - 130, foot, 130, 32);
        var cpuRight = start != null ? start.fixedX - 8 : WinW - Ae2Gui.Margin;
        var cpu = Ae2Gui.Box(Ae2Gui.Margin + 138, foot, Math.Max(40, cpuRight - (Ae2Gui.Margin + 138)), 32);
        var frame = ElementBounds.Fixed(0, 0, WinW, WinH);
        var bg = ElementBounds.Fill.WithFixedPadding(0);
        bg.BothSizing = ElementSizing.FitToChildren;
        var kids = new List<ElementBounds> { frame, Close, title, cpu, cancel };
        if (gridBounds != null)
        {
            kids.Add(gridBounds);
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
            : Lang.Get("electricalprogressivestorage:estorage-order-sim");
        var cpuFont = _noCpu
            ? Ae2Gui.Label().WithOrientation(EnumTextOrientation.Center).WithColor(Ae2Gui.Warn)
            : Ae2Gui.Label().WithOrientation(EnumTextOrientation.Center);

        var sheet = Ae2Gui.Window(_capi, frame, Close.fixedX, Close.fixedY, Close.fixedWidth);
        if (gridBounds != null)
            sheet.Fill(scroll.fixedX, scroll.fixedY, scroll.fixedWidth, scroll.fixedHeight, Ae2Gui.Muted[0], Ae2Gui.Muted[1], Ae2Gui.Muted[2]);

        var compo = _capi.Gui
            .CreateCompo("estorageorder", DialogBounds())
            .BeginChildElements(bg)
            .AddStaticElement(sheet)
            .AddStaticText(heading, Ae2Gui.Title(), title);

        if (gridBounds != null)
        {
            var visible = VisibleRows();
            compo.AddInteractiveElement(new GuiElementPlanGrid(_capi, gridBounds, visible));
            AddLabels(compo, gridBounds.fixedX, gridBounds.fixedY, visible);
            compo.AddScroll(OnPlanScroll, scroll, "planScroll");
        }
        else
            compo.AddStaticText(string.IsNullOrEmpty(_note) ? cpuText : _note, Ae2Gui.Label(), note);

        compo.AddStaticText(cpuText, cpuFont, cpu);
        compo.AddInteractiveElement(new GuiElementAe2Button(_capi, cancel, Lang.Get("electricalprogressivestorage:estorage-order-cancel"), () => _closePane()), "cancel");
        if (start != null)
            compo.AddInteractiveElement(new GuiElementAe2Button(_capi, start, Lang.Get("electricalprogressivestorage:estorage-order-start"), () => Confirm()), "ok");

        var composed = compo.EndChildElements().Compose(false);
        if (gridBounds == null)
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
        var textW = GuiElementPlanGrid.CellW - 64;
        for (var i = 0; i < visible.Count; i++)
        {
            var row = visible[i];
            var count = StackSizeTextPatch.Count(row.Count);
            string text;
            CairoFont font;
            if (row.Kind == 1)
            {
                text = Lang.Get("electricalprogressivestorage:estorage-order-make") + " " + count;
                font = Ae2Gui.Label();
            }
            else if (row.Kind == 2)
            {
                text = Lang.Get("electricalprogressivestorage:estorage-order-miss", count);
                font = Ae2Gui.Label().WithColor(Ae2Gui.Warn);
            }
            else if (row.Kind == 3)
            {
                text = Lang.Get("electricalprogressivestorage:estorage-order-liquid-have", LiquidCraft.LitresText(row.Count));
                font = Ae2Gui.Label();
            }
            else if (row.Kind == 4)
            {
                text = Lang.Get("electricalprogressivestorage:estorage-order-liquid-miss", LiquidCraft.LitresText(row.Count));
                font = Ae2Gui.Label().WithColor(Ae2Gui.Warn);
            }
            else
            {
                text = Lang.Get("electricalprogressivestorage:estorage-order-have", count);
                font = Ae2Gui.Label();
            }

            var x = edge + (i % GuiElementPlanGrid.Cols) * (GuiElementPlanGrid.CellW + GuiElementPlanGrid.Gap);
            var y = gridY + (i / GuiElementPlanGrid.Cols) * RowStride();
            compo.AddStaticText(text, font, ElementBounds.Fixed(x + 8, y + 8, textW, GuiElementPlanGrid.CellH - 16));
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
