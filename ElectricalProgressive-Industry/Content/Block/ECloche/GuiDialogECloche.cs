using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.ECloche;

public class GuiDialogECloche : GuiDialogBlockEntity
{
    private long _lastRedrawMs;
    private BlockEntityECloche _be;
    private bool _auto;

    public GuiDialogECloche(string dialogTitle, InventoryBase inventory, BlockPos blockEntityPosition, ICoreClientAPI capi)
        : base(dialogTitle, inventory, blockEntityPosition, capi)
    {
        if (IsDuplicate)
            return;

        _be = capi.World.BlockAccessor.GetBlockEntity(blockEntityPosition) as BlockEntityECloche;
        _auto = _be?.AutoReplant == true;
        capi.World.Player.InventoryManager.OpenInventory(inventory);
        SetupDialog();
    }

    public void Refresh()
    {
        if (!IsOpened() || capi.ElapsedMilliseconds - _lastRedrawMs <= 80L)
            return;

        _be = capi.World.BlockAccessor.GetBlockEntity(BlockEntityPosition) as BlockEntityECloche;
        SingleComposer?.GetCustomDraw("waterDrawer")?.Redraw();
        SingleComposer?.GetCustomDraw("seedHint")?.Redraw();
        SingleComposer?.GetCustomDraw("fertHint")?.Redraw();
        SingleComposer?.GetCustomDraw("outHint")?.Redraw();
        SingleComposer?.GetDynamicText("statusText")?.SetNewText(StatusText());
        _lastRedrawMs = capi.ElapsedMilliseconds;
    }

    private void SetupDialog()
    {
        double top = GuiStyle.TitleBarHeight + 6;
        var seed = ElementBounds.Fixed(0, top, 48, 48);
        var fert = ElementBounds.Fixed(56, top, 48, 48);
        var outputs = ElementBounds.Fixed(120, top, 204, 48);
        var autoBtn = ElementBounds.Fixed(0, top + 52, 118, 22);
        var status = ElementBounds.Fixed(0, top + 78, 280, 150);
        var tank = ElementBounds.Fixed(292, top + 78, 40, 150);

        var bg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bg.BothSizing = ElementSizing.FitToChildren;
        bg.WithChildren(seed, fert, outputs, autoBtn, status, tank);

        var dialog = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

        ClearComposers();
        SingleComposer = capi.Gui.CreateCompo("ecloche" + BlockEntityPosition, dialog)
            .AddShadedDialogBG(bg)
            .AddDialogTitleBar(DialogTitle, OnTitleBarClose)
            .BeginChildElements(bg)
            .AddItemSlotGrid(Inventory, SendInvPacket, 1, [0], seed, "seed")
            .AddDynamicCustomDraw(seed, OnSeedHint, "seedHint")
            .AddItemSlotGrid(Inventory, SendInvPacket, 1, [1], fert, "fert")
            .AddDynamicCustomDraw(fert, OnFertHint, "fertHint")
            .AddItemSlotGrid(Inventory, SendInvPacket, 4, [2, 3, 4, 5], outputs, "output")
            .AddDynamicCustomDraw(outputs, OnOutputHint, "outHint")
            .AddSmallButton(AutoLabel(), OnToggleAuto, autoBtn, EnumButtonStyle.Small, "autoBtn")
            .AddDynamicCustomDraw(status, OnStatusBg, "statusBg")
            .AddDynamicText(StatusText(), CairoFont.WhiteDetailText(), ElementBounds.Fixed(6, top + 82, 268, 142), "statusText")
            .AddInset(tank.ForkBoundingParent(2, 2, 2, 2), 2)
            .AddDynamicCustomDraw(tank, OnWaterDraw, "waterDrawer")
            .EndChildElements()
            .Compose();
    }

