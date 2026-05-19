﻿using Cairo;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.EAquaAccum;

public class GuiDialogEAquaAccum : GuiDialogBlockEntity
{
    private long lastRedrawMs;
    private float _pumpProgress;
    private float _waterAmount;
    private float _capacity;
    private BlockEntityEAquaAccum.CondensationStatus _status;
    private BlockEntityEAquaAccum _beWaterPump;
    private BlockPos _blockEntityPos;
    private ICoreClientAPI _capi;
    
    // Новые поля для отображения
    private float _condensationRate;
    private float _rainfall;
    private int _powerSetting;
    private int _maxConsumption;

    public GuiDialogEAquaAccum(
        string DialogTitle,
        InventoryBase Inventory,
        BlockPos BlockEntityPosition,
        ICoreClientAPI capi,
        BlockEntityEAquaAccum beWaterPump)
        : base(DialogTitle, Inventory, BlockEntityPosition, capi)
    {
        if (this.IsDuplicate)
            return;

        _capi = capi;
        _blockEntityPos = BlockEntityPosition;
        _beWaterPump = beWaterPump;

        if (_beWaterPump != null)
        {
            _waterAmount = _beWaterPump.LiquidAmount;
            _capacity = _beWaterPump.LiquidCapacity;
            _pumpProgress = _beWaterPump.PumpProgress;
            _status = _beWaterPump.GetCondensationStatus();
            
            // Получаем данные о конденсации
            UpdateCondensationData();
        }

        capi.World.Player.InventoryManager.OpenInventory(Inventory);
        this.SetupDialog();
    }
    
