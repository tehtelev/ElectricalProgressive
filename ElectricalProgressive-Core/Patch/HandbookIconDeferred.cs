using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using HarmonyLib;

namespace ElectricalProgressive.Patch;

/// <summary>
/// Как в Questbook: 3D-иконки не рисуются внутри richtext RenderInteractiveElements,
/// а батчатся и рисуются в конце OnRenderGUI handbook — после 2D-панели.
/// Перекрытие: только Focused не-handbook диалог, если накрывает центр иконки.
/// </summary>
public static class HandbookIconDeferred
{
    private readonly struct Request
    {
        public readonly HandbookItemstackTextComponent Component;
        public readonly float DeltaTime;
        public readonly double RenderX;
        public readonly double RenderY;
        public readonly double RenderZ;

        public Request(HandbookItemstackTextComponent component, float deltaTime, double renderX, double renderY,
            double renderZ)
        {
            Component = component;
            DeltaTime = deltaTime;
            RenderX = renderX;
            RenderY = renderY;
            RenderZ = renderZ;
        }
    }

    private static readonly List<Request> Queue = new(32);
    private static bool _patched;
    private static ICoreClientAPI? _capi;

    public static void EnsurePatched(ICoreClientAPI capi)
    {
        _capi = capi;
        if (_patched)
            return;

        var method = AccessTools.Method(typeof(GuiDialogHandbook), nameof(GuiDialogHandbook.OnRenderGUI));
        if (method == null)
        {
            capi.Logger.Error("[MachineConstruct] GuiDialogHandbook.OnRenderGUI not found");
            return;
        }

        var harmony = new Harmony("electricalprogressive.handbook.icons.deferred");
        harmony.Patch(method, postfix: new HarmonyMethod(typeof(HandbookIconDeferred), nameof(OnHandbookRendered)));
        _patched = true;
        capi.Logger.Notification("[MachineConstruct] Handbook deferred icons (questbook-style) enabled");
    }

    public static void Enqueue(HandbookItemstackTextComponent component, float deltaTime, double renderX,
        double renderY, double renderZ)
    {
        Queue.Add(new Request(component, deltaTime, renderX, renderY, renderZ));
    }

    /// <summary>После 2D handbook — рисуем 3D-иконки (как QuestbookDialog.OnRenderGUI).</summary>
    public static void OnHandbookRendered(GuiDialogHandbook __instance, float deltaTime)
    {
        if (Queue.Count == 0)
            return;

        var capi = _capi;
        if (capi == null)
        {
            Queue.Clear();
            return;
        }

        try
        {
            foreach (var req in Queue)
            {
                if (req.Component == null)
                    continue;

                if (IsOccludedByFocusedOverlay(capi, req))
                    continue;

                // Реальный рендер 3D (base ItemstackTextComponent)
                req.Component.RenderNow(req.DeltaTime, req.RenderX, req.RenderY, req.RenderZ);
            }
        }
        finally
        {
            Queue.Clear();
        }
    }

    /// <summary>
    /// Как questbook skip under modal: не рисуем, только если Focused GUI
    /// (не handbook) реально накрывает центр иконки. Сбоку — рисуем.
    /// </summary>
    private static bool IsOccludedByFocusedOverlay(ICoreClientAPI capi, Request req)
    {
        if (!req.Component.TryGetScreenCenter(req.RenderX, req.RenderY, out var cx, out var cy))
            return false;

        GuiDialog? focused = null;
        foreach (var dlg in capi.Gui.OpenedGuis)
        {
            if (dlg != null && dlg.IsOpened() && dlg.Focused)
            {
                focused = dlg;
                break;
            }
        }

        // Handbook в фокусе или никто — не перекрыто
        if (focused == null || IsHandbookDialog(focused))
            return false;

        return DialogContainsScreenPoint(focused, cx, cy);
    }

    private static bool DialogContainsScreenPoint(GuiDialog dlg, double sx, double sy)
    {
        try
        {
            if (ComposerContainsPoint(dlg.SingleComposer, sx, sy))
                return true;

            var composers = dlg.Composers;
            if (composers?.Values == null)
                return false;

            foreach (var composer in composers.Values)
            {
                if (ComposerContainsPoint(composer, sx, sy))
                    return true;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static bool ComposerContainsPoint(GuiComposer? composer, double sx, double sy)
    {
        var b = composer?.Bounds;
        if (b == null || b.OuterWidth <= 2 || b.OuterHeight <= 2)
            return false;

        try
        {
            // PointInside использует absX/absY (экранные)
            return b.PointInside(sx, sy);
        }
        catch
        {
            return sx >= b.absX && sx <= b.absX + b.OuterWidth
                   && sy >= b.absY && sy <= b.absY + b.OuterHeight;
        }
    }

    private static bool IsHandbookDialog(GuiDialog dlg) =>
        dlg is GuiDialogHandbook
        || dlg.GetType().Name.Contains("Handbook", System.StringComparison.OrdinalIgnoreCase);
}
