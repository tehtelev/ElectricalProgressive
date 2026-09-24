using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EBlastFurnace;

public class GuiDialogEBlastFurnace : GuiDialogBlockEntity
{
    private long lastRedrawMs;
    private float _recipeprogress;
    private BlockEntityEBlastFurnace _beBlastFurnace;
    private BlockPos _blockEntityPos;
    private ICoreClientAPI _capi;
    
    /// <summary>
    /// Событие, вызываемое при закрытии диалога
    /// </summary>
    public event Action OnDialogClosed;

    protected override double FloatyDialogPosition => 0.75;

    public GuiDialogEBlastFurnace(
        string DialogTitle,
        InventoryBase Inventory,
        BlockPos BlockEntityPosition,
        ICoreClientAPI capi)
        : base(DialogTitle, Inventory, BlockEntityPosition, capi)
    {
        if (this.IsDuplicate) return;
            
        _capi = capi;
        _blockEntityPos = BlockEntityPosition;
        
        var be = capi.World.BlockAccessor.GetBlockEntity(_blockEntityPos) as BlockEntityEBlastFurnace;
        if (be != null)
        {
            _beBlastFurnace = be;
            _recipeprogress = be.RecipeProgress;
        }
        
        capi.World.Player.InventoryManager.OpenInventory((IInventory)Inventory);
        this.SetupDialog();
    }

    public void OnInventorySlotModified(int slotid)
    {
        this.capi.Event.EnqueueMainThreadTask(new Action(this.SetupDialog), "setupblastfurnacedlg");
    }

    private void SetupDialog()
    {
        var itemSlot = this.capi.World.Player.InventoryManager.CurrentHoveredSlot;
        if (itemSlot != null && itemSlot.Inventory == this.Inventory)
            this.capi.Input.TriggerOnMouseLeaveSlot(itemSlot);
        else
            itemSlot = null;
            
        const double slot = 48;
        const double pad = 3;
        const double gap = 10;
        const double top = 40;
        const double barW = 96;
        const double barH = 20;

        var gridX = 12.0;
        var gridW = slot * 2 + pad;
        var gridH = slot * 2 + pad;
        var barX = gridX + gridW + gap;
        var outX = barX + barW + gap;
        var rowY = top + (gridH - slot) / 2;
        var barY = rowY + (slot - barH) / 2;

        var bounds1 = ElementBounds.Fixed(0.0, 0.0, outX + slot * 2 + pad + 16, top + gridH + 16);
        var inputGridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, gridX, top, 2, 2);
        var progressBounds = ElementBounds.Fixed(barX, barY, barW, barH);
        var outputSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, outX, rowY, 1, 1);
        var chanceSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, outX + slot + pad, rowY, 1, 1);
        
        var bounds4 = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bounds4.BothSizing = ElementSizing.FitToChildren;
        bounds4.WithChildren(bounds1);
        
        var bounds5 = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);
            
        this.ClearComposers();
        this.SingleComposer = this.capi.Gui
            .CreateCompo("blockentityeblastfurnace" + this.BlockEntityPosition?.ToString(), bounds5)
            .AddShadedDialogBG(bounds4)
            .AddDialogTitleBar(this.DialogTitle, new Action(this.OnTitleBarClose))
            .BeginChildElements(bounds4)
            
            .AddDynamicCustomDraw(progressBounds, new DrawDelegateWithBounds(this.OnProgressDraw), "progressDrawer")
            
            .AddItemSlotGrid((IInventory)this.Inventory, new Action<object>(this.SendInvPacket), 2, new int[4] { 0, 1, 4, 5 }, inputGridBounds, "inputSlot")
            .AddItemSlotGrid((IInventory)this.Inventory, new Action<object>(this.SendInvPacket), 1, new int[1] { 2 }, outputSlotBounds, "outputslot")
            .AddItemSlotGrid((IInventory)this.Inventory, new Action<object>(this.SendInvPacket), 1, new int[1] { 3 }, chanceSlotBounds, "chanceslot")
            
            .EndChildElements()
            .Compose();
            
        this.lastRedrawMs = this.capi.ElapsedMilliseconds;
        if (itemSlot == null) return;
        this.SingleComposer.OnMouseMove(new MouseEvent(this.capi.Input.MouseX, this.capi.Input.MouseY));
    }

    public void Update(float RecipeProgress)
    {
        _recipeprogress = Math.Min(Math.Max(RecipeProgress, 0f), 1f);
        if (!this.IsOpened()) return;
        if (this.capi.ElapsedMilliseconds - this.lastRedrawMs <= 50L) return;
        if (this.SingleComposer != null) this.SingleComposer.GetCustomDraw("progressDrawer")?.Redraw();
        this.lastRedrawMs = this.capi.ElapsedMilliseconds;
    }

    private void OnProgressDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        float progress = Math.Min(Math.Max(_recipeprogress, 0f), 1f);
        double fillWidth = currentBounds.InnerWidth * progress;
        
        ctx.SetSourceRGB(0, 0, 0);
        ctx.LineWidth = 3;
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Stroke();
        
        if (progress > 0)
        {
            using (var gradient = new LinearGradient(0, 0, fillWidth, 0))
            {
                gradient.AddColorStop(0.0, new Color(0.8, 0.2, 0.0, 1.0));
                gradient.AddColorStop(0.5, new Color(0.9, 0.1, 0.0, 1.0));
                gradient.AddColorStop(1.0, new Color(1.0, 0.0, 0.0, 1.0));
                ctx.SetSource(gradient);
                ctx.Rectangle(2, 2, fillWidth - 4, currentBounds.InnerHeight - 4);
                ctx.Fill();
            }
        }
        
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

    private void OnTitleBarClose() => this.TryClose();

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        this.Inventory.SlotModified += new Action<int>(this.OnInventorySlotModified);
        var be = _capi.World.BlockAccessor.GetBlockEntity(_blockEntityPos) as BlockEntityEBlastFurnace;
        if (be != null)
        {
            _recipeprogress = be.RecipeProgress;
            _beBlastFurnace = be;
        }
    }

    public override void OnGuiClosed()
    {
        base.OnGuiClosed();
        this.Inventory.SlotModified -= new Action<int>(this.OnInventorySlotModified);
        this.SingleComposer?.GetSlotGrid("inputSlot")?.OnGuiClosed(this.capi);
        this.SingleComposer?.GetSlotGrid("outputslot")?.OnGuiClosed(this.capi);
        this.SingleComposer?.GetSlotGrid("chanceslot")?.OnGuiClosed(this.capi);
        
        // Вызываем событие закрытия диалога
        OnDialogClosed?.Invoke();
    }
}