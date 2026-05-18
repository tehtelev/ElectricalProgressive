﻿using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EPress;

public class GuiDialogPress : GuiDialogBlockEntity
{
    private long lastRedrawMs;
    private float _recipeprogress;
    private BlockEntityEPress _bePress;
    private BlockPos _blockEntityPos;
    private ICoreClientAPI _capi;

    protected override double FloatyDialogPosition => 0.75;

    public GuiDialogPress(
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
        
        // Получаем начальные данные
        var be = capi.World.BlockAccessor.GetBlockEntity(_blockEntityPos) as BlockEntityEPress;
        if (be != null)
        {
            _bePress = be;
            _recipeprogress = be.RecipeProgress;
        }
        
        capi.World.Player.InventoryManager.OpenInventory((IInventory)Inventory);
        this.SetupDialog();
    }

    public void OnInventorySlotModified(int slotid)
    {
        this.capi.Event.EnqueueMainThreadTask(new Action(this.SetupDialog), "setuppressdlg");
    }

    private void SetupDialog()
    {
        var itemSlot = this.capi.World.Player.InventoryManager.CurrentHoveredSlot;
        if (itemSlot != null && itemSlot.Inventory == this.Inventory)
            this.capi.Input.TriggerOnMouseLeaveSlot(itemSlot);
        else
            itemSlot = (ItemSlot)null;
            
        // Слоты: 0,1 - вход, 2,3 - выход
        var bounds1 = ElementBounds.Fixed(0.0, 0.0, 250.0, 160.0);
        var inputSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 5.0, 45.0, 1, 2);
        var outputSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 200.0, 45.0, 1, 2);
        
        // Прогресс-бар (шире и выше)
        var progressBounds = ElementBounds.Fixed(55, 82, 140, 25);
        
        var bounds4 = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bounds4.BothSizing = ElementSizing.FitToChildren;
        bounds4.WithChildren(bounds1);
        
        var bounds5 = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);
            
        this.ClearComposers();
        this.SingleComposer = this.capi.Gui
            .CreateCompo("blockentitypress" + this.BlockEntityPosition?.ToString(), bounds5)
            .AddShadedDialogBG(bounds4)
            .AddDialogTitleBar(this.DialogTitle, new Action(this.OnTitleBarClose))
            .BeginChildElements(bounds4)
            
            // Прогресс-бар
            .AddDynamicCustomDraw(progressBounds, new DrawDelegateWithBounds(this.OnProgressDraw), "progressDrawer")
            
            // Слоты
            .AddItemSlotGrid((IInventory)this.Inventory, new Action<object>(this.SendInvPacket), 1, new int[2] { 0, 1 }, inputSlotBounds, "inputSlot")
            .AddItemSlotGrid((IInventory)this.Inventory, new Action<object>(this.SendInvPacket), 1, new int[2] { 2, 3 }, outputSlotBounds, "outputslot")
            
            // Подписи
            .AddStaticText(Lang.Get("electricalprogressive:input"), CairoFont.WhiteDetailText(), ElementBounds.Fixed(10, 150, 50, 20))
            .AddStaticText(Lang.Get("electricalprogressive:output"), CairoFont.WhiteDetailText(), ElementBounds.Fixed(205, 150, 50, 20))
            
            .EndChildElements()
            .Compose();
            
        this.lastRedrawMs = this.capi.ElapsedMilliseconds;
        if (itemSlot == null)
            return;
        this.SingleComposer.OnMouseMove(new MouseEvent(this.capi.Input.MouseX, this.capi.Input.MouseY));
    }

    public void Update(float RecipeProgress)
    {
        // Гарантируем что прогресс в диапазоне 0-1
        _recipeprogress = Math.Min(Math.Max(RecipeProgress, 0f), 1f);
        
        if (!this.IsOpened())
            return;
            
        // Обновляем раз в 50 мс для плавности
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
        
        // 1. Рисуем черную толстую рамку
        ctx.SetSourceRGB(0, 0, 0);
        ctx.LineWidth = 3;
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Stroke();
        
        // 2. Рисуем заливку прогресса
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
        
        // 3. Рисуем деления как у линейки (шкала)
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
        
        // 4. Текст прогресса - сбрасываем масштаб
        ctx.Save();
        ctx.SetSourceRGB(0, 0, 0);
        ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(18);
        
        string progressText = $"{progress:P0}";
        var extents = ctx.TextExtents(progressText);
        
        // Рассчитываем позицию в исходной системе координат
        double textX = (currentBounds.InnerWidth - extents.Width) / 2;
        double textY = (currentBounds.InnerHeight + extents.Height) / 2;
        
        ctx.MoveTo(textX, textY);
        ctx.ShowText(progressText);
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
        
        // Получаем актуальный прогресс при открытии
        var be = _capi.World.BlockAccessor.GetBlockEntity(_blockEntityPos) as BlockEntityEPress;
        if (be != null)
        {
            _recipeprogress = be.RecipeProgress;
            _bePress = be;
        }
    }

    public override void OnGuiClosed()
    {
        this.Inventory.SlotModified -= new Action<int>(this.OnInventorySlotModified);
        this.SingleComposer?.GetSlotGrid("inputSlot")?.OnGuiClosed(this.capi);
        this.SingleComposer?.GetSlotGrid("outputslot")?.OnGuiClosed(this.capi);
        base.OnGuiClosed();
    }
}