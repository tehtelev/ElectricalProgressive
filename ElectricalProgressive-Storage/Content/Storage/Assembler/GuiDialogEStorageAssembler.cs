using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage.Assembler;

/// <summary>
/// Окно сборщика.
/// </summary>
public class GuiDialogEStorageAssembler : GuiDialogBlockEntity
{
    private ElementBounds? _close;
    private double _winW;
    private double _winH;
    private double _head;
    private bool _drag;
    private int _dragX;
    private int _dragY;
    private float _fill;
    private readonly BlockEntityEStorageAssembler _assembler;

    public GuiDialogEStorageAssembler(string dialogTitle, InventoryBase inventory, BlockPos pos, ICoreClientAPI capi, BlockEntityEStorageAssembler assembler)
        : base(dialogTitle, inventory, pos, capi)
    {
        _assembler = assembler;
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

    public override void OnRenderGUI(float deltaTime)
    {
        var fill = _assembler.Progress;
        if (Math.Abs(fill - _fill) > 0.001f)
        {
            _fill = fill;
            SingleComposer?.GetCustomDraw("work")?.Redraw();
        }

        base.OnRenderGUI(deltaTime);
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
        _fill = _assembler.Progress;

        var cell = Ae2Gui.Slots(0, 0, 1, 1);
        var top = Ae2Gui.Header + Ae2Gui.Gap;
        var grid = Ae2Gui.Slots(Ae2Gui.Margin, top, 3, 3);
        var output = Ae2Gui.Slots(grid.fixedX + grid.fixedWidth + 28, top + cell.fixedHeight, 1, 1);
        var bar = Ae2Gui.Box(output.fixedX + output.fixedWidth + 10, output.fixedY + 4, 10, output.fixedHeight - 8);
        _winW = bar.fixedX + bar.fixedWidth + Ae2Gui.Margin;
        _winH = top + grid.fixedHeight + Ae2Gui.Margin;
        _head = Ae2Gui.Header;
        _close = Ae2Gui.Box(_winW - Ae2Gui.Margin - 20, 11, 20, 20);
        var title = Ae2Gui.Box(Ae2Gui.Margin + 2, 8, _winW - 70, 26);
        var frame = ElementBounds.Fixed(0, 0, _winW, _winH);
        var bg = ElementBounds.Fill.WithFixedPadding(0);
        bg.BothSizing = ElementSizing.FitToChildren;
        bg.WithChildren(frame, _close, bar);

        var sheet = Ae2Gui.Window(capi, frame, _close.fixedX, _close.fixedY, _close.fixedWidth);

        ClearComposers();
        var compo = capi.Gui
            .CreateCompo("estorageassembler" + BlockEntityPosition, Ae2Gui.Dialog())
            .BeginChildElements(bg)
            .AddStaticElement(sheet)
            .AddStaticText(DialogTitle, Ae2Gui.Title(), title)
            .AddSlots(capi, Inventory, DoSendPacket, 3, [0, 1, 2, 3, 4, 5, 6, 7, 8], grid, "craft")
            .AddSlots(capi, Inventory, DoSendPacket, 1, [InventoryEStorageAssembler.Output], output, "out")
            .AddDynamicCustomDraw(bar, DrawWork, "work")
            .EndChildElements()
            .Compose();
        SingleComposer = compo;
    }

    private void DrawWork(Context ctx, ImageSurface _, ElementBounds bounds)
    {
        var frac = Math.Clamp(_fill, 0f, 1f);
        ctx.Antialias = Antialias.None;
        ctx.SetSourceRGB(Ae2Gui.Ink[0], Ae2Gui.Ink[1], Ae2Gui.Ink[2]);
        ctx.Rectangle(0, 0, bounds.InnerWidth, bounds.InnerHeight);
        ctx.Fill();
        if (frac <= 0)
            return;

        var dh = Math.Max(1, bounds.InnerHeight * frac);
        ctx.SetSourceRGB(0.45, 0.82, 0.95);
        ctx.Rectangle(1, bounds.InnerHeight - dh, Math.Max(1, bounds.InnerWidth - 2), dh);
        ctx.Fill();
    }
}
