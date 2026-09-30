using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Cairo;
using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage;

public class GuiDialogEStorageTerminal : GuiDialogBlockEntity
{
    private enum SortMode
    {
        Mod,
        Name,
        Count
    }

    private const int Cols = 10;
    private const int VisibleRows = 11;
    private const double ScrollW = 16;
    private const double ScrollGap = 4;
    private const double SearchH = 22;
    private const double MeterH = 56;
    private const double MeterGap = 6;
    private const double MeterPad = 6;
    private const double BarH = 16;
    private const double IconW = 44;
    private const double CloseS = 16;

    private static readonly FieldInfo SearchRightSpace = typeof(GuiElementEditableTextBase).GetField(
        "rightSpacing", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

    private static readonly PropertyInfo SearchCaret = typeof(GuiElementEditableTextBase).GetProperty(
        "CaretPosInLine", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

    private static readonly double[] ChannelColor = [0.77, 0.53, 0.23, 1];
    private static readonly double[] PowerColor = [0.43, 0.60, 0.31, 1];
    private static readonly double[] ItemColor = [0.42, 0.58, 0.66, 1];
    private static readonly double[] LiquidColor = [0.25, 0.58, 0.78, 1];
    private static readonly double[] TypeColor = [0.65, 0.42, 0.54, 1];

    private readonly List<ElementBounds> _rail = [];
    private int _scrollRow;
    private int _maxScrollRow;
    private double _rowStride = 50;
    private bool _suppressScroll;
    private string _search = "";
    private bool _searchLock;
    private string _signature = "";
    private SortMode _sort = SortMode.Count;
    private long _meterAt = -1000;
    private long _liquidViewAt = -1000;
    private string _ch = "";
    private string _pw = "";
    private string _it = "";
    private string _ty = "";
    private readonly float[] _meterShown = new float[4];
    private readonly float[] _meterMax = [1f, 1f, 1f, 1f];
    private double _btn = 48;
    private readonly bool _liquid;
    private readonly bool _pattern;
    private ElementBounds? _close;
    private double _winW;
    private double _winH;
    private bool _drag;
    private int _dragX;
    private int _dragY;
    private GuiDialog? _order;

    public GuiDialogEStorageTerminal(string dialogTitle, InventoryBase inventory, BlockPos pos, ICoreClientAPI capi, bool liquid = false, bool pattern = false)
        : base(dialogTitle, inventory, pos, capi)
    {
        _liquid = liquid;
        _pattern = pattern;
        if (IsDuplicate)
            return;

        capi.World.Player.InventoryManager.OpenInventory(inventory);
        if (pattern && capi.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityEStoragePatternTerminal bench)
            capi.World.Player.InventoryManager.OpenInventory(bench.Bench);
        SetupDialog();
    }

    public override void OnGuiClosed()
    {
        _order?.TryClose();
        _order = null;
        if (_pattern && BlockEntityPosition != null &&
            capi.World.BlockAccessor.GetBlockEntity(BlockEntityPosition) is BlockEntityEStoragePatternTerminal bench)
            capi.World.Player.InventoryManager.CloseInventory(bench.Bench);
        base.OnGuiClosed();
    }

    public override void OnRenderGUI(float deltaTime)
    {
        // Пакет слотов после заливки подменяет уже открытый список одной жидкостью.
        // Пока окно открыто, вид заново собирается из ячеек.
        RefreshLiquidView();
        if (Signature() != _signature)
            SetupDialog();

        base.OnRenderGUI(deltaTime);
        TouchMeters(false);
    }

    public override void OnMouseWheel(MouseWheelEventArgs args)
    {
        var mouse = capi.World.Player.InventoryManager.MouseItemSlot;
        var inside = SingleComposer != null && SingleComposer.Bounds.PointInside(capi.Input.MouseX, capi.Input.MouseY);
        if (!args.IsHandled && inside && _maxScrollRow > 0 && (mouse == null || mouse.Empty))
        {
            var row = Math.Clamp(_scrollRow - Math.Sign(args.delta), 0, _maxScrollRow);
            if (row != _scrollRow)
            {
                _scrollRow = row;
                SetupDialog();
            }

            args.SetHandled();
            return;
        }

        base.OnMouseWheel(args);
    }

    public override void OnMouseDown(MouseEvent args)
    {
        if (CloseHit())
        {
            args.Handled = true;
            CloseIconPressed();
            return;
        }

        if (TryOrder(args))
            return;

        if (TryDepositAnywhere(args))
            return;

        base.OnMouseDown(args);
        if (!args.Handled && OnFrame() && !SearchContains())
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

    private bool CloseHit()
    {
        return _close != null && _close.PointInside(capi.Input.MouseX, capi.Input.MouseY);
    }

    private bool SearchContains()
    {
        var search = SingleComposer?.GetTextInput("search");
        return search != null && search.Bounds.PointInside(capi.Input.MouseX, capi.Input.MouseY);
    }

    private bool TryOrder(MouseEvent args)
    {
        if (_liquid || _pattern || args.Button != EnumMouseButton.Left || BlockEntityPosition == null)
            return false;

        var mouse = capi.World.Player.InventoryManager.MouseItemSlot;
        if (mouse != null && !mouse.Empty)
            return false;

        if (capi.World.Player.InventoryManager.CurrentHoveredSlot is not ItemSlotNetwork { CraftOnly: true, Itemstack: { } stack } slot)
            return false;
        if (slot.Inventory != Inventory)
            return false;

        var proto = stack.Clone();
        proto.Attributes.RemoveAttribute(StorageAccess.CraftGhostKey);
        proto.Attributes.RemoveAttribute(StorageAccess.OrderKey);
        proto.StackSize = 1;
        _order?.TryClose();
        _order = new GuiDialogEStorageOrder(capi, proto.GetName(), count => SendOrder(proto, count));
        _order.TryOpen();
        args.Handled = true;
        return true;
    }

    private void SendOrder(ItemStack proto, int count)
    {
        if (BlockEntityPosition == null || count <= 0)
            return;

        var tree = new TreeAttribute();
        tree.SetInt("count", count);
        tree["out"] = new ItemstackAttribute(proto.Clone());
        capi.Network.SendBlockEntityPacket(BlockEntityPosition, StorageAccess.OrderPacketId, tree.ToBytes());
    }

    /// <summary>
    /// Стак в курсоре кладётся в сеть кликом по окну. Рейка, поиск и полоса прокрутки клик не забирают:
    /// их кнопки должны получить OnMouseDown.
    /// </summary>
    private bool TryDepositAnywhere(MouseEvent args)
    {
        if (SingleComposer == null || !IsOpened())
            return false;
        if (args.Button != EnumMouseButton.Left && args.Button != EnumMouseButton.Right)
            return false;

        var mouse = capi.World.Player.InventoryManager.MouseItemSlot;
        if (mouse?.Itemstack == null || mouse.Empty)
            return false;
        if (!SingleComposer.Bounds.PointInside(capi.Input.MouseX, capi.Input.MouseY) || OnFrame())
            return false;

        var bar = SingleComposer.GetScrollbar("storedScroll");
        if (bar != null && (bar.Bounds.PointInside(capi.Input.MouseX, capi.Input.MouseY) || capi.Input.MouseY < bar.Bounds.renderY))
            return false;
        var search = SingleComposer.GetTextInput("search");
        if (search != null && search.Bounds.PointInside(capi.Input.MouseX, capi.Input.MouseY))
            return false;
        foreach (var hit in _rail)
        {
            if (hit.PointInside(capi.Input.MouseX, capi.Input.MouseY))
                return false;
        }

        if (_liquid && StorageAccess.ContainerOf(mouse.Itemstack) != null)
        {
            SendLiquid(args);
            args.Handled = true;
            return true;
        }

        var slotId = SlotForIncoming(mouse.Itemstack);
        if (slotId < 0)
            return false;

        var qty = args.Button == EnumMouseButton.Right ? 1 : mouse.StackSize;
        var op = new ItemStackMoveOperation(capi.World, args.Button, 0, EnumMergePriority.AutoMerge, qty)
        {
            ActingPlayer = capi.World.Player
        };
        var packet = Inventory.ActivateSlot(slotId, mouse, ref op);
        if (packet != null)
            DoSendPacket(packet);
        args.Handled = true;
        return true;
    }

    private void SendLiquid(MouseEvent args)
    {
        if (BlockEntityPosition == null)
            return;

        var one = args.Button == EnumMouseButton.Right ? 1 : 0;
        var data = new byte[8];
        BitConverter.GetBytes(one).CopyTo(data, 0);
        BitConverter.GetBytes(HoveredSlot()).CopyTo(data, 4);
        capi.Network.SendBlockEntityPacket(BlockEntityPosition, StorageAccess.LiquidPacketId, data);
    }

    private int HoveredSlot()
    {
        var hovered = capi.World.Player.InventoryManager.CurrentHoveredSlot;
        if (hovered == null || hovered.Inventory != Inventory || hovered.Empty)
            return -1;

        for (var i = 0; i < Inventory.Count; i++)
        {
            if (Inventory[i] == hovered)
                return i;
        }

        return -1;
    }

    private void SetupDialog()
    {
        var resumeCaret = -1;
        var oldSearch = SingleComposer?.GetTextInput("search");
        if (oldSearch != null && SingleComposer!.CurrentTabIndexElement == oldSearch)
            resumeCaret = SearchCaret.GetValue(oldSearch) is int at ? at : _search.Length;

        var filled = FilledIndices();
        var rows = _pattern ? 8 : VisibleRows;
        var totalRows = filled.Count == 0 ? 0 : (filled.Count + Cols - 1) / Cols;
        _maxScrollRow = Math.Max(0, totalRows - rows);
        _scrollRow = Math.Clamp(_scrollRow, 0, _maxScrollRow);
        _rail.Clear();

        var gridMeasure = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, Cols, rows);
        var gridW = gridMeasure.fixedWidth;
        var gridH = gridMeasure.fixedHeight;
        _rowStride = gridH / VisibleRows;
        _btn = GuiElementPassiveItemSlot.unscaledSlotSize;
        var rail = _btn;
        var contentW = rail + 6 + gridW + ScrollGap + ScrollW;
        var edge = GuiElementTermFrame.Inset;
        var originX = edge;
        var originY = edge;
        var craftMeasure = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, 3, 3);
        var craftOut = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, 1, 1);
        var patternH = _pattern ? 8 + craftMeasure.fixedHeight + 8 + craftOut.fixedHeight : 0;
        var contentH = SearchH + 8 + MeterH + 6 + gridH + patternH;
        var dialogW = edge + contentW + edge;
        var dialogH = edge + contentH + edge;
        _winW = dialogW;
        _winH = dialogH;

        var titleX = originX;
        var titleW = _pattern ? 210.0 : 118.0;
        var searchX = titleX + titleW + 8;
        var searchY = originY;
        var closeX = originX + contentW - CloseS;
        var closeY = originY + (SearchH - CloseS) / 2.0;
        var searchW = Math.Max(80, closeX - 8 - searchX);
        var search = ElementBounds.Fixed(searchX, searchY, searchW, SearchH);
        var icon = SearchH - 6;
        var glass = ElementBounds.Fixed(searchX + searchW - icon - 3, searchY + 3, icon, icon);
        _close = ElementBounds.Fixed(closeX, closeY, CloseS, CloseS);
        var title = ElementBounds.Fixed(titleX, searchY, titleW, SearchH);

        var meterY = originY + SearchH + 8;
        var gridY = meterY + MeterH + 6;
        var grid = ElementBounds.Fixed(originX + rail + 6, gridY, gridW, gridH);
        var scroll = ElementBounds.Fixed(originX + rail + 6 + gridW + ScrollGap, gridY, ScrollW, gridH);

        var meterW = (contentW - 3 * MeterGap) / 4.0;
        var meters = new ElementBounds[4];
        for (var i = 0; i < 4; i++)
            meters[i] = ElementBounds.Fixed(originX + i * (meterW + MeterGap), meterY, meterW, MeterH);

        var frame = ElementBounds.Fixed(0, 0, dialogW, dialogH);
        var bg = ElementBounds.Fill.WithFixedPadding(0);
        bg.BothSizing = ElementSizing.FitToChildren;
        bg.WithChildren(frame, _close);

        var dialog = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

        var hovered = capi.World.Player.InventoryManager.CurrentHoveredSlot;
        if (hovered != null && hovered.Inventory == Inventory)
            capi.Input.TriggerOnMouseLeaveSlot(hovered);

        ClearComposers();
        var compoName = (_pattern ? "estoragepatternterminal" : _liquid ? "estorageliquidterminal" : "estorageterminal") + BlockEntityPosition;
        var compo = capi.Gui
            .CreateCompo(compoName, dialog)
            .BeginChildElements(bg)
            .AddStaticElement(new GuiElementTermFrame(capi, frame, closeX, closeY, CloseS))
            .AddStaticText(DialogTitle, CairoFont.WhiteDetailText().WithFontSize(18), title);

        compo.AddTextInput(search, OnSearch, CairoFont.WhiteDetailText(), "search");
        SearchRightSpace.SetValue(compo.GetTextInput("search"), (icon + 4) * RuntimeEnv.GUIScale);
        compo.AddInteractiveElement(new GuiElementTermIcon(capi, glass, TermIcon.Search, false, false, null, true), "searchIcon");

        AddMeter(compo, meters[0], TermIcon.Channels, ChannelColor, "ch", "estorage-terminal-channels");
        AddMeter(compo, meters[1], TermIcon.Power, PowerColor, "pw", "estorage-terminal-power");
        if (_liquid)
            AddMeter(compo, meters[2], TermIcon.Liquid, LiquidColor, "it", "estorage-terminal-litres");
        else
            AddMeter(compo, meters[2], TermIcon.Items, ItemColor, "it", "estorage-terminal-items");
        AddMeter(compo, meters[3], TermIcon.Types, TypeColor, "ty", "estorage-terminal-kinds");

        var railX = originX;
        AddRail(compo, TermIcon.Mod, _sort == SortMode.Mod, railX, gridY, "estorage-terminal-sort-mod", () => SelectSort(SortMode.Mod));
        AddRail(compo, TermIcon.Name, _sort == SortMode.Name, railX, gridY + _rowStride, "estorage-terminal-sort-name", () => SelectSort(SortMode.Name));
        AddRail(compo, TermIcon.Count, _sort == SortMode.Count, railX, gridY + 2 * _rowStride, "estorage-terminal-sort-count", () => SelectSort(SortMode.Count));
        if (!_pattern)
            AddRail(compo, TermIcon.Clear, false, railX, gridY + (rows - 1) * _rowStride, "estorage-terminal-clear", ClearQuery);

        if (filled.Count > 0)
        {
            var start = _scrollRow * Cols;
            var take = Math.Min(filled.Count - start, rows * Cols);
            var indices = new int[take];
            for (var i = 0; i < take; i++)
                indices[i] = filled[start + i];

            compo.AddItemSlotGrid(Inventory, DoSendPacket, Cols, indices, grid, "stored");
        }

        if (_pattern)
            AddPatternBench(compo, originX, gridY + gridH + 8);

        compo.AddVerticalScrollbar(OnScroll, scroll, "storedScroll");
        SingleComposer = compo.EndChildElements().Compose();

        var typed = SingleComposer.GetTextInput("search");
        _searchLock = true;
        typed.SetValue(_search, false);
        _searchLock = false;
        if (resumeCaret >= 0)
        {
            typed.SetCaretPos(Math.Min(resumeCaret, _search.Length), 0);
            SingleComposer.FocusElement(typed.TabIndex);
        }
        _signature = SignatureOf(filled);
        _ch = _pw = _it = _ty = "";

        _suppressScroll = true;
        var bar = SingleComposer.GetScrollbar("storedScroll");
        var totalHeight = Math.Max(grid.fixedHeight, totalRows * _rowStride);
        bar?.SetHeights((float)grid.fixedHeight, (float)totalHeight);
        bar?.SetScrollbarPosition((int)(_scrollRow * _rowStride));
        _suppressScroll = false;
        TouchMeters(true);
    }

