using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ElectricalProgressive.Patch;

/// <summary>
/// Как в Questbook: в RenderInteractiveElements только ставим в очередь;
/// 3D рисуется в конце GuiDialogHandbook.OnRenderGUI (после 2D-панели).
/// </summary>
public class HandbookItemstackTextComponent : ItemstackTextComponent
{
    private readonly double _unscaledSize;

    public HandbookItemstackTextComponent(
        ICoreClientAPI capi,
        ItemStack itemstack,
        double size,
        double rightSidePadding,
        EnumFloat floatType,
        Action<ItemStack>? onStackClicked = null)
        : base(capi, itemstack, size, rightSidePadding, floatType, onStackClicked)
    {
        _unscaledSize = size;
        HandbookIconDeferred.EnsurePatched(capi);
    }

    /// <summary>
    /// Не рисуем 3D здесь (иначе слой/depth ломается с креативом).
    /// Батчим — Questbook-style deferred.
    /// </summary>
    public override void RenderInteractiveElements(float deltaTime, double renderX, double renderY, double renderZ)
    {
        HandbookIconDeferred.Enqueue(this, deltaTime, renderX, renderY, renderZ);
    }

    /// <summary>Вызывается из deferred flush после OnRenderGUI handbook.</summary>
    public void RenderNow(float deltaTime, double renderX, double renderY, double renderZ)
    {
        base.RenderInteractiveElements(deltaTime, renderX, renderY, renderZ);
    }

    public bool TryGetScreenCenter(double renderX, double renderY, out double cx, out double cy)
    {
        if (!TryGetScreenRect(renderX, renderY, out var x, out var y, out var w, out var h))
        {
            cx = 0;
            cy = 0;
            return false;
        }

        cx = x + w * 0.5;
        cy = y + h * 0.5;
        return true;
    }

    /// <summary>Экранный rect иконки (для scissor / visibility).</summary>
    public bool TryGetScreenRect(double renderX, double renderY, out double x, out double y, out double w, out double h)
    {
        x = y = w = h = 0;

        var lines = BoundsPerLine;
        if (lines is not { Length: > 0 } || lines[0] == null)
            return false;

        var line = lines[0];
        var size = GuiElement.scaled(_unscaledSize);

        if (line.Width >= 1 && line.Height >= 1)
        {
            x = renderX + line.X + offX;
            y = renderY + line.Y + offY;
            w = line.Width;
            h = line.Height;
        }
        else
        {
            x = renderX + line.X + offX;
            y = renderY + line.Y + offY;
            w = size;
            h = size;
        }

        return true;
    }
}
