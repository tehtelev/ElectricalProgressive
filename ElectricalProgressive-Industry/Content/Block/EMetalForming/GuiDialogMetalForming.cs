using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EMetalForming;

public class GuiDialogMetalForming : GuiDialogBlockEntity
{
    private long lastRedrawMs;
    private float _recipeprogress;
    private string _recipeName = "";
    private BlockEntityEMetalForming? _be;
    private readonly BlockPos _blockEntityPos;
    private readonly ICoreClientAPI _capi;

    protected override double FloatyDialogPosition => 0.75;

    public GuiDialogMetalForming(
        string DialogTitle,
        InventoryBase Inventory,
        BlockPos BlockEntityPosition,
        ICoreClientAPI capi)
        : base(DialogTitle, Inventory, BlockEntityPosition, capi)
    {
        if (IsDuplicate)
            return;

        _capi = capi;
        _blockEntityPos = BlockEntityPosition;

        var be = capi.World.BlockAccessor.GetBlockEntity(_blockEntityPos) as BlockEntityEMetalForming;
        if (be != null)
        {
            _be = be;
            _recipeprogress = be.RecipeProgress;
            _recipeName = be.CurrentRecipeName ?? "";
        }

        capi.World.Player.InventoryManager.OpenInventory(Inventory);
        SetupDialog();
    }

    public void OnInventorySlotModified(int slotid)
    {
        capi.Event.EnqueueMainThreadTask(SetupDialog, "setupmetalformingdlg");
    }

