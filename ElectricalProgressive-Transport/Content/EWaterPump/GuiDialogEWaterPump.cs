using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EWaterPump;

public class GuiDialogEWaterPump : GuiDialogBlockEntity
{
    private long lastRedrawMs;
    private float _pumpProgress;
    private float _waterAmount;
    private float _capacity;
    private BlockEntityEWaterPump.PumpStatus _status;
    private BlockEntityEWaterPump _beWaterPump;
    private BlockPos _blockEntityPos;
    private ICoreClientAPI _capi;
    
    public GuiDialogEWaterPump(
        string DialogTitle,
        InventoryBase Inventory,
        BlockPos BlockEntityPosition,
        ICoreClientAPI capi,
        BlockEntityEWaterPump beWaterPump)
        : base(DialogTitle, Inventory, BlockEntityPosition, capi)
    {
        if (this.IsDuplicate)
            return;
            
        _capi = capi;
        _blockEntityPos = BlockEntityPosition;
        _beWaterPump = beWaterPump;
        
        // Получаем начальные данные
        if (_beWaterPump != null)
        {
            _waterAmount = _beWaterPump.LiquidAmount;
            _capacity = _beWaterPump.LiquidCapacity;
            _pumpProgress = _beWaterPump.PumpProgress;
            _status = _beWaterPump.GetPumpStatus();
        }
        
        capi.World.Player.InventoryManager.OpenInventory(Inventory);
        this.SetupDialog();
    }
    
    public void OnInventorySlotModified(int slotid)
    {
        this._capi.Event.EnqueueMainThreadTask(new Action(this.SetupDialog), "setupwaterpumpdlg");
    }
    
    private void SetupDialog()
    {
        var itemSlot = this._capi.World.Player.InventoryManager.CurrentHoveredSlot;
        if (itemSlot != null && itemSlot.Inventory == this.Inventory)
            this._capi.Input.TriggerOnMouseLeaveSlot(itemSlot);
        
        // Слот для воды
        var bounds1 = ElementBounds.Fixed(0.0, 0.0, 300.0, 150.0);
        var waterBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 30.0, 70.0, 1, 1);
        
        // Границы для уровня жидкости
        var waterLevelBounds = ElementBounds.Fixed(250, 40, 40, 100);
        
        // Границы для статуса
        var statusBounds = ElementBounds.Fixed(30, 40, 200, 40);
        
        var bounds4 = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bounds4.BothSizing = ElementSizing.FitToChildren;
        bounds4.WithChildren(bounds1);
        
