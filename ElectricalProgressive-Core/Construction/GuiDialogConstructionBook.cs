using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Construction;

public class GuiDialogConstructionBook : GuiDialog
{
    private const int Cols = 3;
    private const int VisibleRows = 3;
    private const double CellW = 148;
    private const double CellH = 128;
    private const double Gap = 8;
    private const double ScrollW = 16;
    private const double ScrollGap = 6;
    private const double InsetPad = 6;
    private const double LabelLineH = 15;
    private const double LabelPad = 4;
    private const double TitleClear = 36;
    private const double LabelHMax = LabelLineH * 2;
    // Size vs min(icon box). Inventory uses ~0.53; isometric mesh is larger than `size`.
    private const double IconFit = 0.72;
    private const double IconZ = 450;

    private readonly ItemSlot _slot;
    private readonly List<(Block Block, DummySlot Dummy, string Name, ElementBounds Cell)> _entries = [];
    private int _scrollRow;
    private int _maxScrollRow;
    private bool _suppressScroll;

    public override string ToggleKeyCombinationCode => null!;

    public GuiDialogConstructionBook(ICoreClientAPI capi, ItemSlot slot) : base(capi)
    {
        _slot = slot;
        Compose();
    }

    private void Compose()
    {
        var blocks = ConstructionCatalog.ListSchematics(capi.World);
        var rows = Math.Max(1, (Math.Max(1, blocks.Count) + Cols - 1) / Cols);
        _maxScrollRow = Math.Max(0, rows - VisibleRows);
        _scrollRow = Math.Clamp(_scrollRow, 0, _maxScrollRow);

        var gridW = Cols * CellW + (Cols - 1) * Gap;
        var visibleRows = Math.Min(VisibleRows, rows);
        var visibleH = visibleRows * CellH + Math.Max(0, visibleRows - 1) * Gap;
        var rowStride = CellH + Gap;
        var totalH = rows * CellH + Math.Max(0, rows - 1) * Gap;
        var needScroll = rows > VisibleRows;
        var contentW = gridW + (needScroll ? ScrollGap + ScrollW : 0);

        var grid = ElementBounds.Fixed(0, TitleClear, contentW, visibleH);
        var scroll = ElementBounds.Fixed(gridW + ScrollGap, TitleClear, ScrollW, visibleH);

        var bg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bg.BothSizing = ElementSizing.FitToChildren;
        bg.WithChildren(needScroll ? [grid, scroll] : [grid]);

        var dialog = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle);
        var labelFont = CairoFont.WhiteDetailText().WithFontSize(12).WithOrientation(EnumTextOrientation.Center);

        var compo = capi.Gui
            .CreateCompo("ep-construct-book", dialog)
            .AddShadedDialogBG(bg)
            .AddDialogTitleBar(Lang.Get("electricalprogressivecore:construction-book-title"), () => TryClose())
            .BeginChildElements(bg);

        if (blocks.Count == 0)
        {
            compo.AddStaticText(
                Lang.Get("electricalprogressivecore:construction-book-empty"),
                CairoFont.WhiteSmallText(),
                ElementBounds.Fixed(0, TitleClear, gridW, 40));
            SingleComposer = compo.EndChildElements().Compose();
            return;
        }

        var selectedCode = _slot.Itemstack?.Attributes?.GetString(ConstructionCatalog.AttrSelected);
        var first = _scrollRow * Cols;
        var last = Math.Min(blocks.Count, first + VisibleRows * Cols);

        for (var i = first; i < last; i++)
        {
            var block = blocks[i];
            var stack = new ItemStack(block);
            var name = stack.GetName();
            var col = (i - first) % Cols;
            var row = (i - first) / Cols;

            var x = col * (CellW + Gap);
            var y = TitleClear + row * rowStride;

            var cell = ElementBounds.Fixed(x, y, CellW, CellH);
            var wrapped = WrapName(name);
            var labelH = wrapped.Contains("<br>") ? LabelHMax : LabelLineH;
            var label = ElementBounds.Fixed(x + 4, y + CellH - labelH - LabelPad, CellW - 8, labelH);

            var inset = selectedCode != null && block.Code.ToString() == selectedCode ? 4 : 2;
            var visibleIndex = _entries.Count;

            _entries.Add((block, new DummySlot(stack), name, cell));

            compo.AddInset(cell, inset);
            compo.AddButton("", () =>
            {
                Pick(visibleIndex);
                return true;
            }, cell.FlatCopy());
            compo.AddRichtext(wrapped, labelFont, label);
            compo.AddHoverText(name, CairoFont.WhiteSmallText(), 320, cell.FlatCopy());
        }