    private void UpdateCondensationData()
    {
        if (_beWaterPump == null) return;
        
        // Получаем данные через рефлексию или публичные методы
        var powerBehavior = _beWaterPump.PowerBehavior;
        if (powerBehavior != null)
        {
            _powerSetting = powerBehavior.PowerSetting;
            
            // Получаем maxConsumption через рефлексию (private поле)
            var field = powerBehavior.GetType().GetField("_maxConsumption", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                _maxConsumption = (int)field.GetValue(powerBehavior);
            }
        }
        
        // Получаем rainfall через рефлексию (private поле)
        var rainfallField = _beWaterPump.GetType().GetField("_currentRainfall",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (rainfallField != null)
        {
            _rainfall = (float)rainfallField.GetValue(_beWaterPump);
        }
        
        // Получаем condensation rate через рефлексию (private метод)
        var method = _beWaterPump.GetType().GetMethod("GetCurrentCondensationRate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (method != null)
        {
            _condensationRate = (float)method.Invoke(_beWaterPump, null);
        }
    }

    public void OnInventorySlotModified(int slotid)
    {
        this._capi.Event.EnqueueMainThreadTask(new Action(this.SetupDialog), "setupaquaaccumdlg");
    }

    private void SetupDialog()
    {
        var itemSlot = this._capi.World.Player.InventoryManager.CurrentHoveredSlot;
        if (itemSlot != null && itemSlot.Inventory == this.Inventory)
            this._capi.Input.TriggerOnMouseLeaveSlot(itemSlot);

        var bounds1 = ElementBounds.Fixed(0.0, 0.0, 300.0, 180.0); // Увеличил высоту
        var waterLevelBounds = ElementBounds.Fixed(250, 40, 40, 130);
        var statusBounds = ElementBounds.Fixed(10, 40, 200, 135); // Увеличил высоту

        var bounds4 = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bounds4.BothSizing = ElementSizing.FitToChildren;
        bounds4.WithChildren(bounds1);

        var bounds5 = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

        this.ClearComposers();
        this.SingleComposer = this._capi.Gui
            .CreateCompo("blockentityaquaaccum" + this.BlockEntityPosition?.ToString(), bounds5)
            .AddShadedDialogBG(bounds4)
            .AddDialogTitleBar(this.DialogTitle, new Action(this.OnTitleBarClose))
            .BeginChildElements(bounds4)
            .AddDynamicCustomDraw(statusBounds, new DrawDelegateWithBounds(this.OnStatusDraw), "statusDrawer")
            .AddInset(waterLevelBounds.ForkBoundingParent(2, 2, 2, 2), 2)
            .AddDynamicCustomDraw(waterLevelBounds, new DrawDelegateWithBounds(this.OnWaterDraw), "waterDrawer")
            .EndChildElements()
            .Compose();

        this.lastRedrawMs = this._capi.ElapsedMilliseconds;
    }

    public void Update(float pumpProgress, float waterAmount, float capacity, BlockEntityEAquaAccum.CondensationStatus status)
    {
        _pumpProgress = Math.Min(Math.Max(pumpProgress, 0f), 1f);
        _waterAmount = waterAmount;
        _capacity = capacity;
        _status = status;
        
        // Обновляем данные о конденсации при каждом обновлении
        UpdateCondensationData();

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
        ctx.SetSourceRGB(0.1, 0.1, 0.15);
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Fill();

        ctx.SetSourceRGB(1, 1, 1);
        ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(20);

        string statusText = GetStatusText(_status);
        ctx.MoveTo(5, 20);
        ctx.ShowText(statusText);

        // Отображаем мощность
        string powerText = $"Power: {_powerSetting}/{_maxConsumption} W";
        ctx.SetFontSize(18);
        ctx.SetSourceRGB(0.9, 0.9, 0.7);
        ctx.MoveTo(5, 40);
        ctx.ShowText(powerText);

        // Отображаем скорость конденсации, если устройство работает
        if (_status == BlockEntityEAquaAccum.CondensationStatus.Condensing)
        {
            string rateText = $"Rate: {_condensationRate:0.##} L/s";
            ctx.SetSourceRGB(0.6, 0.9, 0.6);
            ctx.MoveTo(5, 60);
            ctx.ShowText(rateText);
            
            // Отображаем влажность
            string rainfallText = GetRainfallText(_rainfall);
            string rainfallValueText = $"Rainfall: {rainfallText} ({_rainfall * 100:0}%)";
            ctx.SetSourceRGB(0.5, 0.7, 0.9);
            ctx.MoveTo(5, 80);
            ctx.ShowText(rainfallValueText);
        }
        
        // Отображаем заполненность бака
        string progressText = $"Tank: {_waterAmount:0.##}/{_capacity} L";
        ctx.SetFontSize(18);
        ctx.SetSourceRGB(0.8, 0.8, 0.8);
        ctx.MoveTo(5, _status == BlockEntityEAquaAccum.CondensationStatus.Condensing ? 100 : 60);
        ctx.ShowText(progressText);
    }
    
    private string GetRainfallText(float rainfall)
    {
        return rainfall switch
        {
            < 0.2f => "Very dry",
            < 0.4f => "Dry",
            < 0.6f => "Moderate",
            < 0.8f => "Wet",
            _ => "Very wet"
        };
    }

    private string GetStatusText(BlockEntityEAquaAccum.CondensationStatus status)
    {
        switch (status)
        {
            case BlockEntityEAquaAccum.CondensationStatus.Condensing:
                return "► Condensing water...";
            case BlockEntityEAquaAccum.CondensationStatus.NoPower:
                return "⚠ No power";
            case BlockEntityEAquaAccum.CondensationStatus.TankFull:
                return "■ Tank full";
            default:
                return "○ Idle";
        }
    }

private void OnWaterDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
{
    RefreshPumpData();

    if (_capacity <= 0)
    {
        DrawEmptyWaterBar(ctx, currentBounds);
        return;
    }

    float fullnessRelative = _waterAmount / _capacity;
    fullnessRelative = Math.Min(Math.Max(fullnessRelative, 0f), 1f);

    double waterTopY = (1.0 - fullnessRelative) * currentBounds.InnerHeight;

    // Рисуем воду
    if (_waterAmount > 0)
    {
        ctx.Rectangle(0, waterTopY, currentBounds.InnerWidth, currentBounds.InnerHeight - waterTopY);

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
            ctx.SetSourceRGB(0.2, 0.4, 0.8);
            ctx.Fill();
        }
    }

    // Рисуем рамку резервуара
    ctx.SetSourceRGB(0, 0, 0);
    ctx.LineWidth = 2;
    ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
    ctx.Stroke();

    // Рисуем деления как у линейки (как в прогресс-баре)
    ctx.SetSourceRGB(0, 0, 0);
    ctx.LineWidth = 1;
    
    double totalHeight = currentBounds.InnerHeight;
    int divisions = 10; // 10 делений
    
    for (int i = 1; i < divisions; i++)
    {
        double y = (totalHeight / divisions) * i;
        double lineWidth = (i % 2 == 0) ? 8 : 5; // четные длиннее, нечетные короче
        
        // Черточка слева
        ctx.MoveTo(0, y);
        ctx.LineTo(lineWidth, y);
        ctx.Stroke();
        
        // Черточка справа
        ctx.MoveTo(currentBounds.InnerWidth - lineWidth, y);
        ctx.LineTo(currentBounds.InnerWidth, y);
        ctx.Stroke();
    }

    // Текст с количеством литров (по центру внизу)
    ctx.SetSourceRGB(0, 0, 0);
    ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Bold);
    ctx.SetFontSize(12);
    string amountText = $"{_waterAmount:0.##}L";
    var amountExtents = ctx.TextExtents(amountText);
    ctx.MoveTo(
        (currentBounds.InnerWidth - amountExtents.Width) / 2,
        currentBounds.InnerHeight - 3
    );
    ctx.ShowText(amountText);
}

    private void DrawEmptyWaterBar(Context ctx, ElementBounds currentBounds)
    {
        ctx.SetSourceRGB(0.1, 0.1, 0.1);
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Fill();

        ctx.SetSourceRGB(0.5, 0.5, 0.5);
        ctx.LineWidth = 1;
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Stroke();

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

    private void OnTitleBarClose() => this?.TryClose();

    private void RefreshPumpData()
    {
        if (_blockEntityPos != null)
        {
            var be = _capi?.World?.BlockAccessor?.GetBlockEntity(_blockEntityPos) as BlockEntityEAquaAccum;
            if (be != null)
            {
                _beWaterPump = be;
                _waterAmount = be.LiquidAmount;
                _capacity = be.LiquidCapacity;
                _pumpProgress = be.PumpProgress;
                _status = be.GetCondensationStatus();
                UpdateCondensationData();
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
        base.OnGuiClosed();
    }
}