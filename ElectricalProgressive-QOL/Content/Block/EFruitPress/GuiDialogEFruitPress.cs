using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EFruitPress;

public class GuiDialogEFruitPress : GuiDialogBlockEntity
{
    private long lastRedrawMs;
    private float _recipeprogress;
    private float _waterAmount;
    private float _capacity;
    private BlockEntityEFruitPress _beFruitPress;
    private BlockPos _blockEntityPos;
    private ICoreClientAPI _capi;
    
    public GuiDialogEFruitPress(
        string DialogTitle,
        InventoryBase Inventory,
        BlockPos BlockEntityPosition,
        ICoreClientAPI capi,
        BlockEntityEFruitPress beFruitPress)
        : base(DialogTitle, Inventory, BlockEntityPosition, capi)
    {
        if (this.IsDuplicate)
            return;
            
        _capi = capi;
        _blockEntityPos = BlockEntityPosition;
        _beFruitPress = beFruitPress;
        
        // Получаем начальные данные о жидкости
        if (_beFruitPress != null)
        {
            _waterAmount = _beFruitPress.LiquidAmount;
            _capacity = _beFruitPress.LiquidCapacity;
            _recipeprogress = _beFruitPress.SqueezeProgress;
        }
        
        capi.World.Player.InventoryManager.OpenInventory(Inventory);
        this.SetupDialog();
    }
    
    public void OnInventorySlotModified(int slotid)
    {
        this._capi.Event.EnqueueMainThreadTask(new Action(this.SetupDialog), "setupfruitpressdlg");
    }
    
