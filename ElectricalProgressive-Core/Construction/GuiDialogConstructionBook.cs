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
    private const double CellW = 148;
    private const double CellH = 128;
    private const double Gap = 8;
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

    public override string ToggleKeyCombinationCode => null!;

    public GuiDialogConstructionBook(ICoreClientAPI capi, ItemSlot slot) : base(capi)
    {
        _slot = slot;
        Compose();
    }

    private void Compose()
    {
        var blocks = ConstructionCatalog.ListSchematics(capi.World);
        var count = Math.Max(1, blocks.Count);
        var rows = (count + Cols - 1) / Cols;
        var gridW = Cols * CellW + (Cols - 1) * Gap;
        var gridH = rows * CellH + Math.Max(0, rows - 1) * Gap;

        var inner = ElementBounds.Fixed(0, TitleClear, gridW, gridH);
        var bg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bg.BothSizing = ElementSizing.FitToChildren;
        bg.WithChildren(inner);

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

        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var stack = new ItemStack(block);
            var name = stack.GetName();
            var col = i % Cols;
            var row = i / Cols;

            var x = col * (CellW + Gap);
            var y = TitleClear + row * (CellH + Gap);

            var cell = ElementBounds.Fixed(x, y, CellW, CellH);
            var wrapped = WrapName(name);
            var labelH = wrapped.Contains("<br>") ? LabelHMax : LabelLineH;
            var label = ElementBounds.Fixed(x + 4, y + CellH - labelH - LabelPad, CellW - 8, labelH);

            var idx = i;
            var inset = selectedCode != null && block.Code.ToString() == selectedCode ? 4 : 2;

            _entries.Add((block, new DummySlot(stack), name, cell));

            compo.AddInset(cell, inset);
            compo.AddButton("", () =>
            {
                Pick(idx);
                return true;
            }, cell.FlatCopy());
            compo.AddRichtext(wrapped, labelFont, label);
            compo.AddHoverText(name, CairoFont.WhiteSmallText(), 320, cell.FlatCopy());
        }

        SingleComposer = compo.EndChildElements().Compose();
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
        _slot.MarkDirty();
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
