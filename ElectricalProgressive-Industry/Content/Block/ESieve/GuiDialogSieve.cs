using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ESieve;

public class GuiDialogSieve : GuiDialogBlockEntity
{
    private long lastRedrawMs;
    private float _recipeprogress;
    private BlockEntityESieve _beSieve;
    private BlockPos _blockEntityPos;
    private ICoreClientAPI _capi;

    protected override double FloatyDialogPosition => 0.75;

    public GuiDialogSieve(
        string DialogTitle,
        InventoryBase Inventory,
        BlockPos BlockEntityPosition,
        ICoreClientAPI capi)
        : base(DialogTitle, Inventory, BlockEntityPosition, capi)
    {
        if (this.IsDuplicate)
            return;

        _capi = capi;
        _blockEntityPos = BlockEntityPosition;

        var be = capi.World.BlockAccessor.GetBlockEntity(_blockEntityPos) as BlockEntityESieve;
        if (be != null)
        {
            _beSieve = be;
            _recipeprogress = be.RecipeProgress;
        }

        capi.World.Player.InventoryManager.OpenInventory((IInventory)Inventory);
        this.SetupDialog();
    }

    public void OnInventorySlotModified(int slotid)
    {
        this.capi.Event.EnqueueMainThreadTask(new Action(this.SetupDialog), "setupesievedlg");
    }