    private BlockEntityEStoragePatternTerminal? PatternEntity =>
        !_pattern || BlockEntityPosition == null
            ? null
            : capi.World.BlockAccessor.GetBlockEntity(BlockEntityPosition) as BlockEntityEStoragePatternTerminal;

    private void AddPatternBench(GuiComposer compo, double x, double y)
    {
        var entity = PatternEntity;
        var inv = entity?.Bench;
        if (inv == null)
            return;

        var processing = entity!.Processing;
        var grid = ElementStdBounds.SlotGrid(EnumDialogArea.None, x, y, 3, 3);
        var output = processing
            ? ElementStdBounds.SlotGrid(EnumDialogArea.None, x + grid.fixedWidth + 8, y, 1, 3)
            : ElementStdBounds.SlotGrid(EnumDialogArea.None, x + grid.fixedWidth + 8, y, 1, 1);
        if (!processing)
            output.fixedY += (grid.fixedHeight - output.fixedHeight) / 2.0;

        var gridIndex = new int[9];
        for (var i = 0; i < 9; i++)
            gridIndex[i] = BlockEntityEStoragePatternTerminal.GridSlot + i;
        compo.AddItemSlotGrid(inv, SendBench, 3, gridIndex, grid, "craftgrid");
        _rail.Add(grid);

        var outIndex = processing
            ? new[] { BlockEntityEStoragePatternTerminal.ProcessSlot, BlockEntityEStoragePatternTerminal.ProcessSlot + 1, BlockEntityEStoragePatternTerminal.ProcessSlot + 2 }
            : new[] { BlockEntityEStoragePatternTerminal.CraftOutSlot };
        compo.AddItemSlotGrid(inv, SendBench, 1, outIndex, output, "craftout");
        _rail.Add(output);
        compo.AddHoverText(
            Lang.Get(processing
                ? "electricalprogressivestorage:estorage-pattern-process-tip"
                : "electricalprogressivestorage:estorage-pattern-result"),
            CairoFont.WhiteSmallText(), 240, output.FlatCopy(), "craftouttip");

        var icon = 22.0;
        var modeX = output.fixedX + output.fixedWidth + 8;
        var stack = icon * 3 + 8;
        var modeY = y + (grid.fixedHeight - stack) / 2.0;
        AddMode(compo, modeX, modeY, icon, icon, TermIcon.Craft, !processing, 3, 0, "estorage-pattern-craft");
        AddMode(compo, modeX, modeY + icon + 4, icon, icon, TermIcon.Process, processing, 3, 1, "estorage-pattern-process");
        AddMode(compo, modeX, modeY + (icon + 4) * 2, icon, icon, TermIcon.Substitute, entity.Substitute, 4, 0, "estorage-pattern-substitute");

        var rowY = y + grid.fixedHeight + 8;
        var blank = ElementStdBounds.SlotGrid(EnumDialogArea.None, x, rowY, 1, 1);
        var arrowX = x + blank.fixedWidth + 6;
        var encoded = ElementStdBounds.SlotGrid(EnumDialogArea.None, arrowX + blank.fixedWidth + 6, rowY, 1, 1);
        compo.AddItemSlotGrid(inv, SendBench, 1, [BlockEntityEStoragePatternTerminal.BlankSlot], blank, "blank");
        compo.AddItemSlotGrid(inv, SendBench, 1, [BlockEntityEStoragePatternTerminal.EncodedSlot], encoded, "encoded");
        _rail.Add(blank);
        _rail.Add(encoded);
        compo.AddHoverText(Lang.Get("electricalprogressivestorage:estorage-pattern-blank"), CairoFont.WhiteSmallText(), 240, blank.FlatCopy(), "blanktip");
        compo.AddHoverText(Lang.Get("electricalprogressivestorage:estorage-pattern-out"), CairoFont.WhiteSmallText(), 240, encoded.FlatCopy(), "encodedtip");
        AddMode(compo, arrowX, rowY, blank.fixedWidth, blank.fixedHeight, TermIcon.Arrow, false, 5, 0, "estorage-pattern-write");
    }