    private void SetupDialog()
    {
        var itemSlot = capi.World.Player.InventoryManager.CurrentHoveredSlot;
        if (itemSlot != null && itemSlot.Inventory == Inventory)
            capi.Input.TriggerOnMouseLeaveSlot(itemSlot);
        else
            itemSlot = null;

        var be = capi.World.BlockAccessor.GetBlockEntity(_blockEntityPos) as BlockEntityEMetalForming;
        if (be != null)
        {
            _be = be;
            _recipeName = be.CurrentRecipeName ?? "";
        }

        var bounds1 = ElementBounds.Fixed(0.0, 0.0, 280.0, 175.0);
        var inputBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 10.0, 55.0, 1, 1);
        var outputBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 230.0, 30.0, 1, 2);
        var progressBounds = ElementBounds.Fixed(65, 67, 155, 25);
        var buttonBounds = ElementBounds.Fixed(10, 135, 160, 26);
        var recipeTextBounds = ElementBounds.Fixed(10, 112, 260, 20);

        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        bgBounds.WithChildren(bounds1);

        var dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0.0);

        var recipeLabel = string.IsNullOrEmpty(_recipeName)
            ? Lang.Get("electricalprogressiveindustry:emetalforming-no-selection")
            : _recipeName;

        ClearComposers();
        SingleComposer = capi.Gui
            .CreateCompo("blockentitymetalforming" + BlockEntityPosition, dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar(DialogTitle, OnTitleBarClose)
            .BeginChildElements(bgBounds)
            .AddDynamicCustomDraw(progressBounds, OnProgressDraw, "progressDrawer")
            .AddItemSlotGrid(Inventory, SendInvPacket, 1, [0], inputBounds, "inputSlot")
            .AddItemSlotGrid(Inventory, SendInvPacket, 1, [1, 2], outputBounds, "outputslot")
            .AddStaticText(Lang.Get("electricalprogressive:input"), CairoFont.WhiteDetailText(),
                ElementBounds.Fixed(12, 105, 50, 18))
            .AddStaticText(Lang.Get("electricalprogressive:output"), CairoFont.WhiteDetailText(),
                ElementBounds.Fixed(228, 135, 50, 18))
            .AddDynamicText(recipeLabel, CairoFont.WhiteDetailText(), recipeTextBounds, "recipename")
            .AddSmallButton(Lang.Get("electricalprogressiveindustry:emetalforming-select"),
                OnSelectRecipe, buttonBounds)
            .EndChildElements()
            .Compose();

        lastRedrawMs = capi.ElapsedMilliseconds;

        if (itemSlot == null)
            return;
        SingleComposer.OnMouseMove(new MouseEvent(capi.Input.MouseX, capi.Input.MouseY));
    }

    private bool OnSelectRecipe()
    {
        _be?.OpenRecipeSelector();
        return true;
    }

    public void Update(float recipeProgress, string? recipeName = null)
    {
        _recipeprogress = Math.Min(Math.Max(recipeProgress, 0f), 1f);
        if (recipeName != null)
            _recipeName = recipeName;

        if (!IsOpened())
            return;

        if (capi.ElapsedMilliseconds - lastRedrawMs <= 50L)
            return;

        SingleComposer?.GetCustomDraw("progressDrawer")?.Redraw();
        var label = string.IsNullOrEmpty(_recipeName)
            ? Lang.Get("electricalprogressiveindustry:emetalforming-no-selection")
            : _recipeName;
        SingleComposer?.GetDynamicText("recipename")?.SetNewText(label);
        lastRedrawMs = capi.ElapsedMilliseconds;
    }

    private void OnProgressDraw(Context ctx, ImageSurface surface, ElementBounds currentBounds)
    {
        float progress = Math.Min(Math.Max(_recipeprogress, 0f), 1f);
        double fillWidth = currentBounds.InnerWidth * progress;

        ctx.SetSourceRGB(0, 0, 0);
        ctx.LineWidth = 3;
        ctx.Rectangle(0, 0, currentBounds.InnerWidth, currentBounds.InnerHeight);
        ctx.Stroke();

        if (progress > 0)
        {
            using var gradient = new LinearGradient(0, 0, fillWidth, 0);
            gradient.AddColorStop(0.0, new Color(0.0, 0.6, 0.0, 1.0));
            gradient.AddColorStop(0.5, new Color(0.8, 0.8, 0.0, 1.0));
            gradient.AddColorStop(1.0, new Color(0.8, 0.0, 0.0, 1.0));
            ctx.SetSource(gradient);
            ctx.Rectangle(2, 2, fillWidth - 4, currentBounds.InnerHeight - 4);
            ctx.Fill();
        }

        ctx.SetSourceRGB(0, 0, 0);
        ctx.LineWidth = 1;
        double totalWidth = currentBounds.InnerWidth;
        const int divisions = 10;
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

        int percent = (int)(progress * 100);
        string percentText = $"{percent}%";
        ctx.Save();
        ctx.SelectFontFace("sans-serif", FontSlant.Normal, FontWeight.Bold);
        double fontSize = currentBounds.InnerHeight * 0.65;
        ctx.SetFontSize(fontSize);
        var textExtents = ctx.TextExtents(percentText);
        if (textExtents.Width > currentBounds.InnerWidth * 0.9)
        {
            fontSize *= currentBounds.InnerWidth * 0.9 / textExtents.Width;
            ctx.SetFontSize(fontSize);
            textExtents = ctx.TextExtents(percentText);
        }

        ctx.SetSourceRGB(1.0, 1.0, 1.0);
        ctx.MoveTo((currentBounds.InnerWidth - textExtents.Width) / 2,
            (currentBounds.InnerHeight + textExtents.Height) / 2);
        ctx.ShowText(percentText);
        ctx.Restore();
    }

    private void SendInvPacket(object p)
    {
        capi.Network.SendBlockEntityPacket(BlockEntityPosition.X, BlockEntityPosition.Y,
            BlockEntityPosition.Z, p);
    }

    private void OnTitleBarClose() => TryClose();

    public override void OnGuiOpened()
    {
        base.OnGuiOpened();
        Inventory.SlotModified += OnInventorySlotModified;
        var be = _capi.World.BlockAccessor.GetBlockEntity(_blockEntityPos) as BlockEntityEMetalForming;
        if (be != null)
        {
            _recipeprogress = be.RecipeProgress;
            _recipeName = be.CurrentRecipeName ?? "";
            _be = be;
        }
    }

    public override void OnGuiClosed()
    {
        Inventory.SlotModified -= OnInventorySlotModified;
        SingleComposer?.GetSlotGrid("inputSlot")?.OnGuiClosed(capi);
        SingleComposer?.GetSlotGrid("outputslot")?.OnGuiClosed(capi);
        base.OnGuiClosed();
    }
}