    private void OnSeedHint(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        if (Inventory?[0]?.Empty != true)
            return;

        HintBg(ctx, bounds);
        double cx = bounds.InnerWidth / 2;
        double cy = bounds.InnerHeight / 2 + 4;
        ctx.SetSourceRGBA(0.85, 0.75, 0.35, 0.85);
        ctx.Arc(cx, cy + 4, 6, 0, Math.PI * 2);
        ctx.Fill();
        ctx.SetSourceRGBA(0.35, 0.7, 0.32, 0.9);
        ctx.MoveTo(cx, cy + 2);
        ctx.CurveTo(cx - 10, cy - 10, cx - 2, cy - 18, cx, cy - 14);
        ctx.CurveTo(cx + 2, cy - 18, cx + 10, cy - 10, cx, cy + 2);
        ctx.Fill();
        ctx.SetSourceRGBA(0.25, 0.55, 0.22, 0.9);
        ctx.MoveTo(cx, cy + 2);
        ctx.LineTo(cx, cy - 12);
        ctx.Stroke();
    }

    private void OnFertHint(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        if (Inventory?[1]?.Empty != true)
            return;

        HintBg(ctx, bounds);
        double cx = bounds.InnerWidth / 2;
        double cy = bounds.InnerHeight / 2;
        ctx.SetSourceRGBA(0.55, 0.38, 0.22, 0.9);
        RoundRect(ctx, cx - 11, cy - 8, 22, 18, 3);
        ctx.Fill();
        ctx.SetSourceRGBA(0.7, 0.52, 0.3, 0.95);
        RoundRect(ctx, cx - 8, cy - 14, 16, 8, 3);
        ctx.Fill();
        ctx.SetSourceRGBA(0.9, 0.82, 0.4, 0.95);
        ctx.Arc(cx - 4, cy + 2, 2.2, 0, Math.PI * 2);
        ctx.Fill();
        ctx.Arc(cx + 3, cy + 4, 2.2, 0, Math.PI * 2);
        ctx.Fill();
        ctx.Arc(cx, cy - 1, 2.0, 0, Math.PI * 2);
        ctx.Fill();
    }

    private void OnOutputHint(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        double slot = bounds.InnerHeight;
        for (int i = 0; i < 4; i++)
        {
            if (Inventory?[2 + i]?.Empty != true)
                continue;

            ctx.Save();
            ctx.Translate(i * slot, 0);
            HintBg(ctx, slot, slot);
            double cx = slot / 2;
            double cy = slot / 2;
            ctx.SetSourceRGBA(0.75, 0.78, 0.55, 0.9);
            ctx.MoveTo(cx - 8, cy - 2);
            ctx.LineTo(cx, cy + 8);
            ctx.LineTo(cx + 8, cy - 2);
            ctx.ClosePath();
            ctx.Fill();
            ctx.SetSourceRGBA(0.55, 0.6, 0.4, 0.9);
            ctx.Rectangle(cx - 3, cy - 10, 6, 10);
            ctx.Fill();
            ctx.Restore();
        }
    }

    private static void HintBg(Context ctx, ElementBounds bounds)
        => HintBg(ctx, bounds.InnerWidth, bounds.InnerHeight);

    private static void HintBg(Context ctx, double w, double h)
    {
        ctx.SetSourceRGBA(0, 0, 0, 0.12);
        ctx.Rectangle(4, 4, w - 8, h - 8);
        ctx.Fill();
    }

    private static void RoundRect(Context ctx, double x, double y, double w, double h, double r)
    {
        ctx.NewPath();
        ctx.MoveTo(x + r, y);
        ctx.LineTo(x + w - r, y);
        ctx.Arc(x + w - r, y + r, r, -Math.PI / 2, 0);
        ctx.LineTo(x + w, y + h - r);
        ctx.Arc(x + w - r, y + h - r, r, 0, Math.PI / 2);
        ctx.LineTo(x + r, y + h);
        ctx.Arc(x + r, y + h - r, r, Math.PI / 2, Math.PI);
        ctx.LineTo(x, y + r);
        ctx.Arc(x + r, y + r, r, Math.PI, 3 * Math.PI / 2);
        ctx.ClosePath();
    }

    private void OnStatusBg(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        ctx.SetSourceRGB(0.1, 0.1, 0.14);
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Fill();
        ctx.SetSourceRGB(0.28, 0.28, 0.36);
        ctx.LineWidth = 1;
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Stroke();
    }

