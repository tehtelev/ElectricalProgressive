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
    public const double CellW = 156;
    public const double CellH = 48;
    public const double Gap = 0;
    public const int Cols = 3;
    public const int VisRows = 6;

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

    public override void ComposeElements(Context ctx, ImageSurface surface)
    {
        Bounds.CalcWorldBounds();
        var cellW = scaled(CellW);
        var cellH = scaled(CellH);
        var edge = Math.Max(1, scaled(1));
        var prev = ctx.Antialias;
        ctx.Antialias = Antialias.None;
        for (var i = 0; i < Cols * VisRows; i++)
        {
            var x = Bounds.drawX + (i % Cols) * cellW;
            var y = Bounds.drawY + (i / Cols) * cellH;
            ctx.SetSourceRGB(Ae2Gui.Well[0], Ae2Gui.Well[1], Ae2Gui.Well[2]);
            ctx.Rectangle(x, y, cellW, cellH);
            ctx.Fill();
            ctx.SetSourceRGBA(Ae2Gui.Ink[0], Ae2Gui.Ink[1], Ae2Gui.Ink[2], 0.55);
            ctx.Rectangle(x, y, cellW, edge);
            ctx.Rectangle(x, y + cellH - edge, cellW, edge);
            ctx.Rectangle(x, y, edge, cellH);
            ctx.Rectangle(x + cellW - edge, y, edge, cellH);
            ctx.Fill();
        }

        ctx.Antialias = prev;
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
        var halfY = scaled(CellH) * 0.5;
        for (var i = 0; i < _slots.Count; i++)
        {
            var cx = Bounds.renderX + (i % Cols) * strideX + scaled(CellW - 8) - icon * 0.5;
            var cy = Bounds.renderY + (i / Cols) * strideY + halfY;
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
