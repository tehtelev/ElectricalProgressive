﻿using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EFuelGenerator;

/// <summary>
/// GUI для электрического генератора на топливе.
/// Отображает состояние генератора, температуру, время горения и уровень жидкости.
/// </summary>
public class GuiBlockEntityEFuelGenerator : GuiDialogBlockEntity
{
    // === Поля ===
    private BlockEntityEFuelGenerator _betestgen;
    private float _gentemp;
    private float _fuelBurntime;
    private float _waterAmount;
    private bool _liquidAllowed;
    private float _currentConsumptionRate;
    
    // Таймер для ограничения частоты обновлений
    private long _lastUpdateTime = 0;
    private const int UPDATE_INTERVAL_MS = 500;
    
    // Кэш для отображаемых значений
    private int _lastDisplayTemp = -1;
    private int _lastDisplayBurnTime = -1;
    private string _lastDisplayWater = "";
    
    // === Конструктор ===
    
    public GuiBlockEntityEFuelGenerator(string dialogTitle, InventoryBase inventory, 
        BlockPos blockEntityPos, ICoreClientAPI capi, BlockEntityEFuelGenerator bentity) 
        : base(dialogTitle, inventory, blockEntityPos, capi)
    {
        if (IsDuplicate) return;
        
        capi.World.Player.InventoryManager.OpenInventory(inventory);
        _betestgen = bentity;
        SetupDialog();
    }
    
    // === Основные методы ===
    
    private void OnSlotModified(int slotid)
    {
        capi.Event.EnqueueMainThreadTask(SetupDialog, "termogen");
    }
    
    public void SetupDialog()
    {
        ElementBounds dialogBounds = ElementBounds.Fixed(250, 60);
        ElementBounds dialog = ElementBounds.Fill.WithFixedPadding(0);
        ElementBounds fuelGrid = ElementStdBounds.SlotGrid(EnumDialogArea.None, 70, 50, 1, 1);
        ElementBounds stoveBounds = ElementBounds.Fixed(70, 70, 210, 150);
        
        ElementBounds waterBounds = ElementBounds.Fixed(17, 40, 40, 150);
        // Темная панель для текста
        ElementBounds textPanelBounds = ElementBounds.Fixed(125, 45, 150, 145);
        
        dialog.BothSizing = ElementSizing.FitToChildren;
        dialog.WithChildren(dialogBounds, fuelGrid, textPanelBounds);
        
        ElementBounds window = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);
            
        if (capi.Settings.Bool["immersiveMouseMode"])
            window = window.WithAlignment(EnumDialogArea.RightMiddle).WithFixedAlignmentOffset(-12, 0);
        else
            window = window.WithAlignment(EnumDialogArea.CenterMiddle).WithFixedAlignmentOffset(20, 0);
        
        var outputText = CairoFont.WhiteDetailText().WithWeight(FontWeight.Normal);
        
        SingleComposer = capi.Gui.CreateCompo("termogen" + BlockEntityPosition, window)
            .AddShadedDialogBG(dialog, true, 5)
            .AddDialogTitleBar(Lang.Get("electricalprogressivebasics:termogen"), OnTitleBarClose)
            .BeginChildElements(dialog)
            .AddDynamicCustomDraw(stoveBounds, OnBgDraw, "symbolDrawer")
            .AddInset(waterBounds.ForkBoundingParent(2, 2, 2, 2), 2)
            .AddDynamicCustomDraw(waterBounds, OnWaterDraw, "waterDrawer")
            .AddItemSlotGrid(Inventory, SendInvPacket, 1, [0], fuelGrid, "fuelSlot")
            .AddDynamicCustomDraw(textPanelBounds, OnTextPanelDraw, "textPanelDrawer")
            .AddDynamicText("", outputText, textPanelBounds, "outputText")
            .EndChildElements()
            .Compose();
        
        var config = _betestgen?.GetLiquidConfig();
        
        _lastDisplayTemp = -1;
        _lastDisplayBurnTime = -1;
        _lastDisplayWater = "";
        _lastUpdateTime = 0;
        