    private void SendBench(object packet)
    {
        if (BlockEntityPosition == null)
            return;
        var inner = packet.GetType().GetField("ActivateInventorySlot")?.GetValue(packet);
        if (inner == null)
            return;

        var data = new byte[21];
        data[0] = 10;
        BitConverter.GetBytes(FieldInt(inner, "TargetSlot")).CopyTo(data, 1);
        BitConverter.GetBytes(FieldInt(inner, "MouseButton")).CopyTo(data, 5);
        BitConverter.GetBytes(FieldInt(inner, "Modifiers")).CopyTo(data, 9);
        BitConverter.GetBytes(FieldInt(inner, "Priority")).CopyTo(data, 13);
        BitConverter.GetBytes(FieldInt(inner, "Dir")).CopyTo(data, 17);
        capi.Network.SendBlockEntityPacket(BlockEntityPosition, BlockEntityEStoragePatternTerminal.PacketId, data);
    }

    private static int FieldInt(object obj, string name)
    {
        var field = obj.GetType().GetField(name);
        return field?.GetValue(obj) is int value ? value : 0;
    }

    private void AddMode(GuiComposer compo, double x, double y, double w, double h, TermIcon icon, bool pressed, byte op, byte arg, string langKey)
    {
        var bounds = ElementBounds.Fixed(x, y, w, h);
        _rail.Add(bounds);
        compo.AddInteractiveElement(new GuiElementTermIcon(capi, bounds, icon, true, pressed, () => SendPattern(op, arg, 0)), langKey);
        compo.AddHoverText(Lang.Get("electricalprogressivestorage:" + langKey + "-tip"), CairoFont.WhiteSmallText(), 240, bounds.FlatCopy(), langKey + "-tip");
    }

