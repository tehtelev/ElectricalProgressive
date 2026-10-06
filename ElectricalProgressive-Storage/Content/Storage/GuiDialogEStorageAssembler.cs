using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage;

public class GuiDialogEStorageAssembler : GuiDialogBlockEntity
{
    private const double TitleH = 22;
    private const double CloseS = 16;

    private ElementBounds? _close;
    private double _winW;
    private double _winH;
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
        var edge = GuiElementTermFrame.Inset;
        return x < edge || y < edge || x > _winW - edge || y > _winH - edge;
    }

    private void SetupDialog()
    {
        _fill = _assembler.Progress;

        var gridMeasure = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, 3, 3);
        var one = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, 1, 1);
        var gridW = gridMeasure.fixedWidth;
        var gridH = gridMeasure.fixedHeight;
        var edge = GuiElementTermFrame.Inset;
        var pad = TermChrome.Pad;
        var gutter = TermChrome.Gutter;
        var gap = 22.0;
        var barW = 8.0;
        var barH = 36.0;
        var blockW = pad + gridW + gap + one.fixedWidth + pad;
        var headerH = pad + TitleH + pad;
        var craftBlockH = pad + gridH + pad;
        var contentH = headerH + gutter + craftBlockH;
        _winW = edge + blockW + edge;
        _winH = edge + contentH + edge;

        var closeX = edge + blockW - CloseS;
        var closeY = edge + pad + (TitleH - CloseS) / 2.0;
        var title = ElementBounds.Fixed(edge + pad, edge + pad, closeX - 8 - (edge + pad), TitleH);
        var header = ElementBounds.Fixed(edge, edge, closeX - 8 - edge, headerH);
        _close = ElementBounds.Fixed(closeX, closeY, CloseS, CloseS);
        var craftTop = edge + headerH + gutter;
        var gridY = craftTop + pad;
        var gridX = edge + pad;
        var grid = ElementBounds.Fixed(gridX, gridY, gridW, gridH);
        var output = ElementBounds.Fixed(gridX + gridW + gap, gridY + (gridH - one.fixedHeight) / 2.0, one.fixedWidth, one.fixedHeight);
        var bar = ElementBounds.Fixed(gridX + gridW + (gap - barW) / 2.0, gridY + (gridH - barH) / 2.0, barW, barH);
        var craftTray = ElementBounds.Fixed(edge, craftTop, blockW, craftBlockH);
        var frame = ElementBounds.Fixed(0, 0, _winW, _winH);
        var bg = ElementBounds.Fill.WithFixedPadding(0);
        bg.BothSizing = ElementSizing.FitToChildren;
        bg.WithChildren(frame, _close, bar);

        var dialog = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

        ClearComposers();
        SingleComposer = capi.Gui
            .CreateCompo("estorageassembler" + BlockEntityPosition, dialog)
            .BeginChildElements(bg)
            .AddStaticElement(new GuiElementTermFrame(capi, frame, closeX, closeY, CloseS))
            .AddStaticElement(new GuiElementTermPanel(capi, header))
            .AddStaticElement(new GuiElementTermPanel(capi, craftTray))
            .AddStaticText(DialogTitle, CairoFont.WhiteDetailText().WithFontSize(18), title)
            .AddItemSlotGrid(Inventory, DoSendPacket, 3, [0, 1, 2, 3, 4, 5, 6, 7, 8], grid, "craft")
            .AddItemSlotGrid(Inventory, DoSendPacket, 1, [InventoryEStorageAssembler.Output], output, "out")
            .AddDynamicCustomDraw(bar, DrawWork, "work")
            .EndChildElements()
            .Compose();
    }

    private void DrawWork(Context ctx, ImageSurface _, ElementBounds bounds)
    {
        var w = bounds.InnerWidth;
        var h = bounds.InnerHeight;
        TermChrome.Inset(ctx, 0, 0, w, h, 0.07, 0.07, 0.07);
        var frac = Math.Clamp(_fill, 0f, 1f);
        if (frac > 0 && h > 2 && w > 2)
        {
            var fillH = Math.Max(1, (h - 2) * frac);
            TermChrome.Gloss(ctx, 1, h - 1 - fillH, w - 2, fillH, 0.45, 0.66, 0.82);
        }
    }
}
