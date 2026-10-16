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
/// Статус крафта процессора. Своя панель: три колонки ячеек и отмена справа снизу.
/// </summary>
public sealed class GuiDialogEStorageProcessor : GuiDialogBlockEntity
{
    private readonly List<CraftStatusLine> _jobs = new();
    private ElementBounds? _close;
    private ElementBounds? _grid;
    private double _winW;
    private double _winH;
    private double _head;
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
        var gridW = GuiElementCraftGrid.Cols * GuiElementCraftGrid.CellW;
        var gridH = GuiElementCraftGrid.VisRows * GuiElementCraftGrid.CellH;
        var top = Ae2Gui.Header + Ae2Gui.Gap;
        _grid = Ae2Gui.Box(Ae2Gui.Margin, top, gridW, gridH);
        var scroll = Ae2Gui.Box(_grid.fixedX + gridW + 8, top, Ae2Gui.ScrollW, gridH);
        _winW = scroll.fixedX + scroll.fixedWidth + Ae2Gui.Margin;
        _winH = top + gridH + 8 + 32 + Ae2Gui.Margin;
        _head = Ae2Gui.Header;
        _close = Ae2Gui.Box(_winW - Ae2Gui.Margin - 20, 11, 20, 20);
        var title = Ae2Gui.Box(Ae2Gui.Margin + 2, 8, _winW - 80, 26);
        var cancel = Ae2Gui.Box(_winW - Ae2Gui.Margin - 130, top + gridH + 8, 130, 32);
        var frame = ElementBounds.Fixed(0, 0, _winW, _winH);
        var bg = ElementBounds.Fill.WithFixedPadding(0);
        bg.BothSizing = ElementSizing.FitToChildren;
        bg.WithChildren(frame, _close, title, _grid, scroll, cancel);

        _clock = ClockText();
        var sheet = Ae2Gui.Window(capi, frame, _close.fixedX, _close.fixedY, _close.fixedWidth);
        sheet.Fill(scroll.fixedX, scroll.fixedY, scroll.fixedWidth, scroll.fixedHeight, Ae2Gui.Muted[0], Ae2Gui.Muted[1], Ae2Gui.Muted[2]);

        ClearComposers();
        var compo = capi.Gui
            .CreateCompo("estorageprocessor" + BlockEntityPosition, Ae2Gui.Dialog())
            .BeginChildElements(bg)
            .AddStaticElement(sheet)
            .AddDynamicText(_clock, Ae2Gui.Title(), title, "clock")
            .AddInteractiveElement(new GuiElementCraftGrid(capi, _grid, _jobs, _scroll, _selected))
            .AddScroll(OnScroll, scroll, "cpuScroll");

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
            var y = _grid.fixedY + row * GuiElementCraftGrid.CellH;
            compo.AddStaticText(
                Lang.Get("electricalprogressivestorage:estorage-cpu-craft", StackSizeTextPatch.Count(line.Count)),
                Ae2Gui.Label(),
                ElementBounds.Fixed(x + 8, y + 8, GuiElementCraftGrid.CellW - 64, GuiElementCraftGrid.CellH - 16));
        }

        compo.AddInteractiveElement(new GuiElementAe2Button(capi, cancel, Lang.Get("electricalprogressivestorage:estorage-order-cancel"), () => Cancel()), "cancel");
        var composed = compo.EndChildElements().Compose(false);
        _suppress = true;
        var bar = composed.GetScrollbar("cpuScroll");
        var viewH = _grid.fixedHeight;
        var total = Math.Max(viewH, RowsOf(_jobs.Count) * GuiElementCraftGrid.CellH);
        bar?.SetHeights((float)viewH, (float)total);
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
        return y >= 0 && y < _head && x >= 0 && x <= _winW;
    }

}

/// <summary>
/// Сетка статуса: пустые ячейки тоже рисуются, выбранная зелёная, иконка справа от надписи.
/// </summary>
public sealed class GuiElementCraftGrid : GuiElement
{
    public const double CellW = 156;
    public const double CellH = 48;
    public const int Cols = 3;
    public const int VisRows = 6;

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
        var cellW = scaled(CellW);
        var cellH = scaled(CellH);
        var edge = Math.Max(1, scaled(1));
        var prev = ctx.Antialias;
        ctx.Antialias = Antialias.None;
        for (var i = 0; i < _slots.Count; i++)
        {
            var x = Bounds.drawX + (i % Cols) * cellW;
            var y = Bounds.drawY + (i / Cols) * cellH;
            ctx.SetSourceRGB(Ae2Gui.Well[0], Ae2Gui.Well[1], Ae2Gui.Well[2]);
            ctx.Rectangle(x, y, cellW, cellH);
            ctx.Fill();
            ctx.SetSourceRGBA(Ae2Gui.Ink[0], Ae2Gui.Ink[1], Ae2Gui.Ink[2], 0.55);
            ctx.Rectangle(x, y, cellW, edge);
            ctx.Rectangle(x, y + cellH - edge, cellW, edge);
            ctx.Rectangle(x, y, edge, cellH);
            ctx.Rectangle(x + cellW - edge, y, edge, cellH);
            ctx.Fill();
            if (i != _localSelected || _slots[i] == null)
                continue;

            ctx.SetSourceRGBA(0.67, 0.91, 1, 0.45);
            ctx.Rectangle(x + edge, y + edge, Math.Max(1, cellW - 2 * edge), Math.Max(1, cellH - 2 * edge));
            ctx.Fill();
        }

        ctx.Antialias = prev;
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        Bounds.CalcWorldBounds();
        var scale = RuntimeEnv.GUIScale;
        var icon = (float)scaled(GuiElementPassiveItemSlot.unscaledItemSize);
        var half = scaled(CellH) * 0.5;
        for (var i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            if (slot == null)
                continue;

            var cx = Bounds.renderX + (i % Cols) * CellW * scale + scaled(CellW - 8) - icon * 0.5;
            var cy = Bounds.renderY + (i / Cols) * CellH * scale + half;
            // 90, как у слота инвентаря: выше подложки (50) и внутри слоя диалога (ZSize 150).
            api.Render.RenderItemstackToGui(slot, cx, cy, 90, icon, ColorUtil.WhiteArgb, true, false, false);
        }
    }
}