    private string StatusText()
    {
        if (_be == null)
            return "";

        int stage = _be.GrowthStage();
        int stages = _be.GrowthStages();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(_be.CropTitle());
        if (stages > 0)
        {
            sb.AppendLine(Lang.Get("ecloche-stage", stage, stages, (int)(_be.Progress * 100f)));
            sb.AppendLine(Lang.Get("ecloche-eta", _be.RemainingLabel()));
        }
        sb.AppendLine(Lang.Get("ecloche-temp", _be.InsideTemp.ToString("0.0"), _be.OptimalTemp.ToString("0.0"), _be.OutsideTemp.ToString("0.0")));
        sb.AppendLine(Lang.Get("ecloche-moist", (_be.Moisture * 100f).ToString("0")));
        sb.AppendLine(Lang.Get("ecloche-npk", _be.NutrientN.ToString("0"), _be.NutrientP.ToString("0"), _be.NutrientK.ToString("0")));
        if (_be.LampOn)
            sb.AppendLine(Lang.Get("ecloche-lamp-on"));
        if (_be.Watering)
            sb.AppendLine(Lang.Get("ecloche-watering"));
        if (_be.Fertilizing)
            sb.AppendLine(Lang.Get("ecloche-fertilizing"));
        return sb.ToString();
    }

    private string AutoLabel()
        => Lang.Get(_auto ? "ecloche-auto-on" : "ecloche-auto-off");

    private bool OnToggleAuto()
    {
        _auto = !_auto;
        capi.Network.SendBlockEntityPacket(BlockEntityPosition, 17001, [_auto ? (byte)1 : (byte)0]);
        SetupDialog();
        return true;
    }

    private void OnWaterDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        _be = capi.World.BlockAccessor.GetBlockEntity(BlockEntityPosition) as BlockEntityECloche;
        float amount = _be?.Litres ?? 0f;
        float cap = _be?.Capacity > 0f ? _be.Capacity : 10f;
        float fill = GameMath.Clamp(amount / cap, 0f, 1f);
        double top = (1.0 - fill) * currentBounds.InnerHeight;

        if (amount > 0)
        {
            ctx.Rectangle(0, top, currentBounds.InnerWidth, currentBounds.InnerHeight - top);
            var liquid = _be.WaterContent();
            var props = liquid != null ? BlockLiquidContainerBase.GetContainableProps(liquid) : null;
            if (props?.Texture != null)
            {
                ctx.Save();
                var matrix = ctx.Matrix;
                matrix.Scale(GuiElement.scaled(3.0), GuiElement.scaled(3.0));
                ctx.Matrix = matrix;
                var loc = props.Texture.Base.Clone().WithPathAppendixOnce(".png");
                GuiElement.fillWithPattern(capi, ctx, loc, true, false, props.Texture.Alpha);
                ctx.Restore();
            }
            else
            {
                ctx.SetSourceRGB(0.2, 0.45, 0.8);
                ctx.Fill();
            }
        }

        ctx.SetSourceRGB(0, 0, 0);
        ctx.LineWidth = 2;
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Stroke();

        ctx.LineWidth = 1;
        for (int i = 1; i < 10; i++)
        {
            double y = currentBounds.InnerHeight / 10.0 * i;
            double w = i % 2 == 0 ? 8 : 5;
            ctx.MoveTo(0, y);
            ctx.LineTo(w, y);
            ctx.Stroke();
            ctx.MoveTo(currentBounds.InnerWidth - w, y);
            ctx.LineTo(currentBounds.InnerWidth, y);
            ctx.Stroke();
        }

        string text = amount > 0 ? amount.ToString("0.0") + "L" : Lang.Get("ecloche-empty");
        ctx.SelectFontFace("sans-serif", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(10);
        var ext = ctx.TextExtents(text);
        double tx = (currentBounds.InnerWidth - ext.Width) / 2;
        double ty = currentBounds.InnerHeight - 4;
        ctx.SetSourceRGB(0, 0, 0);
        ctx.MoveTo(tx, ty);
        ctx.ShowText(text);
        ctx.SetSourceRGB(1, 1, 1);
        ctx.MoveTo(tx, ty - 1);
        ctx.ShowText(text);
    }

    private void SendInvPacket(object packet)
    {
        capi.Network.SendBlockEntityPacket(BlockEntityPosition.X, BlockEntityPosition.Y, BlockEntityPosition.Z, packet);
    }

    private void OnTitleBarClose() => TryClose();
}