        var bounds5 = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);
            
        this.ClearComposers();
        this.SingleComposer = this._capi.Gui
            .CreateCompo("blockentitywaterpump" + this.BlockEntityPosition?.ToString(), bounds5)
            .AddShadedDialogBG(bounds4)
            .AddDialogTitleBar(this.DialogTitle, new Action(this.OnTitleBarClose))
            .BeginChildElements(bounds4)
            
            // Статус помпы
            .AddDynamicCustomDraw(statusBounds, new DrawDelegateWithBounds(this.OnStatusDraw), "statusDrawer")
            
            // Уровень жидкости
            .AddInset(waterLevelBounds.ForkBoundingParent(2, 2, 2, 2), 2)
            .AddDynamicCustomDraw(waterLevelBounds, new DrawDelegateWithBounds(this.OnWaterDraw), "waterDrawer")
            
            // Слот для воды
            .AddItemSlotGrid(Inventory, new Action<object>(this.SendInvPacket), 1, [0], waterBounds, "waterSlot")
            
            // Подпись
            .AddStaticText("Water Tank", CairoFont.WhiteDetailText(), ElementBounds.Fixed(30, 120, 100, 20))
            
            .EndChildElements()
            .Compose();
            
        this.lastRedrawMs = this._capi.ElapsedMilliseconds;
    }
    
    public void Update(float pumpProgress, float waterAmount, float capacity, BlockEntityEWaterPump.PumpStatus status)
    {
        _pumpProgress = Math.Min(Math.Max(pumpProgress, 0f), 1f);
        _waterAmount = waterAmount;
        _capacity = capacity;
        _status = status;
        
        if (!this.IsOpened() || this._capi.ElapsedMilliseconds - this.lastRedrawMs <= 500L)
            return;
            
        if (this.SingleComposer != null)
        {
            this.SingleComposer.GetCustomDraw("statusDrawer").Redraw();
            this.SingleComposer.GetCustomDraw("waterDrawer").Redraw();
        }
        this.lastRedrawMs = this._capi.ElapsedMilliseconds;
    }
    
    private void OnStatusDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        // Рисуем фон
        ctx.SetSourceRGB(0.1, 0.1, 0.15);
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Fill();
        
        // Текст статуса
        ctx.SetSourceRGB(1, 1, 1);
        ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(12);
        
        string statusText = GetStatusText(_status);
        var extents = ctx.TextExtents(statusText);
        ctx.MoveTo(5, 15);
        ctx.ShowText(statusText);
        
        // Текст прогресса
        string progressText = $"Tank: {_waterAmount:0.##}/{_capacity} L";
        ctx.SetFontSize(10);
        ctx.SetSourceRGB(0.8, 0.8, 0.8);
        ctx.MoveTo(5, 35);
        ctx.ShowText(progressText);
    }
    
    private string GetStatusText(BlockEntityEWaterPump.PumpStatus status)
    {
        switch (status)
        {
            case BlockEntityEWaterPump.PumpStatus.Pumping:
                return "Pumping water...";
            case BlockEntityEWaterPump.PumpStatus.NoPower:
                return "No power";
            case BlockEntityEWaterPump.PumpStatus.InsufficientWater:
                return "Not enough water in area";
            case BlockEntityEWaterPump.PumpStatus.TankFull:
                return "Tank full";
            default:
                return "Idle";
        }
    }
    
    private void OnWaterDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        // Обновляем данные
        RefreshPumpData();
        
        if (_capacity <= 0) 
        {
            DrawEmptyWaterBar(ctx, currentBounds);
            return;
        }
        
        float fullnessRelative = _waterAmount / _capacity;
        fullnessRelative = Math.Min(Math.Max(fullnessRelative, 0f), 1f);
        
        double y = (1.0 - fullnessRelative) * currentBounds.InnerHeight;
        
        ctx.Rectangle(0, y, currentBounds.InnerWidth, currentBounds.InnerHeight - y);
        
        // Получаем текстуру жидкости
        ItemStack liquidStack = null;
        if (_beWaterPump != null)
        {
            liquidStack = _beWaterPump.LiquidSlot?.Itemstack;
        }
        
        if (liquidStack != null)
        {
            var containableProps = Vintagestory.GameContent.BlockLiquidContainerBase.GetContainableProps(liquidStack);
            if (containableProps?.Texture != null)
            {
                ctx.Save();
                Matrix matrix = ctx.Matrix;
                matrix.Scale(GuiElement.scaled(3.0), GuiElement.scaled(3.0));
                ctx.Matrix = matrix;
            
                AssetLocation textureLoc = containableProps.Texture.Base.Clone().WithPathAppendixOnce(".png");
                GuiElement.fillWithPattern(_capi, ctx, textureLoc, true, false, containableProps.Texture.Alpha);
            
                ctx.Restore();
            }
        }
        else
        {
            // Если нет данных о жидкости, используем стандартный цвет
            ctx.SetSourceRGB(0.2, 0.4, 0.8);
            ctx.Fill();
        }
        
        // Рамка уровня жидкости
        ctx.SetSourceRGB(0.8, 0.8, 0.8);
        ctx.LineWidth = 1;
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Stroke();
        
        // Отображаем количество (опционально)
        ctx.SetSourceRGB(1, 1, 1);
        ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(10);
        string amountText = $"{_waterAmount:0.##}L";
        var amountExtents = ctx.TextExtents(amountText);
        ctx.MoveTo(
            (currentBounds.InnerWidth - amountExtents.Width) / 2,
            currentBounds.InnerHeight - 5
        );
        ctx.ShowText(amountText);
    }
    
    private void DrawEmptyWaterBar(Context ctx, ElementBounds currentBounds)
    {
        // Рисуем пустую шкалу
        ctx.SetSourceRGB(0.1, 0.1, 0.1);
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Fill();
        
        // Рамка
        ctx.SetSourceRGB(0.5, 0.5, 0.5);
        ctx.LineWidth = 1;
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Stroke();
        
        // Текст "Empty"
        ctx.SetSourceRGB(0.7, 0.7, 0.7);
        ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(10);
        string emptyText = "Empty";
        var extents = ctx.TextExtents(emptyText);
        ctx.MoveTo(
            (currentBounds.InnerWidth - extents.Width) / 2,
            (currentBounds.InnerHeight + extents.Height) / 2
        );
        ctx.ShowText(emptyText);
    }
    
    private void SendInvPacket(object p)
    {
        this._capi.Network.SendBlockEntityPacket(this.BlockEntityPosition.X, this.BlockEntityPosition.Y,
            this.BlockEntityPosition.Z, p);
    }
    
    private void OnTitleBarClose() => this.TryClose();
    
    private void RefreshPumpData()
    {
        if (_blockEntityPos != null)
        {
            var be = _capi?.World?.BlockAccessor?.GetBlockEntity(_blockEntityPos) as BlockEntityEWaterPump;
            if (be != null)
            {
                _beWaterPump = be;
                _waterAmount = be.LiquidAmount;
                _capacity = be.LiquidCapacity;
                _pumpProgress = be.PumpProgress;
                _status = be.GetPumpStatus();
            }
        }
    }
    
    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        this.Inventory.SlotModified += new Action<int>(this.OnInventorySlotModified);
        RefreshPumpData();
    }
    
    public override void OnGuiClosed()
    {
        this.Inventory.SlotModified -= new Action<int>(this.OnInventorySlotModified);
        if (this.SingleComposer != null)
        {
            this.SingleComposer.GetSlotGrid("waterSlot").OnGuiClosed(this._capi);
        }
        base.OnGuiClosed();
    }
}