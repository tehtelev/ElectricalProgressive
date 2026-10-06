using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage;

public sealed class CraftStatusLine
{
    public ItemStack Stack = null!;
    public int Count;
}

/// <summary>
/// Статус крафта процессора. Рамка та же, что у интерфейса.
/// </summary>
public sealed class GuiDialogEStorageProcessor : GuiDialogBlockEntity
{
    private const double TitleH = 22;
    private const double CloseS = 16;
    private const double ScrollW = 16;

    private readonly List<CraftStatusLine> _jobs = new();
    private ElementBounds? _close;
    private ElementBounds? _grid;
    private double _winW;
    private double _winH;
    private double _elapsed;
    private long _stamp;
    private long _asked;
    private int _scroll;
    private int _selected = -1;
    private bool _drag;
    private bool _suppress;
    private int _dragX;
    private int _dragY;
    private string _clock = "";

    public GuiDialogEStorageProcessor(string dialogTitle, BlockPos pos, ICoreClientAPI capi)
        : base(dialogTitle, pos, capi)
    {
        if (IsDuplicate)
            return;

        SetupDialog();
    }

    public static void Toggle(ICoreClientAPI capi, BlockPos pos)
    {
        GuiDialogEStorageProcessor? same = null;
        var others = new List<GuiDialogEStorageProcessor>();
        foreach (var gui in capi.Gui.OpenedGuis)
        {
            if (gui is not GuiDialogEStorageProcessor open)
                continue;
            if (ProcessorCluster.SamePos(open.BlockEntityPosition, pos))
                same = open;
            else
                others.Add(open);
        }

        if (same != null)
        {
            same.TryClose();
            return;
        }

        foreach (var other in others)
            other.TryClose();

        new GuiDialogEStorageProcessor(Lang.Get("electricalprogressivestorage:estorage-cpu-status", "00:00:00"), pos.Copy(), capi).TryOpen();
    }

