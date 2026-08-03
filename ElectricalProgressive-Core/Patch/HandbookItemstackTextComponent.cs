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
        cx = 0;
        cy = 0;

        var lines = BoundsPerLine;
        if (lines is not { Length: > 0 } || lines[0] == null)
            return false;

        var line = lines[0];
        var size = GuiElement.scaled(_unscaledSize);

        if (line.Width >= 1 && line.Height >= 1)
        {
            cx = renderX + line.X + line.Width * 0.5 + offX;
            cy = renderY + line.Y + line.Height * 0.5 + offY;
        }
        else
        {
            cx = renderX + line.X + size * 0.5 + offX;
            cy = renderY + line.Y + size * 0.5 + offY;
        }

        return true;
    }
}
