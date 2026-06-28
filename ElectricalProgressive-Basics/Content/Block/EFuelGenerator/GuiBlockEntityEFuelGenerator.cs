using Cairo;
using System;
using System.Security.Cryptography;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EFuelGenerator;

public class GuiBlockEntityEFuelGenerator : GuiDialogBlockEntity
{
    private BlockEntityEFuelGenerator _betestgen;
    private float _gentemp;
    private float _fuelBurntime;
    private float _waterAmount;
    private bool _liquidAllowed;
    private float _currentConsumptionRate;
    
    public GuiBlockEntityEFuelGenerator(string dialogTitle, InventoryBase inventory, 
        BlockPos blockEntityPos, ICoreClientAPI capi, BlockEntityEFuelGenerator bentity) 
        : base(dialogTitle, inventory, blockEntityPos, capi)
    {
        if (IsDuplicate) return;
        
        capi.World.Player.InventoryManager.OpenInventory(inventory);
        _betestgen = bentity;
        SetupDialog();
    }
    
    private void OnSlotModified(int slotid)
    {
        capi.Event.EnqueueMainThreadTask(SetupDialog, "efuelgenerator");
    }
    
    public void SetupDialog()
    {
        ElementBounds dialogBounds = ElementBounds.Fixed(250, 60);
        ElementBounds dialog = ElementBounds.Fill.WithFixedPadding(0);
        ElementBounds fuelGrid = ElementStdBounds.SlotGrid(EnumDialogArea.None, 70, 50, 1, 1);
        ElementBounds stoveBounds = ElementBounds.Fixed(70, 70, 210, 150);
        
        ElementBounds waterBounds = ElementBounds.Fixed(17, 40, 40, 150);
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
        
        SingleComposer = capi.Gui.CreateCompo("efuelgenerator" + BlockEntityPosition, window)
            .AddShadedDialogBG(dialog, true, 5)
            .AddDialogTitleBar(Lang.Get("electricalprogressivebasics:efuelgenerator"), OnTitleBarClose)
            .BeginChildElements(dialog)
            .AddDynamicCustomDraw(stoveBounds, OnBgDraw, "symbolDrawer")
            .AddInset(waterBounds.ForkBoundingParent(2, 2, 2, 2), 2)
            .AddDynamicCustomDraw(waterBounds, OnWaterDraw, "waterDrawer")
            .AddItemSlotGrid(Inventory, SendInvPacket, 1, [0], fuelGrid, "fuelSlot")
            .AddDynamicCustomDraw(textPanelBounds, OnTextPanelDraw, "textPanelDrawer")
            .AddDynamicText("", outputText, textPanelBounds, "outputText")
            .EndChildElements()
            .Compose();
        
        Update(_betestgen.GenTemp, _betestgen.GetFuelBurnTime(), _betestgen.WaterAmount, 
               _betestgen.IsCurrentLiquidAllowed, _betestgen.CurrentConsumptionRate);
    }
    
    private void OnTextPanelDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        ctx.SetSourceRGB(0.1, 0.1, 0.15);
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Fill();
    
        ctx.SetSourceRGB(0.3, 0.3, 0.4);
        ctx.LineWidth = 1;
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Stroke();
    