    private void SetupDialog()
    {
        var itemSlot = this.capi.World.Player.InventoryManager.CurrentHoveredSlot;
        if (itemSlot != null && itemSlot.Inventory == this.Inventory)
            this.capi.Input.TriggerOnMouseLeaveSlot(itemSlot);
        else
            itemSlot = (ItemSlot)null;

        // Параметры сетки 5x5
        const int outputCols = 5;
        const int outputRows = 5;
        const double slotSize = 40;
        const double padding = 6;
        
        double outputGridWidth = outputCols * (slotSize + padding) + padding;
        double outputGridHeight = outputRows * (slotSize + padding) + padding;
        
        double windowWidth = Math.Max(280, outputGridWidth + 40);
        double windowHeight = 180 + outputGridHeight;

        // Входной слот (по центру сверху)
        var inputBounds = ElementBounds.Fixed((windowWidth - slotSize) / 2, 20, slotSize, slotSize);
        
        // Прогресс-бар
        var progressBounds = ElementBounds.Fixed(15, 70, windowWidth - 30, 25);
        
        // Сетка выходных слотов
        var outputGridBounds = ElementBounds.Fixed(15, 110, outputGridWidth, outputGridHeight);
        
        // Массив ID слотов для выходов (1-25)
        int[] outputSlotIds = new int[25];
        for (int i = 0; i < 25; i++)
            outputSlotIds[i] = 1 + i;

        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        bgBounds.WithChildren(inputBounds, progressBounds, outputGridBounds);

        var dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

        this.ClearComposers();
        this.SingleComposer = this.capi.Gui
            .CreateCompo("blockentityesieve" + this.BlockEntityPosition?.ToString(), dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar(this.DialogTitle, new Action(this.OnTitleBarClose))
            .BeginChildElements(bgBounds)

            // Прогресс-бар
            .AddDynamicCustomDraw(progressBounds, new DrawDelegateWithBounds(this.OnProgressDraw), "progressDrawer")

            // Входной слот
            .AddItemSlotGrid((IInventory)this.Inventory, new Action<object>(this.SendInvPacket), 1, new int[1] { 0 }, inputBounds, "inputSlot")
            
            // Сетка выходных слотов 5x5
            .AddItemSlotGrid((IInventory)this.Inventory, new Action<object>(this.SendInvPacket), outputCols, outputSlotIds, outputGridBounds, "outputSlots")

            // Подписи
            .AddStaticText(Lang.Get("electricalprogressive:input"), CairoFont.WhiteDetailText(), 
                ElementBounds.Fixed(inputBounds.fixedX + 5, inputBounds.fixedY + slotSize + 2, 50, 15))
            .AddStaticText(Lang.Get("electricalprogressive:output"), CairoFont.WhiteDetailText(), 
                ElementBounds.Fixed(outputGridBounds.fixedX + 5, outputGridBounds.fixedY + outputGridHeight + 2, 60, 15))

            .EndChildElements()
            .Compose();

        this.lastRedrawMs = this.capi.ElapsedMilliseconds;

        if (itemSlot == null)
            return;
        this.SingleComposer.OnMouseMove(new MouseEvent(this.capi.Input.MouseX, this.capi.Input.MouseY));
    }

    public void Update(float RecipeProgress)
    {
        _recipeprogress = Math.Min(Math.Max(RecipeProgress, 0f), 1f);

        if (!this.IsOpened())
            return;

        if (this.capi.ElapsedMilliseconds - this.lastRedrawMs <= 50L)
            return;

        if (this.SingleComposer != null)
        {
            this.SingleComposer.GetCustomDraw("progressDrawer")?.Redraw();
        }
        this.lastRedrawMs = this.capi.ElapsedMilliseconds;
    }

    private void OnProgressDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        float progress = Math.Min(Math.Max(_recipeprogress, 0f), 1f);
        double fillWidth = currentBounds.InnerWidth * progress;

        // Рисуем черную рамку
        ctx.SetSourceRGB(0, 0, 0);
        ctx.LineWidth = 3;
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Stroke();

        // Рисуем заливку прогресса
        if (progress > 0)
        {
            using (var gradient = new LinearGradient(0, 0, fillWidth, 0))
            {
                gradient.AddColorStop(0.0, new Color(0.0, 0.6, 0.0, 1.0));
                gradient.AddColorStop(0.5, new Color(0.8, 0.8, 0.0, 1.0));
                gradient.AddColorStop(1.0, new Color(0.8, 0.0, 0.0, 1.0));

                ctx.SetSource(gradient);
                ctx.Rectangle(2, 2, fillWidth - 4, currentBounds.InnerHeight - 4);
                ctx.Fill();
            }
        }

        // Рисуем деления
        ctx.SetSourceRGB(0, 0, 0);
        ctx.LineWidth = 1;

        double totalWidth = currentBounds.InnerWidth;
        int divisions = 10;

        for (int i = 1; i < divisions; i++)
        {
            double x = (totalWidth / divisions) * i;
            double lineHeight = (i % 2 == 0) ? 8 : 5;

            ctx.MoveTo(x, currentBounds.InnerHeight - lineHeight);
            ctx.LineTo(x, currentBounds.InnerHeight);
            ctx.Stroke();

            ctx.MoveTo(x, 0);
            ctx.LineTo(x, lineHeight);
            ctx.Stroke();
        }

        // Текст прогресса
        int percent = (int)(progress * 100);
        string percentText = $"{percent}%";

        ctx.Save();
        ctx.SelectFontFace("sans-serif", FontSlant.Normal, FontWeight.Bold);

        double fontSize = currentBounds.InnerHeight * 0.65;
        ctx.SetFontSize(fontSize);

        var textExtents = ctx.TextExtents(percentText);

        if (textExtents.Width > currentBounds.InnerWidth * 0.9)
        {
            fontSize = fontSize * (currentBounds.InnerWidth * 0.9 / textExtents.Width);
            ctx.SetFontSize(fontSize);
            textExtents = ctx.TextExtents(percentText);
        }

        double textX = (currentBounds.InnerWidth - textExtents.Width) / 2;
        double textY = (currentBounds.InnerHeight + textExtents.Height) / 2;

        ctx.SetSourceRGB(1.0, 1.0, 1.0);
        ctx.MoveTo(textX, textY);
        ctx.ShowText(percentText);
        ctx.Restore();
    }

    private void SendInvPacket(object p)
    {
        this.capi.Network.SendBlockEntityPacket(this.BlockEntityPosition.X, this.BlockEntityPosition.Y,
            this.BlockEntityPosition.Z, p);
    }

    private void OnTitleBarClose() => this?.TryClose();

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        this.Inventory.SlotModified += new Action<int>(this.OnInventorySlotModified);

        var be = _capi.World.BlockAccessor.GetBlockEntity(_blockEntityPos) as BlockEntityESieve;
        if (be != null)
        {
            _recipeprogress = be.RecipeProgress;
            _beSieve = be;
        }
    }

    public override void OnGuiClosed()
    {
        this.Inventory.SlotModified -= new Action<int>(this.OnInventorySlotModified);
        this.SingleComposer?.GetSlotGrid("inputSlot")?.OnGuiClosed(this.capi);
        this.SingleComposer?.GetSlotGrid("outputSlots")?.OnGuiClosed(this.capi);
        base.OnGuiClosed();
    }
}