        if (needScroll)
            compo.AddVerticalScrollbar(OnScroll, scroll, "schematicScroll");

        SingleComposer = compo.EndChildElements().Compose();

        if (!needScroll)
            return;

        _suppressScroll = true;
        var scrollbar = SingleComposer.GetScrollbar("schematicScroll");
        scrollbar?.SetHeights((float)visibleH, (float)totalH);
        scrollbar?.SetScrollbarPosition((int)(_scrollRow * rowStride));
        _suppressScroll = false;
    }

    private void OnScroll(float value)
    {
        if (_suppressScroll)
            return;

        var row = (int)Math.Round(value / (CellH + Gap));
        row = Math.Clamp(row, 0, _maxScrollRow);
        if (row == _scrollRow)
            return;

        _scrollRow = row;
        Rebuild();
    }

    private void Rebuild()
    {
        _entries.Clear();
        SingleComposer?.Dispose();
        Compose();
    }

    public override void OnMouseWheel(MouseWheelEventArgs args)
    {
        base.OnMouseWheel(args);
        if (!IsOpened() || args.IsHandled || _maxScrollRow <= 0)
            return;

        var scrollbar = SingleComposer?.GetScrollbar("schematicScroll");
        if (scrollbar == null || !SingleComposer.Bounds.PointInside(capi.Input.MouseX, capi.Input.MouseY))
            return;

        var row = Math.Clamp(_scrollRow - Math.Sign(args.delta), 0, _maxScrollRow);
        if (row == _scrollRow)
            return;

        _scrollRow = row;
        Rebuild();
        args.SetHandled();
    }

    private static string WrapName(string name)
    {
        const int maxLine = 18;
        if (string.IsNullOrEmpty(name) || name.Length <= maxLine)
            return name;

        var sp = name.LastIndexOf(' ', maxLine);
        if (sp < 7)
            return name[..(maxLine - 1)] + "…";

        var first = name[..sp];
        var rest = name[(sp + 1)..];
        if (rest.Length > maxLine)
            rest = rest[..(maxLine - 1)] + "…";
        return first + "<br>" + rest;
    }

    private void Pick(int index)
    {
        if (index < 0 || index >= _entries.Count || _slot.Itemstack == null)
            return;

        var code = _entries[index].Block.Code.ToString();
        ConstructionCatalog.SetSelected(_slot.Itemstack, code);
        // Не MarkDirty с клиента: ответ сервера со старым стаком затирает выбор.
        ConstructionBookSystem.SendSelect(capi, code);
        TryClose();
    }

    public override void OnRenderGUI(float deltaTime)
    {
        base.OnRenderGUI(deltaTime);

        var pad = GuiElement.scaled(InsetPad);
        var labelReserve = GuiElement.scaled(LabelHMax + LabelPad);

        for (var i = 0; i < _entries.Count; i++)
        {
            var cell = _entries[i].Cell;
            if (cell.OuterWidth < 8 || cell.OuterHeight < 8)
                continue;

            var x = cell.renderX + pad;
            var y = cell.renderY + pad;
            var w = cell.OuterWidth - pad * 2;
            var h = cell.OuterHeight - pad - labelReserve;
            if (w < 8 || h < 8)
                continue;

            var scissor = IconScissorBounds(x, y, w, h);
            capi.Render.PushScissor(scissor, true);
            try
            {
                var size = (float)(Math.Min(w, h) * FitFor(_entries[i].Block));
                capi.Render.RenderItemstackToGui(
                    _entries[i].Dummy,
                    x + w * 0.5,
                    y + h * 0.5,
                    IconZ,
                    size,
                    ColorUtil.WhiteArgb,
                    showStackSize: false);
            }
            finally
            {
                capi.Render.PopScissor();
            }
        }
    }

    private static double FitFor(Block block)
    {
        var part = block.FirstCodePart();
        if (part == "termoplastini" || part == "etermogenerator")
            return 0.52;
        return IconFit;
    }

    private ElementBounds IconScissorBounds(double x, double y, double w, double h)
    {
        var bounds = ElementBounds.FixedSize(
            (int)Math.Round(w / RuntimeEnv.GUIScale),
            (int)Math.Round(h / RuntimeEnv.GUIScale));
        bounds.ParentBounds = capi.Gui.WindowBounds;
        bounds.CalcWorldBounds();
        bounds.absFixedX = x;
        bounds.absFixedY = y;
        bounds.absInnerWidth = w;
        bounds.absInnerHeight = h;
        return bounds;
    }
}
