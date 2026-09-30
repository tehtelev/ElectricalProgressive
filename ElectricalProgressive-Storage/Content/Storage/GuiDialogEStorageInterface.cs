using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage;

public class GuiDialogEStorageInterface : GuiDialogBlockEntity
{
    private const int Cols = 9;
    private const double TitleH = 22;
    private const double CloseS = 16;

    private ElementBounds? _close;
    private double _winW;
    private double _winH;
    private bool _drag;
    private int _dragX;
    private int _dragY;

    public GuiDialogEStorageInterface(string dialogTitle, InventoryBase inventory, BlockPos pos, ICoreClientAPI capi)
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
        const double labelH = 16;
        var gridMeasure = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, Cols, 1);
        var gridW = gridMeasure.fixedWidth;
        var gridH = gridMeasure.fixedHeight;
        var edge = GuiElementTermFrame.Inset;
        var contentW = gridW;
        var contentH = TitleH + 6 + 3 * (labelH + gridH + 6);
        _winW = edge + contentW + edge;
        _winH = edge + contentH + edge;

        var closeX = edge + contentW - CloseS;
        var closeY = edge + (TitleH - CloseS) / 2.0;
        var title = ElementBounds.Fixed(edge, edge, closeX - 8 - edge, TitleH);
        _close = ElementBounds.Fixed(closeX, closeY, CloseS, CloseS);
        var frame = ElementBounds.Fixed(0, 0, _winW, _winH);
        var bg = ElementBounds.Fill.WithFixedPadding(0);
        bg.BothSizing = ElementSizing.FitToChildren;
        bg.WithChildren(frame, _close);

        var dialog = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

        var font = CairoFont.WhiteDetailText().WithFontSize(14);
        ClearComposers();
        var compo = capi.Gui
            .CreateCompo("estorageinterface" + BlockEntityPosition, dialog)
            .BeginChildElements(bg)
            .AddStaticElement(new GuiElementTermFrame(capi, frame, closeX, closeY, CloseS))
            .AddStaticText(DialogTitle, CairoFont.WhiteDetailText().WithFontSize(18), title);

        var y = edge + TitleH + 6;
        AddRow(compo, font, "electricalprogressivestorage:estorage-interface-place", ItemSlotInterface.ExportAt, edge, ref y, gridW, gridH, labelH, "place");
        AddRow(compo, font, "electricalprogressivestorage:estorage-interface-patterns", ItemSlotInterface.PatternAt, edge, ref y, gridW, gridH, labelH, "patterns");
        AddRow(compo, font, "electricalprogressivestorage:estorage-interface-config", ItemSlotInterface.ConfigAt, edge, ref y, gridW, gridH, labelH, "config");
        SingleComposer = compo.EndChildElements().Compose();
    }

    private void AddRow(GuiComposer compo, CairoFont font, string label, int start, double x, ref double y, double gridW, double gridH, double labelH, string key)
    {
        compo.AddStaticText(Lang.Get(label), font, ElementBounds.Fixed(x, y, gridW, labelH));
        y += labelH;
        var ids = new int[Cols];
        for (var i = 0; i < Cols; i++)
            ids[i] = start + i;
        compo.AddItemSlotGrid(Inventory, DoSendPacket, Cols, ids, ElementBounds.Fixed(x, y, gridW, gridH), key);
        y += gridH + 6;
    }
}
