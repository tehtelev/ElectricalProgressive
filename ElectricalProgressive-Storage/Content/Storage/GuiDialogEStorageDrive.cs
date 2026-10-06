using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage;

public class GuiDialogEStorageDrive : GuiDialogBlockEntity
{
    private const int Cols = 2;
    private const int Rows = 4;
    private const double TitleH = 22;
    private const double CloseS = 16;

    private ElementBounds? _close;
    private double _winW;
    private double _winH;
    private bool _drag;
    private int _dragX;
    private int _dragY;

    public GuiDialogEStorageDrive(string dialogTitle, InventoryBase inventory, BlockPos pos, ICoreClientAPI capi)
        : base(dialogTitle, inventory, pos, capi)
    {
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
        var edge = GuiElementTermFrame.Inset;
        return x < edge || y < edge || x > _winW - edge || y > _winH - edge;
    }

    private void SetupDialog()
    {
        var gridMeasure = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, Cols, Rows);
        var gridW = gridMeasure.fixedWidth;
        var gridH = gridMeasure.fixedHeight;
        var edge = GuiElementTermFrame.Inset;
        var pad = TermChrome.Pad;
        var gutter = TermChrome.Gutter;
        var blockW = Math.Max(gridW + pad * 2, 230);
        var headerH = pad + TitleH + pad;
        var gridBlockH = pad + gridH + pad;
        var contentH = headerH + gutter + gridBlockH;
        _winW = edge + blockW + edge;
        _winH = edge + contentH + edge;

        var closeX = edge + blockW - CloseS;
        var closeY = edge + pad + (TitleH - CloseS) / 2.0;
        var title = ElementBounds.Fixed(edge + pad, edge + pad, closeX - 8 - (edge + pad), TitleH);
        var header = ElementBounds.Fixed(edge, edge, closeX - 8 - edge, headerH);
        _close = ElementBounds.Fixed(closeX, closeY, CloseS, CloseS);
        var gridTop = edge + headerH + gutter;
        var grid = ElementBounds.Fixed(edge + (blockW - gridW) / 2.0, gridTop + pad, gridW, gridH);
        var gridTray = ElementBounds.Fixed(edge, gridTop, blockW, gridBlockH);
        var frame = ElementBounds.Fixed(0, 0, _winW, _winH);
        var bg = ElementBounds.Fill.WithFixedPadding(0);
        bg.BothSizing = ElementSizing.FitToChildren;
        bg.WithChildren(frame, _close);

        var dialog = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

        ClearComposers();
        SingleComposer = capi.Gui
            .CreateCompo("estoragedrive" + BlockEntityPosition, dialog)
            .BeginChildElements(bg)
            .AddStaticElement(new GuiElementTermFrame(capi, frame, closeX, closeY, CloseS))
            .AddStaticElement(new GuiElementTermPanel(capi, header))
            .AddStaticElement(new GuiElementTermPanel(capi, gridTray))
            .AddStaticText(DialogTitle, CairoFont.WhiteDetailText().WithFontSize(18), title)
            .AddItemSlotGrid(Inventory, DoSendPacket, Cols, [0, 1, 2, 3, 4, 5, 6, 7], grid, "disks")
            .EndChildElements()
            .Compose();
    }
}