    private void SetupDialog()
    {
        var itemSlot = this._capi.World.Player.InventoryManager.CurrentHoveredSlot;
        if (itemSlot != null && itemSlot.Inventory == this.Inventory)
            this._capi.Input.TriggerOnMouseLeaveSlot(itemSlot);
        
        // Слоты: 0 - фрукты, 1 - бак, 2 - жмых
        var bounds1 = ElementBounds.Fixed(0.0, 0.0, 300.0, 150.0);
        var fruitBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 5.0, 70.0, 1, 1);
        var mashBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 180.0, 70.0, 1, 1);
        
        // Границы для уровня жидкости (шире и выше для лучшей видимости)
        var waterBounds = ElementBounds.Fixed(245, 35, 45, 110);
        
        // Границы для прогресс-бара (как в молоте)
        var progressBounds = ElementBounds.Fixed(55, 85, 123, 25);
        
        var bounds4 = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bounds4.BothSizing = ElementSizing.FitToChildren;
        bounds4.WithChildren(bounds1);
        
        var bounds5 = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);
            
        this.ClearComposers();
        this.SingleComposer = this._capi.Gui
            .CreateCompo("blockentityfruitpress" + this.BlockEntityPosition?.ToString(), bounds5)
            .AddShadedDialogBG(bounds4)
            .AddDialogTitleBar(this.DialogTitle, new Action(this.OnTitleBarClose))
            .BeginChildElements(bounds4)
            
            // Прогресс-бар отжима (улучшенный)
            .AddDynamicCustomDraw(progressBounds, new DrawDelegateWithBounds(this.OnProgressDraw), "progressDrawer")
            
            // Уровень жидкости (улучшенный)
            .AddInset(waterBounds.ForkBoundingParent(2, 2, 2, 2), 2)
            .AddDynamicCustomDraw(waterBounds, new DrawDelegateWithBounds(this.OnWaterDraw), "waterDrawer")
            
            // Слоты
            .AddItemSlotGrid(Inventory, new Action<object>(this.SendInvPacket), 1, [0], fruitBounds, "fruitSlot")
            .AddItemSlotGrid(Inventory, new Action<object>(this.SendInvPacket), 1, [2], mashBounds, "mashSlot")
            
            // Подписи
            .AddStaticText("Fruit", CairoFont.WhiteDetailText(), ElementBounds.Fixed(15, 120, 50, 20))
            .AddStaticText("Mash", CairoFont.WhiteDetailText(), ElementBounds.Fixed(190, 120, 50, 20))
            
            .EndChildElements()
            .Compose();
            
        this.lastRedrawMs = this._capi.ElapsedMilliseconds;
    }
    
    public void Update(float recipeProgress, float waterAmount, float capacity)
    {
        // Гарантируем, что прогресс в диапазоне 0-1
        _recipeprogress = Math.Min(Math.Max(recipeProgress, 0f), 1f);
        _waterAmount = waterAmount;
        _capacity = capacity;
        
        if (!this.IsOpened())
            return;
            
        // Обновляем раз в 50 мс для плавности (как в молоте)
        if (this._capi.ElapsedMilliseconds - this.lastRedrawMs <= 50L)
            return;
            
        if (this.SingleComposer != null)
        {
            this.SingleComposer.GetCustomDraw("progressDrawer")?.Redraw();
            this.SingleComposer.GetCustomDraw("waterDrawer")?.Redraw();
        }
        this.lastRedrawMs = this._capi.ElapsedMilliseconds;
    }
    
    // Улучшенный прогресс-бар (как в молоте)
    private void OnProgressDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        float progress = Math.Min(Math.Max(_recipeprogress, 0f), 1f);
        double fillWidth = currentBounds.InnerWidth * progress;
        
        // 1. Рисуем черную толстую рамку
        ctx.SetSourceRGB(0, 0, 0);
        ctx.LineWidth = 3;
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Stroke();
        
        // 2. Рисуем заливку прогресса с градиентом
        if (progress > 0)
        {
            using (var gradient = new LinearGradient(0, 0, fillWidth, 0))
            {
                gradient.AddColorStop(0.0, new Color(0.0, 0.6, 0.0, 1.0));  // Зеленый
                gradient.AddColorStop(0.5, new Color(0.8, 0.8, 0.0, 1.0));  // Желтый
                gradient.AddColorStop(1.0, new Color(0.8, 0.0, 0.0, 1.0));  // Красный
                
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
        
        // 4. Текст прогресса с масштабированием (как в молоте)
        int percent = (int)(progress * 100);
        string percentText = $"{percent}%";
        
        ctx.Save();
        ctx.SelectFontFace("sans-serif", FontSlant.Normal, FontWeight.Bold);
        
        // Размер шрифта относительно высоты прогресс-бара
        double fontSize = currentBounds.InnerHeight * 0.65;
        ctx.SetFontSize(fontSize);
        
        var textExtents = ctx.TextExtents(percentText);
        
        // Если текст слишком широкий - уменьшаем шрифт
        if (textExtents.Width > currentBounds.InnerWidth * 0.9)
        {
            fontSize = fontSize * (currentBounds.InnerWidth * 0.9 / textExtents.Width);
            ctx.SetFontSize(fontSize);
            textExtents = ctx.TextExtents(percentText);
        }
        
        double textX = (currentBounds.InnerWidth - textExtents.Width) / 2;
        double textY = (currentBounds.InnerHeight + textExtents.Height) / 2;
        
        // Белый цвет текста
        ctx.SetSourceRGB(1.0, 1.0, 1.0);
        ctx.MoveTo(textX, textY);
        ctx.ShowText(percentText);
        ctx.Restore();
    }
    
    // Улучшенный индикатор жидкости с нормальным отображением количества
    private void OnWaterDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        // ВСЕГДА пытаемся получить актуальные данные о жидкости
        float waterAmount = 0;
        float capacity = 0;
        ItemStack liquidStack = null;
        
        // Пытаемся получить BlockEntity заново каждый раз
        var be = _capi?.World?.BlockAccessor?.GetBlockEntity(_blockEntityPos) as BlockEntityEFruitPress;
        if (be != null)
        {
            // Обновляем ссылку
            _beFruitPress = be;
            
            // Получаем актуальные данные
            waterAmount = be.LiquidAmount;
            capacity = be.LiquidCapacity;
            liquidStack = be.LiquidSlot?.Itemstack;
            
            // Также обновляем локальные переменные
            _waterAmount = waterAmount;
            _capacity = capacity;
        }
        else
        {
            // Если не получилось, используем сохраненные значения
            waterAmount = _waterAmount;
            capacity = _capacity;
        }
        
        if (capacity <= 0) 
        {
            DrawEmptyWaterBar(ctx, currentBounds);
            return;
        }
        
        float fullnessRelative = waterAmount / capacity;
        fullnessRelative = Math.Min(Math.Max(fullnessRelative, 0f), 1f);
        
        double waterTopY = (1.0 - fullnessRelative) * currentBounds.InnerHeight;
        
        // Рисуем жидкость
        if (waterAmount > 0)
        {
            ctx.Rectangle(0, waterTopY, currentBounds.InnerWidth, currentBounds.InnerHeight - waterTopY);
            
            // Получаем текстуру жидкости
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
                else
                {
                    ctx.SetSourceRGB(0.2, 0.4, 0.8);
                    ctx.Fill();
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
        
        // Рисуем деления как у линейки (черточки слева и справа)
        ctx.SetSourceRGB(0, 0, 0);
        ctx.LineWidth = 1;
        
        double tankHeight = currentBounds.InnerHeight;
        int divisions = 10;
        
        for (int i = 1; i < divisions; i++)
        {
            double y = (tankHeight / divisions) * i;
            double lineWidth = (i % 2 == 0) ? 8 : 5;
            
            ctx.MoveTo(0, y);
            ctx.LineTo(lineWidth, y);
            ctx.Stroke();
            
            ctx.MoveTo(currentBounds.InnerWidth - lineWidth, y);
            ctx.LineTo(currentBounds.InnerWidth, y);
            ctx.Stroke();
        }
        
        // Отображаем количество литров с нормальным масштабированием
        ctx.Save();
        ctx.SelectFontFace("sans-serif", FontSlant.Normal, FontWeight.Bold);
        
        string amountText;
        if (waterAmount >= 1000)
            amountText = $"{waterAmount / 1000:F1}L";
        else if (waterAmount >= 100)
            amountText = $"{waterAmount:F0}L";
        else if (waterAmount >= 10)
            amountText = $"{waterAmount:F1}L";
        else if (waterAmount > 0)
            amountText = $"{waterAmount:F1}L";
        else
            amountText = "0L";
        
        // Автоматический размер шрифта
        double fontSize = Math.Min(11, currentBounds.InnerWidth / 4.5);
        fontSize = Math.Max(8, fontSize);
        ctx.SetFontSize(fontSize);
        
        var textExtents = ctx.TextExtents(amountText);
        
        // Если текст слишком широкий - уменьшаем шрифт
        if (textExtents.Width > currentBounds.InnerWidth * 0.9)
        {
            fontSize = fontSize * (currentBounds.InnerWidth * 0.9 / textExtents.Width);
            ctx.SetFontSize(fontSize);
            textExtents = ctx.TextExtents(amountText);
        }
        
        // Позиционируем текст внизу бака
        double textX = (currentBounds.InnerWidth - textExtents.Width) / 2;
        double textY = currentBounds.InnerHeight - 3;
        
        // Белый текст с черной обводкой для читаемости
        ctx.SetSourceRGB(0, 0, 0);
        ctx.MoveTo(textX - 1, textY - 1);
        ctx.ShowText(amountText);
        ctx.MoveTo(textX + 1, textY - 1);
        ctx.ShowText(amountText);
        ctx.MoveTo(textX - 1, textY + 1);
        ctx.ShowText(amountText);
        ctx.MoveTo(textX + 1, textY + 1);
        ctx.ShowText(amountText);
        
        ctx.SetSourceRGB(1, 1, 1);
        ctx.MoveTo(textX, textY);
        ctx.ShowText(amountText);
        ctx.Restore();
    }
    
    private void DrawEmptyWaterBar(Context ctx, ElementBounds currentBounds)
    {
        // Рисуем пустую шкалу
        ctx.SetSourceRGB(0.1, 0.1, 0.1);
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Fill();
        
        // Рамка
        ctx.SetSourceRGB(0, 0, 0);
        ctx.LineWidth = 2;
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Stroke();
        
        // Рисуем деления
        ctx.SetSourceRGB(0, 0, 0);
        ctx.LineWidth = 1;
        
        double tankHeight = currentBounds.InnerHeight;
        int divisions = 10;
        
        for (int i = 1; i < divisions; i++)
        {
            double y = (tankHeight / divisions) * i;
            double lineWidth = (i % 2 == 0) ? 8 : 5;
            
            ctx.MoveTo(0, y);
            ctx.LineTo(lineWidth, y);
            ctx.Stroke();
            
            ctx.MoveTo(currentBounds.InnerWidth - lineWidth, y);
            ctx.LineTo(currentBounds.InnerWidth, y);
            ctx.Stroke();
        }
        
        // Текст "Empty"
        ctx.Save();
        ctx.SelectFontFace("sans-serif", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(9);
        
        string emptyText = "Empty";
        var extents = ctx.TextExtents(emptyText);
        
        ctx.SetSourceRGB(0.5, 0.5, 0.5);
        ctx.MoveTo((currentBounds.InnerWidth - extents.Width) / 2, currentBounds.InnerHeight / 2);
        ctx.ShowText(emptyText);
        ctx.Restore();
    }
    
    private void SendInvPacket(object p)
    {
        this._capi.Network.SendBlockEntityPacket(this.BlockEntityPosition.X, this.BlockEntityPosition.Y,
            this.BlockEntityPosition.Z, p);
    }
    
    private void OnTitleBarClose() => this.TryClose();
    
    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        this.Inventory.SlotModified += new Action<int>(this.OnInventorySlotModified);
        
        // Принудительно обновляем данные при открытии GUI
        RefreshLiquidData();
    }
    
    private void RefreshLiquidData()
    {
        if (_blockEntityPos != null)
        {
            var be = _capi?.World?.BlockAccessor?.GetBlockEntity(_blockEntityPos) as BlockEntityEFruitPress;
            if (be != null)
            {
                _beFruitPress = be;
                _waterAmount = be.LiquidAmount;
                _capacity = be.LiquidCapacity;
                _recipeprogress = be.SqueezeProgress;
                
                // Форсируем перерисовку
                if (this.SingleComposer != null && this.IsOpened())
                {
                    this.SingleComposer.GetCustomDraw("progressDrawer")?.Redraw();
                    this.SingleComposer.GetCustomDraw("waterDrawer")?.Redraw();
                }
            }
        }
    }
    
    public override void OnGuiClosed()
    {
        this.Inventory.SlotModified -= new Action<int>(this.OnInventorySlotModified);
        if (this.SingleComposer != null)
        {
            this.SingleComposer.GetSlotGrid("fruitSlot")?.OnGuiClosed(this._capi);
            this.SingleComposer.GetSlotGrid("mashSlot")?.OnGuiClosed(this._capi);
        }
        base.OnGuiClosed();
    }
}