    private void SendPattern(byte op, byte arg, byte button)
    {
        if (BlockEntityPosition == null)
            return;
        var shift = capi.World.Player.Entity.Controls.ShiftKey ? (byte)1 : (byte)0;
        capi.Network.SendBlockEntityPacket(BlockEntityPosition, BlockEntityEStoragePatternTerminal.PacketId, [op, arg, button, shift]);
    }

    private void AppendPattern(StringBuilder text)
    {
        var entity = PatternEntity;
        if (entity == null)
            return;
        text.Append(entity.Processing ? 'p' : 'c');
        text.Append(entity.Substitute ? 's' : 'n');
    }

    private void AddMeter(GuiComposer compo, ElementBounds cell, TermIcon icon, double[] color, string key, string langKey)
    {
        compo.AddStaticElement(new GuiElementMeterPlate(capi, cell));
        var iconBounds = ElementBounds.Fixed(cell.fixedX + MeterPad, cell.fixedY + (MeterH - IconW) / 2.0, IconW, IconW);
        var colX = cell.fixedX + MeterPad + IconW + 6;
        var colW = Math.Max(8, cell.fixedWidth - MeterPad * 2 - IconW - 6);
        var text = ElementBounds.Fixed(colX, cell.fixedY + MeterPad, colW, 20);
        var bar = ElementBounds.Fixed(colX, cell.fixedY + MeterH - MeterPad - BarH, colW, BarH);
        var index = key switch { "ch" => 0, "pw" => 1, "it" => 2, _ => 3 };
        compo.AddStaticElement(new GuiElementTermIcon(capi, iconBounds, icon, false, false, null), key + "Icon");
        compo.AddDynamicCustomDraw(bar, (ctx, _, bounds) => DrawMeter(ctx, bounds, index, color), key + "Draw");
        compo.AddDynamicText("", CairoFont.WhiteDetailText().WithFontSize(16).WithOrientation(EnumTextOrientation.Right), text, key + "Text");
        compo.AddHoverText(Lang.Get("electricalprogressivestorage:" + langKey), CairoFont.WhiteSmallText(), 220, cell.FlatCopy(), key + "Tip");
    }

