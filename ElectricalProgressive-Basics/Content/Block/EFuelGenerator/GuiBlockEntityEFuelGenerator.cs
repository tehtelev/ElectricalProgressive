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
    private BlockEntityEFuelGenerator betestgen;     // Ссылка на сущность генератора
    private float _gentemp;                          // Текущая температура
    private float _fuelBurntime;                     // Время горения
    private float _waterAmount;                      // Количество жидкости
    
    // === Конструктор ===
    
    /// <summary>
    /// Создание GUI для генератора
    /// </summary>
    public GuiBlockEntityEFuelGenerator(string dialogTitle, InventoryBase inventory, 
        BlockPos blockEntityPos, ICoreClientAPI capi, BlockEntityEFuelGenerator bentity) 
        : base(dialogTitle, inventory, blockEntityPos, capi)
    {
        if (IsDuplicate) return;
        
        // Открытие инвентаря игроком
        capi.World.Player.InventoryManager.OpenInventory(inventory);
        betestgen = bentity;
        SetupDialog();
    }
    
    // === Основные методы ===
    
    /// <summary>
    /// Обработка изменения слота инвентаря
    /// </summary>
    private void OnSlotModified(int slotid)
    {
        // Перестройка диалога в основном потоке
        capi.Event.EnqueueMainThreadTask(SetupDialog, "termogen");
    }
    
    /// <summary>
    /// Настройка и создание элементов GUI
    /// </summary>
    public void SetupDialog()
    {
        // Определение границ элементов
        ElementBounds dialogBounds = ElementBounds.Fixed(250, 60);
        ElementBounds dialog = ElementBounds.Fill.WithFixedPadding(0);
        ElementBounds fuelGrid = ElementStdBounds.SlotGrid(EnumDialogArea.None, 80, 50, 1, 1);
        ElementBounds stoveBounds = ElementBounds.Fixed(80, 70, 210, 150);
        
        ElementBounds waterBounds = ElementBounds.Fixed(17, 40, 40, 150);
        ElementBounds textBounds = ElementBounds.Fixed(145, 50, 121, 100);
        
        dialog.BothSizing = ElementSizing.FitToChildren;
        dialog.WithChildren(dialogBounds, fuelGrid, textBounds);
        
        // Настройка позиции окна
        ElementBounds window = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);
            
        if (capi.Settings.Bool["immersiveMouseMode"])
            window = window.WithAlignment(EnumDialogArea.RightMiddle).WithFixedAlignmentOffset(-12, 0);
        else
            window = window.WithAlignment(EnumDialogArea.CenterMiddle).WithFixedAlignmentOffset(20, 0);
        
        var outputText = CairoFont.WhiteDetailText().WithWeight(FontWeight.Normal);
        
        // Создание композитора GUI
        SingleComposer = capi.Gui.CreateCompo("termogen" + BlockEntityPosition, window)
            .AddShadedDialogBG(dialog, true, 5)
            .AddDialogTitleBar(Lang.Get("electricalprogressivebasics:termogen"), OnTitleBarClose)
            .BeginChildElements(dialog)
            .AddDynamicCustomDraw(stoveBounds, OnBgDraw, "symbolDrawer")
            .AddInset(waterBounds.ForkBoundingParent(2, 2, 2, 2), 2)
            .AddDynamicCustomDraw(waterBounds, OnWaterDraw, "waterDrawer")
            .AddItemSlotGrid(Inventory, SendInvPacket, 1, new int[] { 0 }, fuelGrid, "fuelSlot")
            .AddDynamicText("", outputText, textBounds, "outputText")
            .EndChildElements()
            .Compose();
        
        // Инициализация значений
        Update(betestgen.GenTemp, betestgen.GetFuelBurnTime(), betestgen.WaterAmount);
    }
    
    /// <summary>
    /// Отправка пакета изменения инвентаря
    /// </summary>
    private void SendInvPacket(object packet)
    {
        capi.Network.SendBlockEntityPacket(BlockEntityPosition.X, BlockEntityPosition.Y, 
            BlockEntityPosition.Z, packet);
    }
    
    // === Методы отрисовки ===
    
    /// <summary>
    /// Отрисовка фона и индикатора температуры
    /// </summary>
    private void OnBgDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        ctx.Save();
        
        // Настройка трансформации для иконки огня
        var m = ctx.Matrix;
        m.Translate(GuiElement.scaled(5), GuiElement.scaled(53));
        m.Scale(GuiElement.scaled(0.25), GuiElement.scaled(0.25));
        ctx.Matrix = m;
        
        // Отрисовка базовой иконки огня
        capi.Gui.Icons.DrawFlame(ctx);
        
        // Расчет уровня температуры для градиента
        double dy = 210 - 210 * (_gentemp / 1300);
        ctx.Rectangle(0, dy, 200, 210 - dy);
        ctx.Clip();
        
        // Создание градиента для индикатора температуры
        var gradient = new LinearGradient(0, GuiElement.scaled(250), 0, 0);
        gradient.AddColorStop(0, new Color(1, 1, 0, 1));  // Желтый (холодный)
        gradient.AddColorStop(1, new Color(1, 0, 0, 1));  // Красный (горячий)
        ctx.SetSource(gradient);
        
        // Отрисовка цветной части огня
        capi.Gui.Icons.DrawFlame(ctx, 0, false, false);
        gradient.Dispose();
        
        ctx.Restore();
    }
    
    /// <summary>
    /// Отрисовка уровня жидкости
    /// </summary>
    private void OnWaterDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        ItemSlot liquidSlot = Inventory[1];
        if (liquidSlot.Empty)
            return;
        
        float itemsPerLitre = 1f;
        float capacity = betestgen?.WaterCapacity ?? 100f;
        
        // Получение свойств жидкости
        WaterTightContainableProps containableProps = BlockLiquidContainerBase.GetContainableProps(liquidSlot.Itemstack);
        if (containableProps != null)
        {
            itemsPerLitre = containableProps.ItemsPerLitre;
        }
        
        // Расчет уровня заполнения
        float fullnessRelative = (float)liquidSlot.StackSize / itemsPerLitre / capacity;
        fullnessRelative = Math.Min(Math.Max(fullnessRelative, 0f), 1f);
        
        double y = (1.0 - fullnessRelative) * currentBounds.InnerHeight;
        
        // Определение области для отрисовки жидкости
        ctx.Rectangle(0, y, currentBounds.InnerWidth, currentBounds.InnerHeight - y);
        
        // Получение текстуры жидкости
        CompositeTexture compositeTexture = containableProps?.Texture ?? 
            liquidSlot.Itemstack.Collectible.Attributes?["inContainerTexture"]
                .AsObject<CompositeTexture>(null, liquidSlot.Itemstack.Collectible.Code.Domain);
        
        if (compositeTexture != null)
        {
            ctx.Save();
            Matrix matrix = ctx.Matrix;
            matrix.Scale(GuiElement.scaled(3.0), GuiElement.scaled(3.0));
            ctx.Matrix = matrix;
            
            // Загрузка и отрисовка текстуры
            AssetLocation textureLoc = compositeTexture.Base.Clone().WithPathAppendixOnce(".png");
            GuiElement.fillWithPattern(capi, ctx, textureLoc, true, false, compositeTexture.Alpha);
            
            ctx.Restore();
        }
    }
    
    /// <summary>
    /// Обновление данных в GUI
    /// </summary>
    public void Update(float gentemp, float burntime, float waterAmount)
    {
        if (!IsOpened()) return;
        
        _gentemp = gentemp;
        _fuelBurntime = burntime;
        _waterAmount = waterAmount;
        
        // Получение названия жидкости
        string liquidName = "Empty";
        if (Inventory[1] != null && !Inventory[1].Empty)
        {
            liquidName = Inventory[1].Itemstack.GetName();
        }
        
        // Получение вместимости из сущности генератора
        float capacity = betestgen?.WaterCapacity ?? 100f;
        
        // Формирование текста информации
        var newText = (int)gentemp + " °C\n" + 
                     (int)burntime + " " + Lang.Get("electricalprogressivebasics:gui-word-seconds") + "\n" +
                     "Liquid: " + waterAmount.ToString("0.0") + "/" + capacity.ToString("0.0") + " L\n" +
                     liquidName;
        
        // Обновление элементов GUI
        if (SingleComposer != null)
        {
            SingleComposer.GetDynamicText("outputText").SetNewText(newText);
            SingleComposer.GetCustomDraw("symbolDrawer").Redraw();
            SingleComposer.GetCustomDraw("waterDrawer").Redraw();
        }
    }
    
    // === Обработка событий GUI ===
    
    /// <summary>
    /// Обработка закрытия через заголовок
    /// </summary>
    private void OnTitleBarClose()
    {
        TryClose();
    }
    
    /// <summary>
    /// Обработка открытия GUI
    /// </summary>
    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        Inventory.SlotModified += OnSlotModified;
    }
    
    /// <summary>
    /// Обработка закрытия GUI
    /// </summary>
    public override void OnGuiClosed()
    {
        Inventory.SlotModified -= OnSlotModified;
        SingleComposer.GetSlotGrid("fuelSlot").OnGuiClosed(capi);
        base.OnGuiClosed();
    }
}