        ctx.SetSourceRGB(0.5, 0.5, 0.6);
        ctx.LineWidth = 1;
        ctx.Rectangle(2, 2, currentBounds.InnerWidth - 4, currentBounds.InnerHeight - 4);
        ctx.Stroke();
    }
    
    private void SendInvPacket(object packet)
    {
        capi.Network.SendBlockEntityPacket(BlockEntityPosition.X, BlockEntityPosition.Y, 
            BlockEntityPosition.Z, packet);
    }
    
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
        float capacity = _betestgen?.WaterCapacity ?? 100f;

        float waterAmount = 0;
        float itemsPerLitre = 1f;
        bool hasLiquid = !liquidSlot.Empty;

        if (hasLiquid)
        {
            WaterTightContainableProps containableProps =
                BlockLiquidContainerBase.GetContainableProps(liquidSlot.Itemstack);
            if (containableProps != null)
            {
                itemsPerLitre = containableProps.ItemsPerLitre;
            }

            waterAmount = (float)liquidSlot.StackSize / itemsPerLitre;
        }

        float fullnessRelative = hasLiquid ? waterAmount / capacity : 0;
        fullnessRelative = Math.Min(Math.Max(fullnessRelative, 0f), 1f);

        double waterTopY = (1.0 - fullnessRelative) * currentBounds.InnerHeight;

        if (hasLiquid && waterAmount > 0)
        {
            ctx.Rectangle(0, waterTopY, currentBounds.InnerWidth, currentBounds.InnerHeight - waterTopY);

            if (!_liquidAllowed)
            {
                ctx.SetSourceRGBA(1, 0.3, 0.3, 0.7);
                ctx.Fill();
            }
            else
            {
                CompositeTexture compositeTexture =
                    BlockLiquidContainerBase.GetContainableProps(liquidSlot.Itemstack)?.Texture ??
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

        ctx.Save();
        ctx.SelectFontFace("sans-serif", FontSlant.Normal, FontWeight.Bold);

        string amountText;
        if (hasLiquid && waterAmount > 0)
        {
            if (waterAmount >= 1000)
                amountText = $"{waterAmount / 1000:F1}K"+ Lang.Get("electricalprogressivebasics:litres");
            else if (waterAmount >= 100)
                amountText = $"{waterAmount:F0}"+ Lang.Get("electricalprogressivebasics:litres");
            else
                amountText = $"{waterAmount:F1}"+ Lang.Get("electricalprogressivebasics:litres");
        }
        else
        {
            amountText = Lang.Get("electricalprogressivebasics:empty");
        }

        double fontSize = Math.Min(11, currentBounds.InnerWidth / 4.5);
        fontSize = Math.Max(8, fontSize);
        ctx.SetFontSize(fontSize);

        var textExtents = ctx.TextExtents(amountText);

        if (textExtents.Width > currentBounds.InnerWidth * 0.9)
        {
            fontSize = fontSize * (currentBounds.InnerWidth * 0.9 / textExtents.Width);
            ctx.SetFontSize(fontSize);
            textExtents = ctx.TextExtents(amountText);
        }

        double textX = (currentBounds.InnerWidth - textExtents.Width) / 2;
        double textY = currentBounds.InnerHeight - 3;

        if (hasLiquid && waterAmount > 0)
        {
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
        }
        else
        {
            ctx.SetSourceRGB(0.5, 0.5, 0.5);
            ctx.MoveTo(textX, textY);
            ctx.ShowText(amountText);
        }

        ctx.Restore();

        if (hasLiquid && !_liquidAllowed)
        {
            ctx.SetSourceRGB(1, 0.2, 0.2);
            ctx.LineWidth = 2;
            ctx.Rectangle(1, 1, currentBounds.InnerWidth - 2, currentBounds.InnerHeight - 2);
            ctx.Stroke();
        }
    }

    public void Update(float gentemp, float burntime, float waterAmount, bool liquidAllowed = true, float currentConsumptionRate = 0.1f)
    {
        if (!IsOpened())
            return;
        
        _gentemp = gentemp;
        _waterAmount = waterAmount;
        _liquidAllowed = liquidAllowed;
        _fuelBurntime = burntime;
        _currentConsumptionRate = currentConsumptionRate;
        
        string liquidName = Lang.Get("electricalprogressivebasics:empty");
        
        if (Inventory[1] != null && !Inventory[1].Empty)
        {
            liquidName = Inventory[1].Itemstack.GetName();
        }
        
        float capacity = _betestgen?.WaterCapacity ?? 100f;
        var config = _betestgen?.GetLiquidConfig();
        
        string newText = $" {_gentemp:F0} °C\n" + 
                        $" {_fuelBurntime:F0} " + Lang.Get("electricalprogressivebasics:gui-word-seconds") + "\n" +
                        $" {_waterAmount:F1}/{capacity:F0} "+ Lang.Get("electricalprogressivebasics:litres");
        
        if (!_liquidAllowed && Inventory[1] != null && !Inventory[1].Empty)
        {
            newText += $"  ({Lang.Get("electricalprogressivebasics:wrong_type")})";
        }
        
        newText += $"\n {liquidName}";
        
        if (_fuelBurntime > 0.1f && _gentemp > (config?.MinTemperature ?? 200))
        {
            newText += $"\n {Lang.Get("electricalprogressivebasics:current_liquid_consumption", $"{_currentConsumptionRate:F2}")}";
        }
        
        if (config != null && config.RequireSpecificLiquid && !_liquidAllowed && Inventory[1] != null && !Inventory[1].Empty)
        {
            newText += $"\n {Lang.Get("electricalprogressivebasics:requires")}: " + config.GetAllowedLiquidsText();
        }
        
        if (SingleComposer != null)
        {
            SingleComposer.GetDynamicText("outputText").SetNewText(newText);
            SingleComposer.GetCustomDraw("symbolDrawer")?.Redraw();
            SingleComposer.GetCustomDraw("waterDrawer")?.Redraw();
            SingleComposer.GetCustomDraw("textPanelDrawer")?.Redraw();
        }
    }
    
    private void OnTitleBarClose()
    {
        TryClose();
    }
    
    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        Inventory.SlotModified += OnSlotModified;
    }
    
    public override void OnGuiClosed()
    {
        Inventory.SlotModified -= OnSlotModified;
        SingleComposer?.GetSlotGrid("fuelSlot")?.OnGuiClosed(capi);
        base.OnGuiClosed();
    }
}