using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Patch;

/// <summary>
/// Как в Questbook: 3D-иконки не рисуются внутри richtext RenderInteractiveElements,
/// а батчатся и рисуются в конце OnRenderGUI handbook — после 2D-панели.
/// Обязательно scissor по clip-viewport handbook (BeginClip), иначе иконки
/// уезжают за пределы окна при скролле.
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

    private static FieldInfo? _interactiveDrawOrderField;
    private static FieldInfo? _clipFlagField;
    private static Type? _clipType;

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

    /// <summary>После 2D handbook — 3D-иконки внутри scissor clip-области.</summary>
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

        var clip = TryGetHandbookClipBounds(__instance);
        var scissorPushed = false;

        try
        {
            if (clip != null)
            {
                clip.CalcWorldBounds();
                // stacking:false — отдельный viewport; иконки ItemstackTextComponent
                // внутри сами PushScissor(stacking:true) → пересечение с clip
                capi.Render.PushScissor(clip, stacking: false);
                scissorPushed = true;
            }

            foreach (var req in Queue)
            {
                if (req.Component == null)
                    continue;

                if (IsOccludedByFocusedOverlay(capi, req))
                    continue;

                // За пределами viewport (скролл) — не рисуем и не тратим GPU
                if (clip != null && !IsIconVisibleInClip(req, clip))
                    continue;

                req.Component.RenderNow(req.DeltaTime, req.RenderX, req.RenderY, req.RenderZ);
            }
        }
        finally
        {
            if (scissorPushed)
            {
                try
                {
                    capi.Render.PopScissor();
                }
                catch
                {
                    // ignore mismatched stack
                }
            }

            Queue.Clear();
        }
    }

    /// <summary>
    /// Bounds первого GuiElementClip(clip:true) активного composer —
    /// тот же viewport, что BeginClip у detail/overview handbook.
    /// </summary>
    private static ElementBounds? TryGetHandbookClipBounds(GuiDialogHandbook handbook)
    {
        try
        {
            var composer = handbook.SingleComposer;
            if (composer == null)
                return null;

            _interactiveDrawOrderField ??= AccessTools.Field(typeof(GuiComposer), "interactiveElementsInDrawOrder");
            var list = _interactiveDrawOrderField?.GetValue(composer) as IList;
            if (list == null || list.Count == 0)
                return null;

            foreach (var item in list)
            {
                if (item is not GuiElement el)
                    continue;

                var t = el.GetType();
                if (!IsClipElementType(t))
                    continue;

                _clipFlagField ??= AccessTools.Field(t, "clip");
                // clip:true = BeginClip, clip:false = EndClip
                if (_clipFlagField == null || _clipFlagField.DeclaringType != t)
                    _clipFlagField = AccessTools.Field(t, "clip");

                if (_clipFlagField == null)
                    continue;

                if (_clipFlagField.GetValue(el) is not true)
                    continue;

                var b = el.Bounds;
                if (b == null)
                    continue;

                b.CalcWorldBounds();
                if (b.OuterWidth > 8 && b.OuterHeight > 8)
                    return b;
            }

            // fallback: bounds самого composer (диалог целиком, лучше чем ничего)
            var root = composer.Bounds;
            if (root != null)
            {
                root.CalcWorldBounds();
                if (root.OuterWidth > 8 && root.OuterHeight > 8)
                    return root;
            }
        }
        catch
        {
            // reflection / API drift
        }

        return null;
    }

    private static bool IsClipElementType(Type t)
    {
        if (_clipType != null)
            return t == _clipType || t.IsSubclassOf(_clipType);

        if (t.Name == "GuiElementClip")
        {
            _clipType = t;
            return true;
        }

        return false;
    }

    /// <summary>Иконка пересекается с clip (с небольшим запасом).</summary>
    private static bool IsIconVisibleInClip(Request req, ElementBounds clip)
    {
        if (!req.Component.TryGetScreenRect(req.RenderX, req.RenderY, out var x, out var y, out var w, out var h))
            return false;

        const double pad = 4;
        var clipL = clip.absX - pad;
        var clipT = clip.absY - pad;
        var clipR = clip.absX + clip.OuterWidth + pad;
        var clipB = clip.absY + clip.OuterHeight + pad;

        var iconR = x + w;
        var iconB = y + h;

        // AABB intersection
        return x < clipR && iconR > clipL && y < clipB && iconB > clipT;
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
        || dlg.GetType().Name.Contains("Handbook", StringComparison.OrdinalIgnoreCase);
}
