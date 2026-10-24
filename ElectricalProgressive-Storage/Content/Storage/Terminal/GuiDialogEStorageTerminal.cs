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

namespace ElectricalProgressive.Content.Storage.Terminal;

/// <summary>
/// Окно сети: предметы с дисков или жидкости с ячеек.
/// </summary>
public class GuiDialogEStorageTerminal : GuiDialogBlockEntity
{
    private enum SortMode
    {
        Mod,
        Name,
        Count
    }

    private const int Cols = 9;
    private const int VisibleRows = 11;
    private const double MeterH = 56;
    private const double MeterPad = 6;
    private const double BarH = 16;
    private const double IconW = 44;

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
    private bool _setupQueued;
    private string _signature = "";
    private SortMode _sort = SortMode.Count;
    private bool _craftView;
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
    private readonly bool _craft;
    private ElementBounds? _close;
    private double _winW;
    private double _winH;
    private double _head;
    private bool _drag;
    private int _dragX;
    private int _dragY;
    private EStorageOrderPane? _pane;
    private GuiDialogEStorageOrder? _order;
    private bool _holdEscape;

    public GuiDialogEStorageTerminal(string dialogTitle, InventoryBase inventory, BlockPos pos, ICoreClientAPI capi, bool liquid = false, bool pattern = false, bool craft = false)
        : base(dialogTitle, inventory, pos, capi)
    {
        _liquid = liquid;
        _pattern = pattern;
        _craft = craft;
        if (IsDuplicate)
            return;

        capi.World.Player.InventoryManager.OpenInventory(inventory);
        var entity = PanelStack.View(capi.World, pos, inventory);
        if (pattern && entity is BlockEntityEStoragePatternTerminal bench)
            capi.World.Player.InventoryManager.OpenInventory(bench.Bench);
        if (craft && entity is BlockEntityEStorageCraftTerminal craftBench)
            capi.World.Player.InventoryManager.OpenInventory(craftBench.Bench);
        SetupDialog();
    }

    public override bool OnEscapePressed()
    {
        if (_pane != null || _order != null)
        {
            CloseOrder();
            _holdEscape = false;
            return true;
        }

        if (_holdEscape)
        {
            _holdEscape = false;
            return true;
        }

        return base.OnEscapePressed();
    }

    public override void OnGuiClosed()
    {
        CloseOrder();
        var entity = PanelStack.View(capi.World, BlockEntityPosition, Inventory);
        if (_pattern && entity is BlockEntityEStoragePatternTerminal bench)
            capi.World.Player.InventoryManager.CloseInventory(bench.Bench);
        if (_craft && entity is BlockEntityEStorageCraftTerminal craftBench)
            capi.World.Player.InventoryManager.CloseInventory(craftBench.Bench);
        base.OnGuiClosed();
    }

    public override void OnRenderGUI(float deltaTime)
    {
        // Пакет слотов после заливки подменяет уже открытый список одной жидкостью.
        // Пока окно открыто, вид заново собирается из ячеек.
        _holdEscape = false;
        if (_pane == null)
        {
            RefreshLiquidView();
            if (Signature() != _signature)
                SetupDialog();
        }

        base.OnRenderGUI(deltaTime);
        if (_pane == null)
            TouchMeters(false);
    }

    public override void OnMouseWheel(MouseWheelEventArgs args)
    {
        if (args.IsHandled || _pane != null)
            return;

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
        if (args.Handled || _pane != null)
            return;

        if (CloseHit())
        {
            args.Handled = true;
            CloseIconPressed();
            return;
        }

        var hitSearch = SearchContains();
        if (!hitSearch)
            SingleComposer?.UnfocusOwnElements();

        if (TryOrder(args))
            return;

        if (TryDepositAnywhere(args))
            return;

        base.OnMouseDown(args);
        if (hitSearch)
            FocusSearch();
        if (!args.Handled && OnFrame() && !hitSearch)
            BeginDrag();
    }

