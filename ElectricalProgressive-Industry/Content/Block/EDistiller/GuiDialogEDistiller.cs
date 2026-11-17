using Cairo;
using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EDistiller;

public class GuiDialogEDistiller : GuiDialogBlockEntity
{
    private readonly ICoreClientAPI _capi;
    private readonly BlockEntityEDistiller _be;
    private long _lastRedrawMs;
    private long _lastTextMs;

    public GuiDialogEDistiller(
        string dialogTitle,
        InventoryBase inventory,
        BlockPos pos,
        ICoreClientAPI capi,
        BlockEntityEDistiller be)
        : base(dialogTitle, inventory, pos, capi)
    {
        if (IsDuplicate)
            return;

        _capi = capi;
        _be = be;
        capi.World.Player.InventoryManager.OpenInventory(inventory);
        SetupDialog();
    }

    private static string T(string key) => Lang.Get("electricalprogressiveindustry:" + key);
    private static string T(string key, params object[] args) => Lang.Get("electricalprogressiveindustry:" + key, args);

    private void OnTitleBarClose() => TryClose();

    private void SetupDialog()
    {
        var top = GuiStyle.TitleBarHeight + 4;
        var mashBounds = ElementBounds.Fixed(8, top, 44, 150);
        var waterBounds = ElementBounds.Fixed(64, top, 44, 150);
        var productBounds = ElementBounds.Fixed(120, top, 44, 150);
        var infoBg = ElementBounds.Fixed(180, top, 250, 174);
        var infoText = ElementBounds.Fixed(186, top + 4, 238, 166);

        var bg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bg.BothSizing = ElementSizing.FitToChildren;
        bg.WithChildren(ElementBounds.Fixed(0, 0, 440, 210));

        var dialog = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

        var font = CairoFont.WhiteDetailText();

        ClearComposers();
        SingleComposer = _capi.Gui
            .CreateCompo("blockentityedistiller" + BlockEntityPosition, dialog)
            .AddShadedDialogBG(bg)
            .AddDialogTitleBar(DialogTitle, OnTitleBarClose)
            .BeginChildElements(bg)
            .AddInset(mashBounds.ForkBoundingParent(2, 2, 2, 2), 2)
            .AddDynamicCustomDraw(mashBounds, (ctx, s, b) => DrawTank(ctx, b, _be.MashSlot, _be.MashLitres, _be.MashCapacity), "mashDrawer")
            .AddStaticText(T("edistiller-mash"), font, ElementBounds.Fixed(4, top + 154, 52, 16))
            .AddInset(waterBounds.ForkBoundingParent(2, 2, 2, 2), 2)
            .AddDynamicCustomDraw(waterBounds, (ctx, s, b) => DrawTank(ctx, b, _be.WaterSlot, _be.WaterLitres, _be.WaterCapacity), "waterDrawer")
            .AddStaticText(T("edistiller-water"), font, ElementBounds.Fixed(56, top + 154, 60, 16))
            .AddInset(productBounds.ForkBoundingParent(2, 2, 2, 2), 2)
            .AddDynamicCustomDraw(productBounds, (ctx, s, b) => DrawTank(ctx, b, _be.ProductSlot, _be.ProductLitres, _be.ProductCapacity), "productDrawer")
            .AddStaticText(T("edistiller-product"), font, ElementBounds.Fixed(112, top + 154, 64, 16))
            .AddDynamicCustomDraw(infoBg, OnInfoBackground, "infoBg")
            .AddDynamicText(_be.GetGuiText(), font, infoText, "statusText")
            .EndChildElements()
            .Compose();

        _lastRedrawMs = _capi.ElapsedMilliseconds;
    }

    public void Update()
    {
        if (!IsOpened() || SingleComposer == null)
            return;

        var now = _capi.ElapsedMilliseconds;
        if (now - _lastTextMs > 250)
        {
            SingleComposer.GetDynamicText("statusText")?.SetNewText(_be.GetGuiText());
            _lastTextMs = now;
        }

        if (now - _lastRedrawMs > 400)
        {
            SingleComposer.GetCustomDraw("mashDrawer")?.Redraw();
            SingleComposer.GetCustomDraw("waterDrawer")?.Redraw();
            SingleComposer.GetCustomDraw("productDrawer")?.Redraw();
            _lastRedrawMs = now;
        }
    }

    private static void OnInfoBackground(Context ctx, ImageSurface surface, ElementBounds bounds)
    {
        ctx.SetSourceRGB(0.1, 0.1, 0.15);
        ctx.Rectangle(0, 0, bounds.InnerWidth, bounds.InnerHeight);
        ctx.Fill();
        ctx.SetSourceRGB(0.3, 0.3, 0.4);
        ctx.LineWidth = 1;
        ctx.Rectangle(0, 0, bounds.InnerWidth, bounds.InnerHeight);
        ctx.Stroke();
    }

    private void DrawTank(Context ctx, ElementBounds bounds, ItemSlot slot, float litres, float capacity)
    {
        var rel = capacity <= 0 ? 0 : GameMath.Clamp(litres / capacity, 0f, 1f);
        var top = (1.0 - rel) * bounds.InnerHeight;

        if (litres > 0)
        {
            ctx.Rectangle(0, top, bounds.InnerWidth, bounds.InnerHeight - top);
            var stack = slot?.Itemstack;
            var props = stack != null ? BlockLiquidContainerBase.GetContainableProps(stack) : null;
            if (props?.Texture != null)
            {
                ctx.Save();
                var m = ctx.Matrix;
                m.Scale(GuiElement.scaled(3.0), GuiElement.scaled(3.0));
                ctx.Matrix = m;
                var tex = props.Texture.Base.Clone().WithPathAppendixOnce(".png");
                GuiElement.fillWithPattern(_capi, ctx, tex, true, false, props.Texture.Alpha);
                ctx.Restore();
            }
            else
            {
                ctx.SetSourceRGB(0.25, 0.45, 0.8);
                ctx.Fill();
            }
        }

        ctx.SetSourceRGB(0, 0, 0);
        ctx.LineWidth = 2;
        ctx.Rectangle(0, 0, bounds.InnerWidth, bounds.InnerHeight);
        ctx.Stroke();

        ctx.SelectFontFace("sans-serif", FontSlant.Normal, FontWeight.Bold);
        ctx.SetFontSize(9);
        var label = litres <= 0 ? T("edistiller-empty") : T("edistiller-litres-short", litres.ToString("0.#"));
        var ext = ctx.TextExtents(label);
        ctx.SetSourceRGB(1, 1, 1);
        ctx.MoveTo(Math.Max(1, (bounds.InnerWidth - ext.Width) / 2), bounds.InnerHeight - 6);
        ctx.ShowText(label);
    }
}