    private void DrawMeter(Context ctx, ElementBounds bounds, int index, double[] color)
    {
        var w = bounds.InnerWidth;
        var h = bounds.InnerHeight;
        ctx.SetSourceRGBA(0.08, 0.06, 0.045, 1);
        ctx.Rectangle(0, 0, w, h);
        ctx.Fill();
        var limit = Math.Max(_meterMax[index], 1f);
        var frac = Math.Clamp(_meterShown[index] / limit, 0f, 1f);
        if (frac > 0 && w >= 1)
        {
            ctx.SetSourceRGBA(color[0], color[1], color[2], 1);
            ctx.Rectangle(0, 0, Math.Max(1, w * frac), h);
            ctx.Fill();
        }

        ctx.SetSourceRGBA(0.55, 0.42, 0.26, 1);
        ctx.LineWidth = 1;
        ctx.Rectangle(0.5, 0.5, Math.Max(1, w - 1), Math.Max(1, h - 1));
        ctx.Stroke();
    }

    private void AddRail(GuiComposer compo, TermIcon icon, bool pressed, double x, double y, string langKey, Action click)
    {
        var bounds = ElementBounds.Fixed(x, y, _btn, _btn);
        _rail.Add(bounds);
        compo.AddInteractiveElement(new GuiElementTermIcon(capi, bounds, icon, true, pressed, click), langKey);
        compo.AddHoverText(Lang.Get("electricalprogressivestorage:" + langKey), CairoFont.WhiteSmallText(), 240, bounds.FlatCopy(), langKey + "-tip");
    }