    public override void OnMouseMove(MouseEvent args)
    {
        if (args.Handled || _pane != null)
            return;

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
        if (_pane != null)
        {
            _drag = false;
            return;
        }

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
        return y >= 0 && y < _head && x >= 0 && x <= _winW;
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

    private void FocusSearch()
    {
        var typed = SingleComposer?.GetTextInput("search");
        if (typed == null || SingleComposer == null || SingleComposer.CurrentTabIndexElement == typed)
            return;

        var caret = SearchCaret.GetValue(typed) is int at ? at : _search.Length;
        SingleComposer.FocusElement(typed.TabIndex);
        typed.SetCaretPos(Math.Min(caret, _search.Length), 0);
    }

    private bool TryOrder(MouseEvent args)
    {
        if (BlockEntityPosition == null)
            return false;

        var wheel = args.Button == EnumMouseButton.Middle;
        var ghost = args.Button == EnumMouseButton.Left;
        if (!wheel && !ghost)
            return false;

        if (ghost)
        {
            var mouse = capi.World.Player.InventoryManager.MouseItemSlot;
            if (mouse != null && !mouse.Empty)
                return false;
        }

        if (capi.World.Player.InventoryManager.CurrentHoveredSlot is not ItemSlotNetwork { Itemstack: { } stack } slot)
            return false;
        if (slot.Inventory != Inventory || !slot.CanOrder)
            return false;
        if (ghost && !slot.CraftOnly)
            return false;

        var proto = stack.Clone();
        proto.Attributes.RemoveAttribute(StorageAccess.CraftGhostKey);
        proto.Attributes.RemoveAttribute(StorageAccess.OrderKey);
        proto.StackSize = 1;
        CloseOrder();
        var leaving = capi.World.Player.InventoryManager.CurrentHoveredSlot;
        if (leaving != null && leaving.Inventory == Inventory)
            capi.Input.TriggerOnMouseLeaveSlot(leaving);
        SingleComposer?.UnfocusOwnElements();
        _pane = new EStorageOrderPane(capi, proto.GetName(), proto, (count, start) => SendOrder(proto, count, start), CloseOrder, RefreshOrder);
        _order = new GuiDialogEStorageOrder(capi, _pane, () => SingleComposer?.Bounds, CloseOrder);
        _order.Open();
        args.Handled = true;
        return true;
    }

    private void SendOrder(ItemStack proto, int count, bool start)
    {
        if (BlockEntityPosition == null || count <= 0)
            return;

        var tree = new TreeAttribute();
        tree.SetInt("count", count);
        tree.SetBool("start", start);
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

    private void CloseOrder()
    {
        if (_pane == null && _order == null)
            return;

        var order = _order;
        _order = null;
        _holdEscape = true;
        DetachOrder();
        if (order != null && order.IsOpened())
            order.TryClose();
        if (IsOpened())
            capi.Gui.RequestFocus(this);
    }

    private void RefreshOrder()
    {
        if (_order != null && _order.IsOpened())
            _order.Recompose();
    }

    private void DetachOrder()
    {
        if (EStorageOrderSync.Current == _pane)
            EStorageOrderSync.Current = null;
        _pane = null;
    }

    private void SetupDialog()
    {
        var resumeCaret = -1;
        var oldSearch = SingleComposer?.GetTextInput("search");
        if (oldSearch != null && SingleComposer!.CurrentTabIndexElement == oldSearch)
            resumeCaret = SearchCaret.GetValue(oldSearch) is int at ? at : _search.Length;

        var filled = FilledIndices();
        var rows = _pattern || _craft ? 8 : VisibleRows;
        var totalRows = filled.Count == 0 ? 0 : (filled.Count + Cols - 1) / Cols;
        _maxScrollRow = Math.Max(0, totalRows - rows);
        _scrollRow = Math.Clamp(_scrollRow, 0, _maxScrollRow);
        _rail.Clear();

        var pitch = Ae2Gui.Slots(0, 0, 1, 1).fixedHeight;
        var bench = _pattern || _craft ? 8 + 32 + pitch * 3 : 0;
        _head = Ae2Gui.Header;
        _btn = 40;
        var top = Ae2Gui.Header + Ae2Gui.Gap;
        var grid = Ae2Gui.Slots(Ae2Gui.Margin, top, Cols, rows);
        _rowStride = grid.fixedHeight / rows;
        var scroll = Ae2Gui.Box(grid.fixedX + grid.fixedWidth + 6, top, Ae2Gui.ScrollW, grid.fixedHeight);
        var sortX = scroll.fixedX + scroll.fixedWidth + 8;
        _winW = sortX + _btn + Ae2Gui.Margin;
        _winH = top + grid.fixedHeight + bench + Ae2Gui.Margin;
        _close = Ae2Gui.Box(_winW - Ae2Gui.Margin - 20, 11, 20, 20);
        var title = Ae2Gui.Box(Ae2Gui.Margin + 2, 8, _craft ? 210 : 150, 26);
        var search = Ae2Gui.Box(title.fixedX + title.fixedWidth + 8, 10, Math.Max(80, _close.fixedX - 8 - (title.fixedX + title.fixedWidth + 8)), 22);
        var frame = ElementBounds.Fixed(0, 0, _winW, _winH);
        var bg = ElementBounds.Fill.WithFixedPadding(0);
        bg.BothSizing = ElementSizing.FitToChildren;
        bg.WithChildren(frame, _close, search);

        var sheet = Ae2Gui.Window(capi, frame, _close.fixedX, _close.fixedY, _close.fixedWidth);
        if (_pattern || _craft)
            sheet.Fill(Ae2Gui.Margin, top + grid.fixedHeight + 4, grid.fixedWidth, 1, Ae2Gui.Muted[0], Ae2Gui.Muted[1], Ae2Gui.Muted[2]);

        var field = new GuiElementAe2Sheet(capi, frame);
        field.Fill(search.fixedX - 2, search.fixedY - 2, search.fixedWidth + 4, search.fixedHeight + 4, Ae2Gui.FieldInk[0], Ae2Gui.FieldInk[1], Ae2Gui.FieldInk[2]);

        var hovered = capi.World.Player.InventoryManager.CurrentHoveredSlot;
        if (hovered != null && hovered.Inventory == Inventory)
            capi.Input.TriggerOnMouseLeaveSlot(hovered);

        ClearComposers();
        var compoName = (_craft ? "estoragecraftterminal" : _pattern ? "estoragepatternterminal" : _liquid ? "estorageliquidterminal" : "estorageterminal") + BlockEntityPosition;
        var compo = capi.Gui
            .CreateCompo(compoName, Ae2Gui.Dialog())
            .BeginChildElements(bg)
            .AddScroll(OnScroll, scroll, "storedScroll")
            .AddStaticElement(sheet)
            .AddStaticText(DialogTitle, Ae2Gui.Title(), title)
            .AddTextInput(search, OnSearch, Ae2Gui.FieldFont(), "search");
        SearchRightSpace.SetValue(compo.GetTextInput("search"), 4 * RuntimeEnv.GUIScale);
        compo.AddStaticElement(field);

        var sortY = top;
        var sortStep = _btn + 6;
        AddRail(compo, TermIcon.Mod, _sort == SortMode.Mod, sortX, sortY, "estorage-terminal-sort-mod", () => SelectSort(SortMode.Mod));
        AddRail(compo, TermIcon.Name, _sort == SortMode.Name, sortX, sortY + sortStep, "estorage-terminal-sort-name", () => SelectSort(SortMode.Name));
        AddRail(compo, TermIcon.Count, _sort == SortMode.Count, sortX, sortY + 2 * sortStep, "estorage-terminal-sort-count", () => SelectSort(SortMode.Count));
        AddRail(compo, _craftView ? TermIcon.Craftable : TermIcon.Stored, _craftView, sortX, sortY + 3 * sortStep, _craftView ? "estorage-terminal-craftable" : "estorage-terminal-stored", ToggleCraftView);

        if (filled.Count > 0)
        {
            var start = _scrollRow * Cols;
            var take = Math.Min(filled.Count - start, rows * Cols);
            var indices = new int[take];
            for (var i = 0; i < take; i++)
                indices[i] = filled[start + i];

            compo.AddSlots(capi, Inventory, DoSendPacket, Cols, indices, grid, "stored");
        }

        if (_pattern)
            AddPatternBench(compo, top + grid.fixedHeight + 8, pitch);
        if (_craft)
            AddCraftBench(compo, top + grid.fixedHeight + 8, pitch);

        var composed = compo.EndChildElements().Compose(false);
        SingleComposer = composed;

        var typed = SingleComposer.GetTextInput("search");
        _searchLock = true;
        typed.SetValue(_search, false);
        _searchLock = false;
        if (resumeCaret >= 0)
        {
            SingleComposer.FocusElement(typed.TabIndex);
            typed.SetCaretPos(Math.Min(resumeCaret, _search.Length), 0);
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
        !_pattern
            ? null
            : PanelStack.View(capi.World, BlockEntityPosition, Inventory) as BlockEntityEStoragePatternTerminal;

    private void AddPatternBench(GuiComposer compo, double strip, double pitch)
    {
        var entity = PatternEntity;
        var inv = entity?.Bench;
        if (inv == null)
            return;

        var processing = entity!.Processing;
        const double band = 32;
        var slotsY = strip + band;
        var grid = Ae2Gui.Slots(Ae2Gui.Margin, slotsY, 3, 3);
        var flow = Ae2Gui.Box(grid.fixedX + grid.fixedWidth + 8, slotsY + pitch + (pitch - 40) / 2, 40, 40);
        _rail.Add(flow);
        compo.AddStaticElement(new GuiElementTermIcon(capi, flow, TermIcon.Arrow, false, false, null, false, true));
        var outputX = flow.fixedX + flow.fixedWidth + 8;
        var output = processing
            ? Ae2Gui.Slots(outputX, slotsY, 1, 3)
            : Ae2Gui.Slots(outputX, slotsY + pitch, 1, 1);

        var gridIndex = new int[9];
        for (var i = 0; i < 9; i++)
            gridIndex[i] = BlockEntityEStoragePatternTerminal.GridSlot + i;
        compo.AddSlots(capi, inv, SendBench, 3, gridIndex, grid, "craftgrid");
        _rail.Add(grid);

        var outIndex = processing
            ? new[] { BlockEntityEStoragePatternTerminal.ProcessSlot, BlockEntityEStoragePatternTerminal.ProcessSlot + 1, BlockEntityEStoragePatternTerminal.ProcessSlot + 2 }
            : new[] { BlockEntityEStoragePatternTerminal.CraftOutSlot };
        compo.AddSlots(capi, inv, SendBench, 1, outIndex, output, "craftout");
        _rail.Add(output);
        compo.AddHoverText(
            Lang.Get(processing
                ? "electricalprogressivestorage:estorage-pattern-process-tip"
                : "electricalprogressivestorage:estorage-pattern-result"),
            CairoFont.WhiteSmallText(), 240, output.FlatCopy(), "craftouttip");

        var side = outputX + pitch + 8;
        const double mode = 28;
        var modeY = strip + (band - mode) / 2;
        var modeRight = side + pitch;
        AddMode(compo, modeRight - mode * 3 - 8, modeY, mode, TermIcon.Craft, !processing, 3, 0, "estorage-pattern-craft");
        AddMode(compo, modeRight - mode * 2 - 4, modeY, mode, TermIcon.Process, processing, 3, 1, "estorage-pattern-process");
        AddMode(compo, modeRight - mode, modeY, mode, TermIcon.Substitute, entity.Substitute, 4, 0, "estorage-pattern-substitute");
        compo.AddStaticText(
            Lang.Get("electricalprogressivestorage:estorage-pattern-encode"),
            Ae2Gui.Label(),
            Ae2Gui.Box(Ae2Gui.Margin, strip, Math.Max(40, modeRight - mode * 3 - 16 - Ae2Gui.Margin), band));

        var blank = Ae2Gui.Slots(side, slotsY, 1, 1);
        var encoded = Ae2Gui.Slots(side, slotsY + pitch * 2, 1, 1);
        compo.AddSlots(capi, inv, SendBench, 1, [BlockEntityEStoragePatternTerminal.BlankSlot], blank, "blank");
        compo.AddSlots(capi, inv, SendBench, 1, [BlockEntityEStoragePatternTerminal.EncodedSlot], encoded, "encoded");
        _rail.Add(blank);
        _rail.Add(encoded);
        compo.AddHoverText(Lang.Get("electricalprogressivestorage:estorage-pattern-blank"), CairoFont.WhiteSmallText(), 240, blank.FlatCopy(), "blanktip");
        compo.AddHoverText(Lang.Get("electricalprogressivestorage:estorage-pattern-out"), CairoFont.WhiteSmallText(), 240, encoded.FlatCopy(), "encodedtip");
        AddMode(compo, side + (pitch - 40) / 2, slotsY + pitch + (pitch - 40) / 2, 40, TermIcon.Arrow, false, 5, 0, "estorage-pattern-write");
    }

    private void AddCraftBench(GuiComposer compo, double strip, double pitch)
    {
        var entity = PanelStack.View(capi.World, BlockEntityPosition, Inventory) as BlockEntityEStorageCraftTerminal;
        var inv = entity?.Bench;
        if (inv == null)
            return;

        const double band = 32;
        var slotsY = strip + band;
        var grid = Ae2Gui.Slots(Ae2Gui.Margin, slotsY, 3, 3);
        var flow = Ae2Gui.Box(grid.fixedX + grid.fixedWidth + 8, slotsY + pitch + (pitch - 40) / 2, 40, 40);
        _rail.Add(flow);
        compo.AddStaticElement(new GuiElementTermIcon(capi, flow, TermIcon.Arrow, false, false, null, false, true));
        var output = Ae2Gui.Slots(flow.fixedX + flow.fixedWidth + 8, slotsY + pitch, 1, 1);
        var gridIndex = new int[9];
        for (var i = 0; i < 9; i++)
            gridIndex[i] = i;
        compo.AddSlots(capi, inv, SendCraft, 3, gridIndex, grid, "craftgrid");
        _rail.Add(grid);
        compo.AddSlots(capi, inv, SendCraft, 1, [BlockEntityEStorageCraftTerminal.OutSlot], output, "craftout");
        _rail.Add(output);
        compo.AddStaticText(
            Lang.Get("electricalprogressivestorage:estorage-craft-bench"),
            Ae2Gui.Label(),
            Ae2Gui.Box(Ae2Gui.Margin, strip, Math.Max(40, output.fixedX + output.fixedWidth - Ae2Gui.Margin), band));
        compo.AddHoverText(Lang.Get("electricalprogressivestorage:estorage-craft-grid"), CairoFont.WhiteSmallText(), 260, grid.FlatCopy(), "craftgridtip");
        compo.AddHoverText(Lang.Get("electricalprogressivestorage:estorage-craft-out"), CairoFont.WhiteSmallText(), 260, output.FlatCopy(), "craftouttip");
    }

    private void SendCraft(object packet) => SendSlot(packet, BlockEntityEStorageCraftTerminal.PacketId);

    private void SendBench(object packet) => SendSlot(packet, BlockEntityEStoragePatternTerminal.PacketId);

    private void SendSlot(object packet, int packetId)
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
        capi.Network.SendBlockEntityPacket(BlockEntityPosition, packetId, data);
    }

    private static int FieldInt(object obj, string name)
    {
        var field = obj.GetType().GetField(name);
        return field?.GetValue(obj) is int value ? value : 0;
    }

    private void AddMode(GuiComposer compo, double x, double y, double size, TermIcon icon, bool pressed, byte op, byte arg, string langKey)
    {
        var bounds = Ae2Gui.Box(x, y, size, size);
        _rail.Add(bounds);
        compo.AddInteractiveElement(new GuiElementTermIcon(capi, bounds, icon, false, pressed, () => SendPattern(op, arg, 0), false, false, true), langKey);
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
        compo.AddInteractiveElement(new GuiElementTermIcon(capi, iconBounds, icon, false, false, null), key + "Icon");
        compo.AddDynamicCustomDraw(bar, (ctx, _, bounds) => DrawMeter(ctx, bounds, index, color), key + "Draw");
        compo.AddDynamicText("", CairoFont.WhiteDetailText().WithFontSize(16).WithOrientation(EnumTextOrientation.Right), text, key + "Text");
        compo.AddHoverText(Lang.Get("electricalprogressivestorage:" + langKey), CairoFont.WhiteSmallText(), 220, cell.FlatCopy(), key + "Tip");
    }

    private void DrawMeter(Context ctx, ElementBounds bounds, int index, double[] color)
    {
        var w = bounds.InnerWidth;
        var h = bounds.InnerHeight;
        TermChrome.Inset(ctx, 0, 0, w, h, 0.08, 0.06, 0.045);
        var limit = Math.Max(_meterMax[index], 1f);
        var frac = Math.Clamp(_meterShown[index] / limit, 0f, 1f);
        if (frac > 0 && w > 2 && h > 2)
            TermChrome.Gloss(ctx, 1, 1, Math.Max(1, (w - 2) * frac), h - 2, color[0], color[1], color[2]);
    }

    private void AddRail(GuiComposer compo, TermIcon icon, bool pressed, double x, double y, string langKey, Action click)
    {
        var bounds = ElementBounds.Fixed(x, y, _btn, _btn);
        _rail.Add(bounds);
        compo.AddInteractiveElement(new GuiElementTermIcon(capi, bounds, icon, false, pressed, click, false, false, true), langKey);
        compo.AddHoverText(Lang.Get("electricalprogressivestorage:" + langKey), CairoFont.WhiteSmallText(), 240, bounds.FlatCopy(), langKey + "-tip");
    }

    private void ToggleCraftView()
    {
        _craftView = !_craftView;
        _scrollRow = 0;
        QueueSetup();
    }

    private void SelectSort(SortMode mode)
    {
        if (_sort == mode)
            return;
        _sort = mode;
        _scrollRow = 0;
        QueueSetup();
    }

    private void QueueSetup()
    {
        if (_setupQueued)
            return;
        _setupQueued = true;
        capi.Event.EnqueueMainThreadTask(() =>
        {
            _setupQueued = false;
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
        text.Append((int)_sort).Append('|').Append(_craftView ? 'c' : 's').Append('|').Append(_search).Append('|');
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
            var craft = Inventory[i] is ItemSlotNetwork { CraftOnly: true };
            if (craft != _craftView)
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
        QueueSetup();
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
