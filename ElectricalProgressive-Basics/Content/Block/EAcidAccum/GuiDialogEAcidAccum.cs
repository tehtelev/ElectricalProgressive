using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EAcidAccum;

public class GuiDialogEAcidAccum : GuiDialogBlockEntity
{
    private readonly ICoreClientAPI _capi;
    private readonly BlockPos _blockEntityPos;
    private BlockEntityEAcidAccum _be;
    private long lastRedrawMs;
    private float _acidAmount;
    private float _acidCapacity;
    private float _energy;
    private float _energyMax;

    public GuiDialogEAcidAccum(
        string dialogTitle,
        InventoryBase inventory,
        BlockPos blockEntityPosition,
        ICoreClientAPI capi,
        BlockEntityEAcidAccum be)
        : base(dialogTitle, inventory, blockEntityPosition, capi)
    {
        if (IsDuplicate)
            return;

        _capi = capi;
        _blockEntityPos = blockEntityPosition;
        _be = be;
        PullStats();
        capi.World.Player.InventoryManager.OpenInventory(inventory);
        SetupDialog();
    }

    private void PullStats()
    {
        var be = _capi.World.BlockAccessor.GetBlockEntity(_blockEntityPos) as BlockEntityEAcidAccum ?? _be;
        if (be == null)
            return;
        _be = be;
        _acidAmount = be.LiquidAmount;
        _acidCapacity = be.LiquidCapacity;
        var accum = be.AccumBehavior;
        _energy = accum?.GetCapacity() ?? 0;
        _energyMax = accum?.GetMaxCapacity() ?? 0;
    }

    public void OnInventorySlotModified(int slotid)
    {
        _capi.Event.EnqueueMainThreadTask(SetupDialog, "setupeacidaccumdlg");
    }

    private void SetupDialog()
    {
        var hovered = _capi.World.Player.InventoryManager.CurrentHoveredSlot;
        if (hovered != null && hovered.Inventory == Inventory)
            _capi.Input.TriggerOnMouseLeaveSlot(hovered);

        PullStats();

        var bounds1 = ElementBounds.Fixed(0.0, 0.0, 160.0, 150.0);
        var acidBounds = ElementBounds.Fixed(20, 30, 45, 110);
        var energyBounds = ElementBounds.Fixed(85, 30, 45, 110);

        var bounds4 = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bounds4.BothSizing = ElementSizing.FitToChildren;
        bounds4.WithChildren(bounds1);

        var bounds5 = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

        ClearComposers();
        SingleComposer = _capi.Gui
            .CreateCompo("blockentityeacidaccum" + BlockEntityPosition, bounds5)
            .AddShadedDialogBG(bounds4)
            .AddDialogTitleBar(DialogTitle, OnTitleBarClose)
            .BeginChildElements(bounds4)
            .AddInset(acidBounds.ForkBoundingParent(2, 2, 2, 2), 2)
            .AddDynamicCustomDraw(acidBounds, OnAcidDraw, "acidDrawer")
            .AddInset(energyBounds.ForkBoundingParent(2, 2, 2, 2), 2)
            .AddDynamicCustomDraw(energyBounds, OnEnergyDraw, "energyDrawer")
            .EndChildElements()
            .Compose();

        lastRedrawMs = _capi.ElapsedMilliseconds;
    }

    public void Update()
    {
        if (!IsOpened())
            return;
        if (_capi.ElapsedMilliseconds - lastRedrawMs <= 50L)
            return;
        PullStats();
        SingleComposer?.GetCustomDraw("acidDrawer")?.Redraw();
        SingleComposer?.GetCustomDraw("energyDrawer")?.Redraw();
        lastRedrawMs = _capi.ElapsedMilliseconds;
    }