    private void SelectSort(SortMode mode)
    {
        if (_sort == mode)
            return;
        _sort = mode;
        _scrollRow = 0;
        QueueSetup();
    }

    private void ClearQuery()
    {
        if (_search.Length == 0 && _scrollRow == 0)
            return;
        _search = "";
        _scrollRow = 0;
        QueueSetup();
    }

    private void QueueSetup()
    {
        capi.Event.EnqueueMainThreadTask(() =>
        {
            if (IsOpened())
                SetupDialog();
        }, "estorage-terminal");
    }

    private void OnScroll(float value)
    {
        if (_suppressScroll || _rowStride <= 0)
            return;

        var row = Math.Clamp((int)Math.Round(value / _rowStride), 0, _maxScrollRow);
        if (row == _scrollRow)
            return;

        _scrollRow = row;
        SetupDialog();
    }

    private string Signature() => SignatureOf(FilledIndices());

    private string SignatureOf(List<int> filled)
    {
        var text = new StringBuilder(filled.Count * 6 + 16);
        text.Append((int)_sort).Append('|').Append(_search).Append('|');
        AppendPattern(text);
        foreach (var index in filled)
        {
            var stack = Inventory[index].Itemstack;
            text.Append(index);
            text.Append(':');
            text.Append(stack?.Collectible?.Id ?? 0);
            text.Append('=');
            text.Append(stack?.StackSize ?? 0);
            text.Append(Inventory[index] is ItemSlotNetwork { CraftOnly: true } ? 'c' : 's');
            text.Append(',');
        }

        return text.ToString();
    }

