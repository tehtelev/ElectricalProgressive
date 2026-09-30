using HarmonyLib;
using System;
using System.Globalization;
using System.Reflection;
using Vintagestory.API.Client;

namespace ElectricalProgressive.Content.Storage;

/// <summary>
/// Число на иконке стопки: с 1000 пишется как 1к, 1.5к, 12к, 1.2м.
/// </summary>
public static class StackSizeTextPatch
{
    public static void Apply(Harmony harmony)
    {
        var type = AccessTools.TypeByName("Vintagestory.Client.NoObf.InventoryItemRenderer");
        var method = AccessTools.Method(type, "GenStackSizeTexture");
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(StackSizeTextPatch), nameof(Prefix)));
    }

    public static bool Prefix(object __instance, int stackSize, float fontSizeMultiplier, ref LoadedTexture __result)
    {
        if (stackSize < 1000)
            return true;

        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var renderer = __instance.GetType();
            var font = ((CairoFont)renderer.GetField("stackSizeFont", flags)!.GetValue(__instance)!).Clone();
            font.UnscaledFontsize *= fontSizeMultiplier;

            var game = renderer.GetField("game", flags)!.GetValue(__instance)!;
            var api = (ICoreClientAPI)game.GetType().GetField("api", flags)!.GetValue(game)!;
            __result = api.Gui.TextTexture.GenTextTexture(Format(stackSize), font, null);
            return false;
        }
        catch
        {
            return true;
        }
    }

    public static string Format(int stackSize)
    {
        if (stackSize >= 1_000_000)
            return Trim(stackSize / 1_000_000f) + "м";

        return Trim(stackSize / 1000f) + "к";
    }

    private static string Trim(float value)
    {
        var rounded = Math.Round(value, 1);
        if (Math.Abs(rounded - Math.Round(rounded)) < 0.05)
            return ((int)Math.Round(rounded)).ToString(CultureInfo.InvariantCulture);

        return rounded.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