    private void OnAcidDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        PullStats();
        DrawColumn(ctx, currentBounds,
            _acidCapacity > 0 ? GameMath.Clamp(_acidAmount / _acidCapacity, 0f, 1f) : 0f,
            FormatLitres(_acidAmount),
            drawAcidFill: true);
    }

    private void OnEnergyDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        PullStats();
        DrawColumn(ctx, currentBounds,
            _energyMax > 0 ? GameMath.Clamp(_energy / _energyMax, 0f, 1f) : 0f,
            FormatEnergy(_energy),
            drawAcidFill: false);
    }

    private void DrawColumn(Context ctx, ElementBounds currentBounds, float fill, string label, bool drawAcidFill)
    {
        fill = GameMath.Clamp(fill, 0f, 1f);
        double top = (1.0 - fill) * currentBounds.InnerHeight;

        if (fill > 0)
        {
            ctx.Rectangle(0, top, currentBounds.InnerWidth, currentBounds.InnerHeight - top);
            if (drawAcidFill)
                FillAcidPattern(ctx);
            else
            {
                ctx.SetSourceRGB(0.15, 0.55, 0.85);
                ctx.Fill();
            }
        }

        ctx.SetSourceRGB(0, 0, 0);
        ctx.LineWidth = 2;
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Stroke();

        ctx.SetSourceRGB(0, 0, 0);
        ctx.LineWidth = 1;
        const int divisions = 10;
        for (var i = 1; i < divisions; i++)
        {
            var y = currentBounds.InnerHeight / divisions * i;
            var lineWidth = i % 2 == 0 ? 8 : 5;
            ctx.MoveTo(0, y);
            ctx.LineTo(lineWidth, y);
            ctx.Stroke();
            ctx.MoveTo(currentBounds.InnerWidth - lineWidth, y);
            ctx.LineTo(currentBounds.InnerWidth, y);
            ctx.Stroke();
        }

        DrawBottomLabel(ctx, currentBounds, label);
    }

    private void FillAcidPattern(Context ctx)
    {
        var stack = _be?.LiquidSlot?.Itemstack;
        var props = stack != null ? BlockLiquidContainerBase.GetContainableProps(stack) : null;
        if (props?.Texture != null)
        {
            ctx.Save();
            var matrix = ctx.Matrix;
            matrix.Scale(GuiElement.scaled(3.0), GuiElement.scaled(3.0));
            ctx.Matrix = matrix;
            var textureLoc = props.Texture.Base.Clone().WithPathAppendixOnce(".png");
            GuiElement.fillWithPattern(_capi, ctx, textureLoc, true, false, props.Texture.Alpha);
            ctx.Restore();
            return;
        }

        ctx.SetSourceRGB(0.85, 0.75, 0.15);
        ctx.Fill();
    }

    private static void DrawBottomLabel(Context ctx, ElementBounds currentBounds, string text)
    {
        ctx.Save();
        ctx.SelectFontFace("sans-serif", FontSlant.Normal, FontWeight.Bold);
        var fontSize = Math.Max(8, Math.Min(11, currentBounds.InnerWidth / 4.5));
        ctx.SetFontSize(fontSize);
        var ext = ctx.TextExtents(text);
        if (ext.Width > currentBounds.InnerWidth * 0.9)
        {
            fontSize *= currentBounds.InnerWidth * 0.9 / ext.Width;
            ctx.SetFontSize(fontSize);
            ext = ctx.TextExtents(text);
        }

        var textX = (currentBounds.InnerWidth - ext.Width) / 2;
        var textY = currentBounds.InnerHeight - 3;
        ctx.SetSourceRGB(0, 0, 0);
        ctx.MoveTo(textX - 1, textY - 1);
        ctx.ShowText(text);
        ctx.MoveTo(textX + 1, textY - 1);
        ctx.ShowText(text);
        ctx.MoveTo(textX - 1, textY + 1);
        ctx.ShowText(text);
        ctx.MoveTo(textX + 1, textY + 1);
        ctx.ShowText(text);
        ctx.SetSourceRGB(1, 1, 1);
        ctx.MoveTo(textX, textY);
        ctx.ShowText(text);
        ctx.Restore();
    }

    private static string FormatLitres(float amount)
    {
        if (amount >= 100)
            return $"{amount:F0}L";
        if (amount > 0)
            return $"{amount:F1}L";
        return "0L";
    }

    private static string FormatEnergy(float energy)
    {
        if (energy >= 1000000)
            return $"{energy / 1000000f:F1}MJ";
        if (energy >= 1000)
            return $"{energy / 1000f:F0}kJ";
        return $"{(int)energy}J";
    }

    private void OnTitleBarClose() => TryClose();

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        Inventory.SlotModified += OnInventorySlotModified;
    }

    public override void OnGuiClosed()
    {
        Inventory.SlotModified -= OnInventorySlotModified;
        base.OnGuiClosed();
    }
}
