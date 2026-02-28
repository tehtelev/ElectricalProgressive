using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ElectricalProgressive.Patch;

public class MechBlockRendererPatch
{
    private static HarmonyMethod _transpilerMethod;

    public static void RegisterPatch(Harmony harmony)
    {
        var method = typeof(MechBlockRenderer).GetMethod(
            "UpdateCustomFloatBuffer",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new Exception("MechBlockRendererPatch: UpdateCustomFloatBuffer not found!");

        _transpilerMethod = new HarmonyMethod(
            typeof(MechBlockRendererPatch).GetMethod(
                nameof(Transpiler),
                BindingFlags.Static | BindingFlags.NonPublic));

        harmony.Patch(method, transpiler: _transpilerMethod);
    }

    public static void UnregisterPatch(Harmony harmony)
    {
        var method = typeof(MechBlockRenderer).GetMethod(
            "UpdateCustomFloatBuffer",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (method != null && _transpilerMethod != null)
            harmony.Unpatch(method, _transpilerMethod.method);
    }

    /// <summary>
    /// Кешируем dev.Position в локальную переменную, чтобы не вызывать
    /// callvirt get_Position() трижды на каждую итерацию цикла.
    ///
    /// До патча:
    ///   dev = enumerator.Current          ← stloc.3
    ///   tmp.Set(dev.Position.X - ...)     ← ldloc.3 + callvirt get_Position  (1)
    ///          dev.Position.Y - ...       ← ldloc.3 + callvirt get_Position  (2)
    ///          dev.Position.Z - ...       ← ldloc.3 + callvirt get_Position  (3)
    ///
    /// После патча:
    ///   dev    = enumerator.Current       ← stloc.3
    ///   devPos = dev.Position             ← ldloc.3 + callvirt + stloc devPos  [вставка]
    ///   tmp.Set(devPos.X - ...)           ← ldloc devPos  (1)
    ///          devPos.Y - ...             ← ldloc devPos  (2)
    ///          devPos.Z - ...             ← ldloc devPos  (3)
    /// </summary>
    static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        ILGenerator il)
    {
        var getPosition = typeof(IMechanicalPowerRenderable)
                              .GetProperty("Position")
                              ?.GetGetMethod()
                          ?? throw new Exception(
                              "MechBlockRendererPatch: get_Position not found!");

        // Новый локал: BlockPos devPos — индекс назначит ILGenerator автоматически
        var devPosLocal = il.DeclareLocal(typeof(BlockPos));

        var codes = new List<CodeInstruction>(instructions);
        var result = new List<CodeInstruction>();
        bool cacheInjected = false;

        for (int i = 0; i < codes.Count; i++)
        {
            // ── Точка инъекции: stloc.3 (dev = enumerator.Current) ────────────────
            // Вставляем devPos = dev.Position сразу после присвоения dev.
            // cacheInjected защищает от повторной вставки, если stloc.3
            // встречается ещё где-то (например, в другой ветке).
            if (!cacheInjected && codes[i].opcode == OpCodes.Stloc_3)
            {
                result.Add(codes[i]);                                                        // stloc.3
                result.Add(new CodeInstruction(OpCodes.Ldloc_3));                           // ldloc.3 (dev)
                result.Add(new CodeInstruction(OpCodes.Callvirt, getPosition));             // callvirt get_Position
                result.Add(new CodeInstruction(OpCodes.Stloc, devPosLocal));               // stloc devPosLocal
                cacheInjected = true;
                continue;
            }

            // ── Замена: ldloc.3 + callvirt get_Position → ldloc devPosLocal ───────
            if (i + 1 < codes.Count &&
                codes[i].opcode == OpCodes.Ldloc_3 &&
                codes[i + 1].opcode == OpCodes.Callvirt &&
                codes[i + 1].operand is MethodInfo mi &&
                mi == getPosition)
            {
                // Переносим метки с ldloc.3 на новую инструкцию, чтобы не сломать
                // ветвления (brtrue, br.s), которые могут на неё ссылаться
                var ldDevPos = new CodeInstruction(OpCodes.Ldloc, devPosLocal);
                ldDevPos.labels.AddRange(codes[i].labels);
                result.Add(ldDevPos);
                i++;    // пропускаем следующий callvirt get_Position
                continue;
            }

            result.Add(codes[i]);
        }

        if (!cacheInjected)
            throw new Exception(
                "MechBlockRendererPatch: injection point (stloc.3) not found!");

        return result;
    }
}