    public static void Deliver(ICoreClientAPI capi, BlockPos pos, byte[] data)
    {
        foreach (var gui in capi.Gui.OpenedGuis)
        {
            if (gui is GuiDialogEStorageProcessor open && ProcessorCluster.SamePos(open.BlockEntityPosition, pos))
                open.Apply(data);
        }
    }

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        Ask();
    }

    public override void OnRenderGUI(float deltaTime)
    {
        var now = capi.ElapsedMilliseconds;
        if (now - _asked >= 450)
            Ask();

        var text = ClockText();
        if (text != _clock)
        {
            _clock = text;
            SingleComposer?.GetDynamicText("clock")?.SetNewText(text);
        }

        base.OnRenderGUI(deltaTime);
    }

    public override void OnMouseDown(MouseEvent args)
    {
        if (_close != null && _close.PointInside(capi.Input.MouseX, capi.Input.MouseY))
        {
            args.Handled = true;
            TryClose();
            return;
        }

        if (HitCell(out var index))
        {
            args.Handled = true;
            if (index < _jobs.Count && index != _selected)
            {
                _selected = index;
                SetupDialog();
            }

            return;
        }

        base.OnMouseDown(args);
        if (!args.Handled && OnFrame())
            BeginDrag();
    }

    public override void OnMouseMove(MouseEvent args)
    {
        if (_drag && SingleComposer != null)
        {
            var bounds = SingleComposer.Bounds;
            bounds.fixedX += (args.X - _dragX) / RuntimeEnv.GUIScale;
            bounds.fixedY += (args.Y - _dragY) / RuntimeEnv.GUIScale;
            _dragX = args.X;
            _dragY = args.Y;
            bounds.CalcWorldBounds();
        }

        base.OnMouseMove(args);
    }

    public override void OnMouseUp(MouseEvent args)
    {
        if (_drag && SingleComposer != null)
            capi.Gui.SetDialogPosition(SingleComposer.DialogName, new Vec2i((int)SingleComposer.Bounds.fixedX, (int)SingleComposer.Bounds.fixedY));
        _drag = false;
        base.OnMouseUp(args);
    }

    public override void OnMouseWheel(MouseWheelEventArgs args)
    {
        if (_grid != null && _grid.PointInside(capi.Input.MouseX, capi.Input.MouseY) && MaxScroll() > 0)
        {
            var row = Math.Clamp(_scroll - Math.Sign(args.delta), 0, MaxScroll());
            if (row != _scroll)
            {
                _scroll = row;
                SetupDialog();
            }

            args.SetHandled();
            return;
        }

        base.OnMouseWheel(args);
    }

    private void Apply(byte[] data)
    {
        if (!IsOpened())
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

        var next = new List<CraftStatusLine>();
        var n = Math.Clamp(tree.GetInt("n"), 0, 64);
        for (var i = 0; i < n; i++)
        {
            if (tree["s" + i] is not ItemstackAttribute { value: { } raw })
                continue;
            if (raw.Collectible == null && !raw.ResolveBlockOrItem(capi.World))
                continue;
            if (raw.Collectible == null)
                continue;

            var copy = raw.Clone();
            copy.StackSize = 1;
            next.Add(new CraftStatusLine { Stack = copy, Count = Math.Max(1, tree.GetInt("c" + i)) });
        }

        _elapsed = Math.Max(0, tree.GetDouble("e0"));
        _stamp = capi.ElapsedMilliseconds;
        var changed = next.Count != _jobs.Count;
        if (!changed)
        {
            for (var i = 0; i < next.Count; i++)
            {
                if (next[i].Count != _jobs[i].Count || !StorageAccess.Same(capi.World, next[i].Stack, _jobs[i].Stack))
                {
                    changed = true;
                    break;
                }
            }
        }

        _jobs.Clear();
        _jobs.AddRange(next);
        if (_jobs.Count == 0)
            _selected = -1;
        else if (_selected < 0 || _selected >= _jobs.Count)
        {
            _selected = 0;
            changed = true;
        }

        if (_scroll > MaxScroll())
        {
            _scroll = MaxScroll();
            changed = true;
        }

        if (changed || _grid == null)
            SetupDialog();
    }

    private void Ask()
    {
        _asked = capi.ElapsedMilliseconds;
        capi.Network.SendBlockEntityPacket(BlockEntityPosition, BlockEntityEStorageProcessor.StatusPacket, Array.Empty<byte>());
    }

    private bool Cancel()
    {
        if (_selected < 0 || _selected >= _jobs.Count)
            return true;

        capi.Network.SendBlockEntityPacket(BlockEntityPosition, BlockEntityEStorageProcessor.CancelPacket, BitConverter.GetBytes(_selected));
        return true;
    }

    private string ClockText()
    {
        var sec = 0;
        if (_jobs.Count > 0)
        {
            var extra = (capi.ElapsedMilliseconds - _stamp) / 1000.0;
            if (extra < 0)
                extra = 0;
            if (extra > 2)
                extra = 2;
            sec = (int)Math.Floor(_elapsed + extra);
            if (sec < 0)
                sec = 0;
        }

        var text = (sec / 3600).ToString("D2") + ":" + ((sec / 60) % 60).ToString("D2") + ":" + (sec % 60).ToString("D2");
        return Lang.Get("electricalprogressivestorage:estorage-cpu-status", text);
    }

    private void SetupDialog()
    {
        var edge = GuiElementTermFrame.Inset;
        var pad = TermChrome.Pad;
        var gutter = TermChrome.Gutter;
        var gridW = GuiElementCraftGrid.CellW * GuiElementCraftGrid.Cols;
        var gridH = GuiElementCraftGrid.CellH * GuiElementCraftGrid.VisRows;
        var blockW = pad + gridW + 8 + ScrollW + pad;
        var headerH = pad + TitleH + pad;
        var gridBlockH = pad + gridH + pad;
        var gridTop = edge + headerH + gutter;
        var gridY = gridTop + pad;
        var btnY = gridTop + gridBlockH + gutter;
        var contentH = btnY + 28 - edge;
        _winW = edge + blockW + edge;
        _winH = edge + contentH + edge;
        var closeX = edge + blockW - CloseS;
        var closeY = edge + pad + (TitleH - CloseS) / 2.0;
        var title = ElementBounds.Fixed(edge + pad, edge + pad, closeX - 8 - (edge + pad), TitleH);
        var header = ElementBounds.Fixed(edge, edge, closeX - 8 - edge, headerH);
        _close = ElementBounds.Fixed(closeX, closeY, CloseS, CloseS);
        _grid = ElementBounds.Fixed(edge + pad, gridY, gridW, gridH);
        var scroll = ElementBounds.Fixed(edge + pad + gridW + 8, gridY, ScrollW, gridH);
        var jobsTray = ElementBounds.Fixed(edge, gridTop, blockW, gridBlockH);
        var cancel = ElementBounds.Fixed(edge + blockW - 120, btnY, 120, 28);
        var frame = ElementBounds.Fixed(0, 0, _winW, _winH);
        var bg = ElementBounds.Fill.WithFixedPadding(0);
        bg.BothSizing = ElementSizing.FitToChildren;
        bg.WithChildren(frame, _close, title, _grid, scroll, cancel);

        _clock = ClockText();
        var dialog = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

        ClearComposers();
        var compo = capi.Gui
            .CreateCompo("estorageprocessor" + BlockEntityPosition, dialog)
            .BeginChildElements(bg)
            .AddStaticElement(new GuiElementTermFrame(capi, frame, closeX, closeY, CloseS))
            .AddStaticElement(new GuiElementTermPanel(capi, header))
            .AddStaticElement(new GuiElementTermPanel(capi, jobsTray))
            .AddDynamicText(_clock, CairoFont.WhiteDetailText().WithFontSize(16), title, "clock")
            .AddInteractiveElement(new GuiElementCraftGrid(capi, _grid, _jobs, _scroll, _selected))
            .AddVerticalScrollbar(OnScroll, scroll, "cpuScroll");

        var first = _scroll * GuiElementCraftGrid.Cols;
        var shown = Math.Min(_jobs.Count - first, GuiElementCraftGrid.Cols * GuiElementCraftGrid.VisRows);
        if (shown < 0)
            shown = 0;
        for (var i = 0; i < shown; i++)
        {
            var line = _jobs[first + i];
            var col = i % GuiElementCraftGrid.Cols;
            var row = i / GuiElementCraftGrid.Cols;
            var x = _grid.fixedX + col * GuiElementCraftGrid.CellW;
            var y = gridY + row * GuiElementCraftGrid.CellH;
            compo.AddStaticText(
                Lang.Get("electricalprogressivestorage:estorage-cpu-craft", StackSizeTextPatch.Count(line.Count)),
                CairoFont.WhiteDetailText().WithFontSize(15),
                ElementBounds.Fixed(x + 8, y + 8, 100, GuiElementCraftGrid.CellH - 12));
        }

        compo.AddSmallButton(Lang.Get("electricalprogressivestorage:estorage-order-cancel"), Cancel, cancel, EnumButtonStyle.Normal, "cancel");
        var composed = compo.EndChildElements().Compose();
        _suppress = true;
        var bar = composed.GetScrollbar("cpuScroll");
        var total = Math.Max(gridH, RowsOf(_jobs.Count) * GuiElementCraftGrid.CellH);
        bar?.SetHeights((float)gridH, (float)total);
        bar?.SetScrollbarPosition((int)(_scroll * GuiElementCraftGrid.CellH));
        _suppress = false;
        SingleComposer = composed;
    }

    private void OnScroll(float value)
    {
        if (_suppress)
            return;

        var row = Math.Clamp((int)Math.Round(value / GuiElementCraftGrid.CellH), 0, MaxScroll());
        if (row == _scroll)
            return;

        _scroll = row;
        SetupDialog();
    }

    private int MaxScroll() => Math.Max(0, RowsOf(_jobs.Count) - GuiElementCraftGrid.VisRows);

    private static int RowsOf(int count)
    {
        if (count <= 0)
            return 0;
        return (count + GuiElementCraftGrid.Cols - 1) / GuiElementCraftGrid.Cols;
    }

    private bool HitCell(out int index)
    {
        index = -1;
        if (_grid == null || !_grid.PointInside(capi.Input.MouseX, capi.Input.MouseY))
            return false;

        var scale = RuntimeEnv.GUIScale;
        var localX = (capi.Input.MouseX - _grid.renderX) / scale;
        var localY = (capi.Input.MouseY - _grid.renderY) / scale;
        var col = (int)(localX / GuiElementCraftGrid.CellW);
        var row = (int)(localY / GuiElementCraftGrid.CellH);
        if (col < 0 || col >= GuiElementCraftGrid.Cols || row < 0 || row >= GuiElementCraftGrid.VisRows)
            return false;
        if (localX - col * GuiElementCraftGrid.CellW >= GuiElementCraftGrid.CellW)
            return false;

        index = (_scroll + row) * GuiElementCraftGrid.Cols + col;
        return true;
    }

    private void BeginDrag()
    {
        var bounds = SingleComposer?.Bounds;
        if (bounds == null)
            return;

        _drag = true;
        _dragX = capi.Input.MouseX;
        _dragY = capi.Input.MouseY;
        if (bounds.Alignment == EnumDialogArea.None)
            return;

        bounds.Alignment = EnumDialogArea.None;
        bounds.fixedOffsetX = 0;
        bounds.fixedOffsetY = 0;
        bounds.fixedX = bounds.absX / RuntimeEnv.GUIScale;
        bounds.fixedY = bounds.absY / RuntimeEnv.GUIScale;
        bounds.absMarginX = 0;
        bounds.absMarginY = 0;
        bounds.MarkDirtyRecursive();
        bounds.CalcWorldBounds();
    }

    private bool OnFrame()
    {
        var bounds = SingleComposer?.Bounds;
        if (bounds == null || !bounds.PointInside(capi.Input.MouseX, capi.Input.MouseY))
            return false;

        var scale = RuntimeEnv.GUIScale;
        var x = (capi.Input.MouseX - bounds.renderX) / scale;
        var y = (capi.Input.MouseY - bounds.renderY) / scale;
        var edge = GuiElementTermFrame.Inset;
        return x < edge || y < edge || x > _winW - edge || y > _winH - edge;
    }

}