    private List<int> FilledIndices()
    {
        var filled = new List<int>();
        var query = _search.Trim();
        for (var i = 0; i < Inventory.Count; i++)
        {
            var stack = Inventory[i].Itemstack;
            if (stack == null || Inventory[i].Empty)
                continue;
            if (query.Length > 0)
            {
                var name = stack.GetName() ?? "";
                if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
            }

            filled.Add(i);
        }

        filled.Sort(CompareSlots);
        return filled;
    }

    private int CompareSlots(int a, int b)
    {
        var left = Inventory[a].Itemstack;
        var right = Inventory[b].Itemstack;
        if (left == null || right == null)
            return (left == null ? 1 : 0) - (right == null ? 1 : 0);

        var cmp = _sort switch
        {
            SortMode.Mod => string.Compare(Domain(left), Domain(right), StringComparison.OrdinalIgnoreCase),
            SortMode.Name => string.Compare(NameOf(left), NameOf(right), StringComparison.OrdinalIgnoreCase),
            _ => _liquid
                ? StorageAccess.StackLitres(right).CompareTo(StorageAccess.StackLitres(left))
                : right.StackSize.CompareTo(left.StackSize)
        };
        if (cmp != 0)
            return cmp;
        cmp = string.Compare(NameOf(left), NameOf(right), StringComparison.OrdinalIgnoreCase);
        if (cmp != 0)
            return cmp;
        return string.Compare(Domain(left), Domain(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string Domain(ItemStack stack) => stack.Collectible?.Code?.Domain ?? "";

    private static string NameOf(ItemStack stack) => stack.GetName() ?? "";

    private int SlotForIncoming(ItemStack stack)
    {
        for (var i = 0; i < Inventory.Count; i++)
        {
            if (Inventory[i] is ItemSlotNetwork net && net.CraftOnly)
                continue;
            var have = Inventory[i].Itemstack;
            if (have != null && StorageAccess.Same(capi.World, have, stack))
                return i;
        }

        return FirstEmpty();
    }

    private int FirstEmpty()
    {
        for (var i = 0; i < Inventory.Count; i++)
        {
            if (Inventory[i].Empty)
                return i;
        }

        return -1;
    }

    private void RefreshLiquidView()
    {
        if (!_liquid || Inventory is not InventoryEStorageTerminal view)
            return;

        var now = capi.ElapsedMilliseconds;
        if (now - _liquidViewAt < 200)
            return;

        _liquidViewAt = now;
        view.Rebuild();
    }

    private void OnSearch(string text)
    {
        if (_searchLock || text == _search)
            return;
        _search = text;
        _scrollRow = 0;
        SetupDialog();
    }

    private void TouchMeters(bool force)
    {
        var now = capi.ElapsedMilliseconds;
        if (!force && now - _meterAt < 200)
            return;
        _meterAt = now;
        RefreshMeters();
    }

    private void RefreshMeters()
    {
        if (SingleComposer == null || capi.World == null || BlockEntityPosition == null)
            return;

        var scan = StorageAccess.GetScan(capi.World, BlockEntityPosition);
        var used = scan.Conflict ? 0 : scan.ChannelUsed;
        var supply = scan.Conflict ? 0 : scan.ChannelSupply;

        var cap = 2000f;
        var controller = scan.Controller;
        if (controller != null)
            cap = MyMiniLib.GetAttributeFloat(capi.World.BlockAccessor.GetBlock(controller), "maxConsumption", 2000f);
        if (cap < 1f)
            cap = 1f;

        var demand = 0f;
        if (!scan.Conflict && StorageAccess.HasPower(capi.World, scan))
            demand = Math.Min(StorageAccess.Demand(capi.World, scan), cap);

        long items = 0;
        var types = 0;
        for (var i = 0; i < Inventory.Count; i++)
        {
            var stack = Inventory[i].Itemstack;
            if (stack == null || Inventory[i].Empty)
                continue;
            items += _liquid
                ? (long)Math.Floor(StorageAccess.StackLitres(stack) + 1e-3)
                : stack.StackSize;
            types++;
        }

        long itemCap;
        int typeCap;
        if (_liquid)
            StorageAccess.CellBudgets(capi.World, BlockEntityPosition, out itemCap, out typeCap);
        else
            StorageAccess.DiskBudgets(capi.World, BlockEntityPosition, out itemCap, out typeCap);

        SetMeter(0, "ch", supply <= 0 ? 0 : used, Math.Max(supply, 1), Ratio(used, supply), ref _ch);
        SetMeter(1, "pw", demand, cap, Abbrev((long)Math.Round(demand)), ref _pw);
        SetMeter(2, "it", itemCap <= 0 ? 0f : (float)Math.Min(items, itemCap), (float)Math.Max(itemCap, 1L), Ratio(items, itemCap), ref _it);
        SetMeter(3, "ty", typeCap <= 0 ? 0 : Math.Min(types, typeCap), Math.Max(typeCap, 1), Ratio(types, typeCap), ref _ty);
    }

    private void SetMeter(int index, string key, float shown, float max, string text, ref string cache)
    {
        var limit = Math.Max(max, 1f);
        _meterShown[index] = Math.Min(shown, limit);
        _meterMax[index] = limit;
        SingleComposer?.GetCustomDraw(key + "Draw")?.Redraw();
        if (text == cache)
            return;
        cache = text;
        SingleComposer?.GetDynamicText(key + "Text")?.SetNewText(text);
    }

    private static string Ratio(long current, long cap) => Abbrev(current) + "/" + Abbrev(cap);

    private static string Abbrev(long value)
    {
        if (value < 0)
            value = 0;
        if (value < 1000)
            return value.ToString(CultureInfo.InvariantCulture);
        if (value > int.MaxValue)
            value = int.MaxValue;
        return StackSizeTextPatch.Format((int)value);
    }
}
