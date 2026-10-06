using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage;

public sealed class PlanRow
{
    public ItemStack Stack = null!;
    public int Count;
    public int Kind;
}

/// <summary>
/// Подложка ячеек плана. Иконки рисуются в том же проходе, что слоты интерфейса.
/// </summary>
public sealed class GuiElementPlanGrid : GuiElement
{
    public const double CellW = 176;
    public const double Gap = 4;
    public const int Cols = 3;
    public const int VisRows = 5;

    private readonly List<DummySlot> _slots = new();

    public GuiElementPlanGrid(ICoreClientAPI capi, ElementBounds bounds, IReadOnlyList<PlanRow> rows)
        : base(capi, bounds)
    {
        foreach (var row in rows)
        {
            var copy = row.Stack.Clone();
            copy.StackSize = 1;
            _slots.Add(new DummySlot(copy));
        }
    }

    public static double CellH => GuiElementPassiveItemSlot.unscaledSlotSize;

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        var scale = RuntimeEnv.GUIScale;
        var cellW = CellW * scale;
        var cellH = CellH * scale;
        var gap = Gap * scale;
        for (var i = 0; i < _slots.Count; i++)
        {
            var x = Bounds.drawX + (i % Cols) * (cellW + gap);
            var y = Bounds.drawY + (i / Cols) * (cellH + gap);
            TermChrome.Inset(ctx, x, y, cellW, cellH, 0.09, 0.075, 0.06);
        }
    }

    public override void RenderInteractiveElements(float deltaTime)
    {
        if (_slots.Count == 0)
            return;

        Bounds.CalcWorldBounds();
        var scale = RuntimeEnv.GUIScale;
        var strideX = (CellW + Gap) * scale;
        var strideY = (CellH + Gap) * scale;
        var icon = (float)scaled(GuiElementPassiveItemSlot.unscaledItemSize);
        var half = scaled(CellH) * 0.5;
        for (var i = 0; i < _slots.Count; i++)
        {
            var cx = Bounds.renderX + (i % Cols) * strideX + CellW * scale - half;
            var cy = Bounds.renderY + (i / Cols) * strideY + half;
            // 90, как у слота инвентаря: выше подложки (50) и внутри слоя диалога (ZSize 150).
            api.Render.RenderItemstackToGui(
                _slots[i],
                cx,
                cy,
                90,
                icon,
                ColorUtil.WhiteArgb,
                true,
                false,
                false);
        }
    }
}

public sealed class GuiElementSolid : GuiElement
{
    private readonly double _r;
    private readonly double _g;
    private readonly double _b;

    public GuiElementSolid(ICoreClientAPI capi, ElementBounds bounds, double r, double g, double b)
        : base(capi, bounds)
    {
        _r = r;
        _g = g;
        _b = b;
    }

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        TermChrome.Inset(ctx, Bounds.drawX, Bounds.drawY, Bounds.OuterWidth, Bounds.OuterHeight, _r, _g, _b);
    }
}