/// <summary>
/// Сетка статуса: пустые ячейки тоже рисуются, выбранная зелёная, иконка справа от надписи.
/// </summary>
public sealed class GuiElementCraftGrid : GuiElement
{
    public const double CellW = 168;
    public const double CellH = 36;
    public const int Cols = 2;
    public const int VisRows = 7;

    private readonly List<DummySlot?> _slots = new();
    private readonly int _localSelected;

    public GuiElementCraftGrid(ICoreClientAPI capi, ElementBounds bounds, IReadOnlyList<CraftStatusLine> jobs, int scroll, int selected)
        : base(capi, bounds)
    {
        var first = scroll * Cols;
        _localSelected = selected >= first && selected < first + Cols * VisRows ? selected - first : -1;
        for (var i = 0; i < Cols * VisRows; i++)
        {
            var index = first + i;
            if (index < 0 || index >= jobs.Count)
            {
                _slots.Add(null);
                continue;
            }

            var copy = jobs[index].Stack.Clone();
            copy.StackSize = 1;
            _slots.Add(new DummySlot(copy));
        }
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        var scale = RuntimeEnv.GUIScale;
        var cellW = CellW * scale;
        var cellH = CellH * scale;
        for (var i = 0; i < _slots.Count; i++)
        {
            var x = Bounds.drawX + (i % Cols) * cellW;
            var y = Bounds.drawY + (i / Cols) * cellH;
            var green = i == _localSelected && _slots[i] != null;
            if (green)
            {
                TermChrome.Raised(ctx, x, y, cellW, cellH, false);
                ctx.SetSourceRGBA(0.30, 0.62, 0.32, 0.38);
                ctx.Rectangle(x + 3, y + 3, Math.Max(1, cellW - 6), Math.Max(1, cellH - 6));
                ctx.Fill();
            }
            else
                TermChrome.Inset(ctx, x, y, cellW, cellH, 0.07, 0.06, 0.05);
        }
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        Bounds.CalcWorldBounds();
        var scale = RuntimeEnv.GUIScale;
        var icon = (float)scaled(28);
        var half = scaled(CellH) * 0.5;
        for (var i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            if (slot == null)
                continue;

            var cx = Bounds.renderX + (i % Cols) * CellW * scale + scaled(128);
            var cy = Bounds.renderY + (i / Cols) * CellH * scale + half;
            // 90, как у слота инвентаря: выше подложки (50) и внутри слоя диалога (ZSize 150).
            api.Render.RenderItemstackToGui(slot, cx, cy, 90, icon, ColorUtil.WhiteArgb, true, false, false);
        }
    }
}
