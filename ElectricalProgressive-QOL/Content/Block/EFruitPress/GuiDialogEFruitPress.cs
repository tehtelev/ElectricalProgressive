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
        var fruitBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 30.0, 70.0, 1, 1);
        var bucketBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 105.0, 70.0, 1, 1);
        var mashBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 180.0, 70.0, 1, 1);
        
        // Границы для уровня жидкости
        var waterBounds = ElementBounds.Fixed(250, 40, 40, 100);
        
        // Границы для прогресс-бара
        var progressBounds = ElementBounds.Fixed(30, 40, 200, 20);
        
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
            
            // Прогресс-бар отжима
            .AddDynamicCustomDraw(progressBounds, new DrawDelegateWithBounds(this.OnProgressDraw), "progressDrawer")
            
            // Уровень жидкости
            .AddInset(waterBounds.ForkBoundingParent(2, 2, 2, 2), 2)
            .AddDynamicCustomDraw(waterBounds, new DrawDelegateWithBounds(this.OnWaterDraw), "waterDrawer")
            
            // Слоты
            .AddItemSlotGrid(Inventory, new Action<object>(this.SendInvPacket), 1, [0], fruitBounds, "fruitSlot")
            .AddItemSlotGrid(Inventory, new Action<object>(this.SendInvPacket), 1, [1], bucketBounds, "bucketSlot")
            .AddItemSlotGrid(Inventory, new Action<object>(this.SendInvPacket), 1, [2], mashBounds, "mashSlot")
            
            // Подписи
            .AddStaticText("Fruit", CairoFont.WhiteDetailText(), ElementBounds.Fixed(30, 120, 50, 20))
            .AddStaticText("Bucket", CairoFont.WhiteDetailText(), ElementBounds.Fixed(105, 120, 50, 20))
            .AddStaticText("Mash", CairoFont.WhiteDetailText(), ElementBounds.Fixed(180, 120, 50, 20))
            
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
        
        if (!this.IsOpened() || this._capi.ElapsedMilliseconds - this.lastRedrawMs <= 500L)
            return;
            
        if (this.SingleComposer != null)
        {
            this.SingleComposer.GetCustomDraw("progressDrawer").Redraw();
            this.SingleComposer.GetCustomDraw("waterDrawer").Redraw();
        }
        this.lastRedrawMs = this._capi.ElapsedMilliseconds;
    }
    
    private void OnProgressDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        // Гарантируем корректный диапазон
        float progress = Math.Min(Math.Max(_recipeprogress, 0f), 1f);
        
        // Рамка прогресс-бара
        ctx.SetSourceRGB(0.2, 0.2, 0.2);
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Fill();
        
        // Заполнение прогресс-бара
        if (progress > 0)
        {
            double fillWidth = currentBounds.InnerWidth * progress;
            
            // Градиент от зеленого к желтому
            var gradient = new LinearGradient(0, 0, fillWidth, 0);
            gradient.AddColorStop(0.0, new Color(0.0, 0.6, 0.0, 1.0));  // Зеленый
            gradient.AddColorStop(0.5, new Color(0.8, 0.8, 0.0, 1.0));  // Желтый
            gradient.AddColorStop(1.0, new Color(1.0, 0.5, 0.0, 1.0));  // Оранжевый
            
            ctx.SetSource(gradient);
            ctx.Rectangle(0, 0, fillWidth, currentBounds.InnerHeight);
            ctx.Fill();
            gradient.Dispose();
        }
        
        // Текст прогресса
        ctx.SetSourceRGB(1, 1, 1);
        ctx.SelectFontFace("Arial", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(12);
        string progressText = $"Pressing: {progress:P0}"; // P0 = процент с 0 знаков после запятой
        var extents = ctx.TextExtents(progressText);
        ctx.MoveTo(
            (currentBounds.InnerWidth - extents.Width) / 2,
            (currentBounds.InnerHeight + extents.Height) / 2
        );
        ctx.ShowText(progressText);
    }
    
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
            // Если емкость нулевая, рисуем пустую шкалу
            DrawEmptyWaterBar(ctx, currentBounds);
            return;
        }
        
        float fullnessRelative = waterAmount / capacity;
        fullnessRelative = Math.Min(Math.Max(fullnessRelative, 0f), 1f);
        
        double y = (1.0 - fullnessRelative) * currentBounds.InnerHeight;
        
        ctx.Rectangle(0, y, currentBounds.InnerWidth, currentBounds.InnerHeight - y);
        
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
        string amountText = $"{waterAmount:0.##}L";
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
            this.SingleComposer.GetSlotGrid("fruitSlot").OnGuiClosed(this._capi);
            this.SingleComposer.GetSlotGrid("bucketSlot").OnGuiClosed(this._capi);
            this.SingleComposer.GetSlotGrid("mashSlot").OnGuiClosed(this._capi);
        }
        base.OnGuiClosed();
    }
}