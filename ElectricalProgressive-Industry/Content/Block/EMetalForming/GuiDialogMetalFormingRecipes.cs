using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EMetalForming;

/// <summary>
/// Выбор изделия. Иконка всегда оловянная, название без металла.
/// </summary>
public class GuiDialogMetalFormingRecipes : GuiDialogGeneric
{
    private readonly BlockPos _pos;
    private readonly List<SkillItem> _items;
    private readonly Action<int> _onSelected;
    private int _prevOver = -1;
    public GuiDialogMetalFormingRecipes(
        string title,
        List<IReadOnlyList<SmithingRecipe>> groups,
        Action<int> onSelected,
        BlockPos pos,
        ICoreClientAPI capi)
        : base(title, capi)
    {
        _pos = pos;
        _onSelected = onSelected;
        _items = new List<SkillItem>(groups.Count);

        var slot = GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGridBase.unscaledSlotPadding;
        foreach (var group in groups)
        {
            var output = TinVariant(group) ?? group.FirstOrDefault()?.Output?.ResolvedItemstack;
            if (output?.Collectible == null)
                continue;

            var stack = output.Clone();
            stack.StackSize = 1;
            var dummy = new DummySlot(stack);
            _items.Add(new SkillItem
            {
                Code = stack.Collectible.Code.Clone(),
                Name = BlockEntityEMetalForming.GenericCollectibleName(group.Select(r => r.Output?.ResolvedItemstack)),
                Description = "",
                RenderHandler = (_, _, x, y) =>
                {
                    var box = GuiElement.scaled(slot - 5.0);
                    capi.Render.RenderItemstackToGui(
                        dummy,
                        x + box / 2.0,
                        y + box / 2.0,
                        100,
                        (float)GuiElement.scaled(GuiElementPassiveItemSlot.unscaledItemSize),
                        ColorUtil.WhiteArgb,
                        shading: true,
                        rotate: false,
                        showStackSize: false);
                }
            });
        }

        SetupDialog();
    }

    public void SetIngredientCounts(int index, int count, string materialName)
    {
        if (index < 0 || index >= _items.Count || string.IsNullOrWhiteSpace(materialName))
            return;

        _items[index].Data = Lang.Get("recipeselector-requiredcount", count, materialName.ToLowerInvariant());
    }

    private void SetupDialog()
    {
        var count = Math.Max(1, _items.Count);
        var cols = Math.Min(count, 7);
        var rows = (int)Math.Ceiling(count / (float)cols);
        var cell = GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGridBase.unscaledSlotPadding;
        var width = Math.Max(300.0, cols * cell);

        var grid = ElementBounds.Fixed(0, 30, width, rows * cell);
        var name = ElementBounds.Fixed(0, rows * cell + 50, width, 33);
        var desc = name.BelowCopy(0, 10);
        var bg = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bg.BothSizing = ElementSizing.FitToChildren;

        SingleComposer = capi.Gui
            .CreateCompo("metalforming-recipes" + _pos, ElementStdBounds.AutosizedMainDialog)
            .AddShadedDialogBG(bg)
            .AddDialogTitleBar(DialogTitle, () => TryClose())
            .BeginChildElements(bg)
            .AddSkillItemGrid(_items, cols, rows, OnSlotClick, grid, "skillitemgrid")
            .AddDynamicText("", CairoFont.WhiteSmallishText(), name, "name")
            .AddDynamicText("", CairoFont.WhiteDetailText(), desc, "desc")
            .AddDynamicText("", CairoFont.WhiteDetailText(), desc.BelowCopy(0, 20), "ingredient")
            .EndChildElements()
            .Compose();

        SingleComposer.GetSkillItemGrid("skillitemgrid").OnSlotOver = OnSlotOver;
    }

    private static ItemStack? TinVariant(IReadOnlyList<SmithingRecipe> group)
    {
        foreach (var recipe in group)
        {
            var stack = recipe.Output?.ResolvedItemstack;
            if (stack?.Collectible?.LastCodePart() == "tin")
                return stack;
        }

        return null;
    }

    private void OnSlotOver(int index)
    {
        if (index < 0 || index >= _items.Count || index == _prevOver)
            return;

        _prevOver = index;
        SingleComposer.GetDynamicText("name").SetNewText(_items[index].Name);
        SingleComposer.GetDynamicText("desc").SetNewText(_items[index].Description ?? "");

        var text = _items[index].Data as string ?? "";
        SingleComposer.GetDynamicText("ingredient").SetNewText(text);
    }

    private void OnSlotClick(int index)
    {
        _onSelected(index);
        TryClose();
    }

    public override void OnRenderGUI(float deltaTime)
    {
        if (capi.Settings.Bool["immersiveMouseMode"])
        {
            var projected = MatrixToolsd.Project(
                new Vec3d(_pos.X + 0.5, _pos.Y + 0.5, _pos.Z + 0.5),
                capi.Render.PerspectiveProjectionMat,
                capi.Render.PerspectiveViewMat,
                capi.Render.FrameWidth,
                capi.Render.FrameHeight);
            if (projected.Z < 0)
                return;

            SingleComposer.Bounds.Alignment = EnumDialogArea.None;
            SingleComposer.Bounds.fixedOffsetX = 0;
            SingleComposer.Bounds.fixedOffsetY = 0;
            SingleComposer.Bounds.absFixedX = projected.X - SingleComposer.Bounds.OuterWidth / 2.0;
            SingleComposer.Bounds.absFixedY = capi.Render.FrameHeight - projected.Y - SingleComposer.Bounds.OuterHeight * 0.75;
            SingleComposer.Bounds.absMarginX = 0;
            SingleComposer.Bounds.absMarginY = 0;
        }

        base.OnRenderGUI(deltaTime);
    }
}
