using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage.Bus;

/// <summary>
/// Окно фильтра шины: одна строка образцов.
/// </summary>
public class GuiDialogEStorageBus : GuiDialogEStoragePanel
{
    private const int Cols = 9;

    private readonly string _hint;
    private ElementBounds? _close;
    private double _winW;
    private double _winH;
    private double _head;
    private bool _drag;
    private int _dragX;
    private int _dragY;

    public GuiDialogEStorageBus(string dialogTitle, string hint, InventoryBase inventory, BlockPos pos, ICoreClientAPI capi, Facing facing)
        : base(dialogTitle, inventory, pos, capi, facing, "estoragebus")
    {
        _hint = hint;
        if (IsDuplicate)
            return;

        capi.World.Player.InventoryManager.OpenInventory(inventory);
        SetupDialog();
    }

    public override void OnMouseDown(MouseEvent args)
    {
        if (_close != null && _close.PointInside(capi.Input.MouseX, capi.Input.MouseY))
        {
            args.Handled = true;
            CloseIconPressed();
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

    private void SetupDialog()
    {
        var pitch = Ae2Gui.Slots(0, 0, 1, 1).fixedHeight;
        const double lab = 22;
        var slotsY = Ae2Gui.Header + Ae2Gui.Gap + lab;
        _winW = Ae2Gui.Margin * 2 + Cols * pitch;
        _winH = slotsY + pitch + Ae2Gui.Margin;
        _head = Ae2Gui.Header;
        _close = Ae2Gui.Box(_winW - Ae2Gui.Margin - 20, 11, 20, 20);
        var title = Ae2Gui.Box(Ae2Gui.Margin + 2, 8, _winW - 70, 26);
        var frame = ElementBounds.Fixed(0, 0, _winW, _winH);
        var bg = ElementBounds.Fill.WithFixedPadding(0);
        bg.BothSizing = ElementSizing.FitToChildren;
        bg.WithChildren(frame, _close);

        var sheet = Ae2Gui.Window(capi, frame, _close.fixedX, _close.fixedY, _close.fixedWidth);
        var ids = new int[Cols];
        for (var i = 0; i < Cols; i++)
            ids[i] = i;

        ClearComposers();
        SingleComposer = capi.Gui
            .CreateCompo(DialogKey, WindowBounds(_winW, _winH))
            .BeginChildElements(bg)
            .AddStaticElement(sheet)
            .AddStaticText(DialogTitle, Ae2Gui.Title(), title)
            .AddStaticText(_hint, Ae2Gui.Label(), Ae2Gui.Box(Ae2Gui.Margin, Ae2Gui.Header + Ae2Gui.Gap, _winW - Ae2Gui.Margin * 2, lab))
            .AddSlots(capi, Inventory, Send, Cols, ids, Ae2Gui.Slots(Ae2Gui.Margin, slotsY, Cols, 1), "filter", Ae2SlotMark.None)
            .EndChildElements()
            .Compose(false);
    }
}
