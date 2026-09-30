using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ElectricalProgressive.Content.Storage;

public class GuiDialogEStorageOrder : GuiDialog
{
    private readonly Action<int> _order;
    private readonly string _title;
    private int _count = 1;

    public GuiDialogEStorageOrder(ICoreClientAPI capi, string title, Action<int> order)
        : base(capi)
    {
        _order = order;
        _title = title;
        Setup();
    }

    public override string ToggleKeyCombinationCode => null!;

    public override bool PrefersUngrabbedMouse => true;

    public override double DrawOrder => 0.9;

    public override bool CaptureAllInputs() => IsOpened();

    private void Setup()
    {
        var minus = ElementBounds.Fixed(0, 0, 36, 30);
        var field = ElementBounds.Fixed(44, 0, 140, 30);
        var plus = ElementBounds.Fixed(192, 0, 36, 30);
        var ok = ElementBounds.Fixed(0, 40, 128, 28);
        var cancel = ElementBounds.Fixed(136, 40, 92, 28);
        var hint = ElementBounds.Fixed(0, 76, 228, 40);
        var bg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bg.BothSizing = ElementSizing.FitToChildren;
        bg.WithChildren(minus, field, plus, ok, cancel, hint);

        SingleComposer = capi.Gui
            .CreateCompo("estorageorder", ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle))
            .AddShadedDialogBG(bg)
            .AddDialogTitleBar(_title, () => TryClose())
            .BeginChildElements(bg)
            .AddSmallButton("−", () => Bump(-1), minus, EnumButtonStyle.Normal, "minus")
            .AddTextInput(field, OnQty, CairoFont.WhiteDetailText(), "qty")
            .AddSmallButton("+", () => Bump(1), plus, EnumButtonStyle.Normal, "plus")
            .AddSmallButton(Lang.Get("electricalprogressivestorage:estorage-order-ok"), Confirm, ok, EnumButtonStyle.Normal, "ok")
            .AddSmallButton(Lang.Get("electricalprogressivestorage:estorage-order-cancel"), () => TryClose(), cancel, EnumButtonStyle.Normal, "cancel")
            .AddStaticText(Lang.Get("electricalprogressivestorage:estorage-order-hint"), CairoFont.WhiteDetailText(), hint)
            .EndChildElements()
            .Compose();

        SingleComposer.GetTextInput("qty").SetValue("1", false);
    }

    private void OnQty(string text)
    {
        if (int.TryParse(text.Trim(), out var count))
            _count = Math.Clamp(count, 1, 100000);
    }

    private bool Bump(int delta)
    {
        ReadField();
        _count = Math.Clamp(_count + delta, 1, 100000);
        SingleComposer?.GetTextInput("qty")?.SetValue(_count.ToString(), false);
        return true;
    }

    private bool Confirm()
    {
        ReadField();
        if (_count > 0)
            _order(_count);
        TryClose();
        return true;
    }

    private void ReadField()
    {
        var text = SingleComposer?.GetTextInput("qty")?.GetText() ?? "";
        if (int.TryParse(text.Trim(), out var count))
            _count = Math.Clamp(count, 1, 100000);
    }
}