        Update(_betestgen.GenTemp, _betestgen.GetFuelBurnTime(), _betestgen.WaterAmount, 
               config?.IsLiquidAllowed(_betestgen.WaterSlot.Itemstack) ?? true,
               _betestgen.CurrentConsumptionRate);
    }
    
    /// <summary>
    /// Отрисовка темной панели для текста (как в GuiDialogEAquaAccum)
    /// </summary>
    private void OnTextPanelDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        // Темный фон
        ctx.SetSourceRGB(0.1, 0.1, 0.15);
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Fill();
        
        // Тонкая рамка
        ctx.SetSourceRGB(0.3, 0.3, 0.4);
        ctx.LineWidth = 1;
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Stroke();
    }
    
    private void SendInvPacket(object packet)
    {
        capi.Network.SendBlockEntityPacket(BlockEntityPosition.X, BlockEntityPosition.Y, 
            BlockEntityPosition.Z, packet);
    }
    
    // === Методы отрисовки ===
    
    private void OnBgDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        ctx.Save();
        
        var m = ctx.Matrix;
        m.Translate(GuiElement.scaled(5), GuiElement.scaled(53));
        m.Scale(GuiElement.scaled(0.25), GuiElement.scaled(0.25));
        ctx.Matrix = m;
        
        capi.Gui.Icons.DrawFlame(ctx);
        
        double dy = 210 - 210 * (_gentemp / 1300);
        ctx.Rectangle(0, dy, 200, 210 - dy);
        ctx.Clip();
        
        var gradient = new LinearGradient(0, GuiElement.scaled(250), 0, 0);
        gradient.AddColorStop(0, new Color(1, 1, 0, 1));
        gradient.AddColorStop(1, new Color(1, 0, 0, 1));
        ctx.SetSource(gradient);
        
        capi.Gui.Icons.DrawFlame(ctx, 0, false, false);
        gradient?.Dispose();
        
        ctx.Restore();
    }
    
    private void OnWaterDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        ItemSlot liquidSlot = Inventory[1];
        if (liquidSlot.Empty)
        {
            ctx.Save();
            ctx.SetSourceRGBA(0.3, 0.3, 0.3, 0.5);
            ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
            ctx.Fill();
            
            ctx.SetSourceRGB(0, 0, 0);
            ctx.LineWidth = 1;
            
            double totalHeight = currentBounds.InnerHeight;
            int divisions = 10;
            
            for (int i = 1; i < divisions; i++)
            {
                double y = (totalHeight / divisions) * i;
                double lineWidth = (i % 2 == 0) ? 8 : 5;
                
                ctx.MoveTo(0, y);
                ctx.LineTo(lineWidth, y);
                ctx.Stroke();
                
                ctx.MoveTo(currentBounds.InnerWidth - lineWidth, y);
                ctx.LineTo(currentBounds.InnerWidth, y);
                ctx.Stroke();
            }
            
            ctx.Restore();
            return;
        }
        
        float itemsPerLitre = 1f;
        float capacity = _betestgen?.WaterCapacity ?? 100f;
        
        WaterTightContainableProps containableProps = BlockLiquidContainerBase.GetContainableProps(liquidSlot.Itemstack);
        if (containableProps != null)
        {
            itemsPerLitre = containableProps.ItemsPerLitre;
        }
        
        float fullnessRelative = (float)liquidSlot.StackSize / itemsPerLitre / capacity;
        fullnessRelative = Math.Min(Math.Max(fullnessRelative, 0f), 1f);
        
        double waterTopY = (1.0 - fullnessRelative) * currentBounds.InnerHeight;
        
        if (fullnessRelative > 0)
        {
            ctx.Rectangle(0, waterTopY, currentBounds.InnerWidth, currentBounds.InnerHeight - waterTopY);
            
            if (!_liquidAllowed)
            {
                ctx.SetSourceRGBA(1, 0.3, 0.3, 0.7);
                ctx.Fill();
            }
            else
            {
                CompositeTexture compositeTexture = containableProps?.Texture ?? 
                    liquidSlot.Itemstack.Collectible.Attributes?["inContainerTexture"]
                        .AsObject<CompositeTexture>(null, liquidSlot.Itemstack.Collectible.Code.Domain);
                
                if (compositeTexture != null)
                {
                    ctx.Save();
                    Matrix matrix = ctx.Matrix;
                    matrix.Scale(GuiElement.scaled(3.0), GuiElement.scaled(3.0));
                    ctx.Matrix = matrix;
                    
                    AssetLocation textureLoc = compositeTexture.Base.Clone().WithPathAppendixOnce(".png");
                    GuiElement.fillWithPattern(capi, ctx, textureLoc, true, false, compositeTexture.Alpha);
                    
                    ctx.Restore();
                }
                else
                {
                    ctx.SetSourceRGB(0.2, 0.4, 0.8);
                    ctx.Fill();
                }
            }
        }
        
        ctx.SetSourceRGB(0, 0, 0);
        ctx.LineWidth = 2;
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Stroke();
        
        ctx.SetSourceRGB(0, 0, 0);
        ctx.LineWidth = 1;
        
        double _totalHeight = currentBounds.InnerHeight;
        int _divisions = 10;
        
        for (int i = 1; i < _divisions; i++)
        {
            double y = (_totalHeight / _divisions) * i;
            double lineWidth = (i % 2 == 0) ? 8 : 5;
            
            ctx.MoveTo(0, y);
            ctx.LineTo(lineWidth, y);
            ctx.Stroke();
            
            ctx.MoveTo(currentBounds.InnerWidth - lineWidth, y);
            ctx.LineTo(currentBounds.InnerWidth, y);
            ctx.Stroke();
        }
        
        ctx.SetSourceRGB(0, 0, 0);
        ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(10);
        
        float currentLiters = (float)liquidSlot.StackSize / itemsPerLitre;
        string amountText = $"{currentLiters:0.##}L";
        var amountExtents = ctx.TextExtents(amountText);
        
        ctx.MoveTo(
            (currentBounds.InnerWidth - amountExtents.Width) / 2,
            currentBounds.InnerHeight - 3
        );
        ctx.ShowText(amountText);
    }
    
    /// <summary>
    /// Обновление данных в GUI с ограничением по времени
    /// </summary>
    public void Update(float gentemp, float burntime, float waterAmount, bool liquidAllowed = true, float currentConsumptionRate = 0.1f)
    {
        if (!IsOpened()) return;
        
        long currentTime = capi.ElapsedMilliseconds;
        
        _gentemp = gentemp;
        _waterAmount = waterAmount;
        _liquidAllowed = liquidAllowed;
        
        if (currentTime - _lastUpdateTime < UPDATE_INTERVAL_MS)
        {
            if (SingleComposer != null)
            {
                SingleComposer.GetCustomDraw("symbolDrawer").Redraw();
                SingleComposer.GetCustomDraw("waterDrawer").Redraw();
            }
            return;
        }
        
        _fuelBurntime = burntime;
        _currentConsumptionRate = currentConsumptionRate;
        
        string liquidName = Lang.Get("electricalprogressivebasics:empty");
        
        if (Inventory[1] != null && !Inventory[1].Empty)
        {
            liquidName = Inventory[1].Itemstack.GetName();
        }
        
        float capacity = _betestgen?.WaterCapacity ?? 100f;
        var config = _betestgen?.GetLiquidConfig();
        
        int displayTemp = (int)Math.Round(gentemp);
        int displayBurnTime = (int)Math.Round(burntime);
        string displayWater = waterAmount.ToString("0.0");
        
        if (displayTemp == _lastDisplayTemp && 
            displayBurnTime == _lastDisplayBurnTime && 
            displayWater == _lastDisplayWater)
        {
            _lastUpdateTime = currentTime;
            
            if (SingleComposer != null)
            {
                SingleComposer.GetCustomDraw("symbolDrawer").Redraw();
                SingleComposer.GetCustomDraw("waterDrawer").Redraw();
            }
            return;
        }
        
        _lastDisplayTemp = displayTemp;
        _lastDisplayBurnTime = displayBurnTime;
        _lastDisplayWater = displayWater;
        _lastUpdateTime = currentTime;
        
        // Формируем текст без эмодзи
        string newText = $"{displayTemp} °C\n" + 
                        $"{displayBurnTime} " + Lang.Get("electricalprogressivebasics:gui-word-seconds") + "\n" +
                        $"{displayWater}/{capacity.ToString("0.0")} L";
        
        if (!liquidAllowed && !Inventory[1].Empty)
        {
            newText += $" ({Lang.Get("electricalprogressivebasics:Wrong type")})";
        }
        
        newText += $"\n{liquidName}";
        
        if (burntime > 0.1f && gentemp > (config?.MinTemperature ?? 200))
        {
            newText += $"\n{Lang.Get("electricalprogressivebasics:Consumption")}: {currentConsumptionRate:F2} L/s";
        }
        
        if (config != null && config.RequireSpecificLiquid && !liquidAllowed && !Inventory[1].Empty)
        {
            newText += $"\n{Lang.Get("electricalprogressivebasics:Requires")}: " + config.GetAllowedLiquidsText();
        }
        
        if (SingleComposer != null)
        {
            SingleComposer.GetDynamicText("outputText").SetNewText(newText);
            SingleComposer.GetCustomDraw("symbolDrawer").Redraw();
            SingleComposer.GetCustomDraw("waterDrawer").Redraw();
        }
    }
    
    // === Обработка событий GUI ===
    
    private void OnTitleBarClose()
    {
        TryClose();
    }
    
    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        Inventory.SlotModified += OnSlotModified;
        
        _lastDisplayTemp = -1;
        _lastDisplayBurnTime = -1;
        _lastDisplayWater = "";
        _lastUpdateTime = 0;
    }
    
    public override void OnGuiClosed()
    {
        Inventory.SlotModified -= OnSlotModified;
        SingleComposer?.GetSlotGrid("fuelSlot")?.OnGuiClosed(capi);
        base.OnGuiClosed();
    }
}