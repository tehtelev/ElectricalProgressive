using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Patch;

public class Mat4fMultiplyPatch
{
    private static HarmonyMethod _transpilerMethod;

    public static void RegisterPatch(Harmony harmony, ICoreAPI api)
    {
        var method = typeof(Mat4f).GetMethod(
            "Multiply",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(float[]), typeof(float[]), typeof(float[]) },
            null);

        if (method == null)
        {
            api.Logger.Error("Mat4fMultiplyPatch: Mat4f.Multiply not found!");
            return;
        }

        _transpilerMethod = new HarmonyMethod(
            typeof(Mat4fMultiplyPatch).GetMethod(
                nameof(Transpiler),
                BindingFlags.Static | BindingFlags.NonPublic));

        harmony.Patch(method, transpiler: _transpilerMethod);
    }

    public static void UnregisterPatch(Harmony harmony)
    {
        var method = typeof(Mat4f).GetMethod(
            "Multiply",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(float[]), typeof(float[]), typeof(float[]) },
            null);

        if (method != null && _transpilerMethod != null)
            harmony?.Unpatch(method, _transpilerMethod.method);
    }

    /// <summary>
    /// Полностью заменяет тело Mat4f.Multiply inline unsafe IL:
    /// fixed-блоки + прямая работа с указателями без double-промотирования и bounds-checks.
    /// ILGenerator нужен для объявления pinned-локальных переменных и меток ветвлений.
    /// </summary>
    static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> _,   // исходные инструкции — выбрасываем
        ILGenerator il)
    {
        var fType = typeof(float);
        var fPtrT = fType.MakePointerType();  // float*
        var fArrT = typeof(float[]);

        // ── Объявляем locals ──────────────────────────────────────────────────────
        // Эти locals добавляются ПОСЛЕ оригинальных [0..19], но мы используем
        // LocalBuilder-ссылки, поэтому Harmony выставит корректные индексы автоматически.

        var pa = il.DeclareLocal(fPtrT);          // float* pa
        var pb = il.DeclareLocal(fPtrT);          // float* pb
        var pout = il.DeclareLocal(fPtrT);          // float* pout
        var pinA = il.DeclareLocal(fArrT, true);    // float[] pinned  (для a)
        var pinB = il.DeclareLocal(fArrT, true);    // float[] pinned  (для b)
        var pinO = il.DeclareLocal(fArrT, true);    // float[] pinned  (для output)

        // aM[row*4 + col] = pa[row*4 + col]  — 16 кешированных элементов матрицы A
        var aM = new LocalBuilder[16];
        for (int i = 0; i < 16; i++) aM[i] = il.DeclareLocal(fType);

        // Временные b0..b3 для одной строки матрицы B
        var bV = new LocalBuilder[4];
        for (int i = 0; i < 4; i++) bV[i] = il.DeclareLocal(fType);

        var emit = new List<CodeInstruction>();

        // ── Вспомогательные функции ───────────────────────────────────────────────

        // Загружает ptr[idx] → dest.  Байтовое смещение = idx * sizeof(float) = idx * 4.
        // tag — опциональная метка для первой инструкции (для присоединения done-метки
        // предыдущего fixed-блока).
        void LoadPtrElem(LocalBuilder ptr, int idx, LocalBuilder dest, Label? tag = null)
        {
            var first = new CodeInstruction(OpCodes.Ldloc, ptr);
            if (tag.HasValue) first.labels.Add(tag.Value);
            emit.Add(first);
            if (idx > 0)
            {
                emit.Add(new CodeInstruction(OpCodes.Ldc_I4, idx));
                emit.Add(new CodeInstruction(OpCodes.Conv_I));
                emit.Add(new CodeInstruction(OpCodes.Ldc_I4_4));
                emit.Add(new CodeInstruction(OpCodes.Mul));
                emit.Add(new CodeInstruction(OpCodes.Add));
            }
            emit.Add(new CodeInstruction(OpCodes.Ldind_R4));
            emit.Add(new CodeInstruction(OpCodes.Stloc, dest));
        }

        // Помещает адрес ptr[idx] на стек (для последующего stind.r4).
        void PtrAddr(LocalBuilder ptr, int idx)
        {
            emit.Add(new CodeInstruction(OpCodes.Ldloc, ptr));
            if (idx > 0)
            {
                emit.Add(new CodeInstruction(OpCodes.Ldc_I4, idx));
                emit.Add(new CodeInstruction(OpCodes.Conv_I));
                emit.Add(new CodeInstruction(OpCodes.Ldc_I4_4));
                emit.Add(new CodeInstruction(OpCodes.Mul));
                emit.Add(new CodeInstruction(OpCodes.Add));
            }
        }

        // Вычисляет bV[0]*aM[j] + bV[1]*aM[4+j] + bV[2]*aM[8+j] + bV[3]*aM[12+j]
        // и оставляет результат на стеке (адрес назначения уже лежит под ним).
        void MulAdd4(int j)
        {
            emit.Add(new CodeInstruction(OpCodes.Ldloc, bV[0]));
            emit.Add(new CodeInstruction(OpCodes.Ldloc, aM[j]));
            emit.Add(new CodeInstruction(OpCodes.Mul));
            emit.Add(new CodeInstruction(OpCodes.Ldloc, bV[1]));
            emit.Add(new CodeInstruction(OpCodes.Ldloc, aM[4 + j]));
            emit.Add(new CodeInstruction(OpCodes.Mul));
            emit.Add(new CodeInstruction(OpCodes.Add));
            emit.Add(new CodeInstruction(OpCodes.Ldloc, bV[2]));
            emit.Add(new CodeInstruction(OpCodes.Ldloc, aM[8 + j]));
            emit.Add(new CodeInstruction(OpCodes.Mul));
            emit.Add(new CodeInstruction(OpCodes.Add));
            emit.Add(new CodeInstruction(OpCodes.Ldloc, bV[3]));
            emit.Add(new CodeInstruction(OpCodes.Ldloc, aM[12 + j]));
            emit.Add(new CodeInstruction(OpCodes.Mul));
            emit.Add(new CodeInstruction(OpCodes.Add));
        }

        // Генерирует fixed(float* ptr = arr) — стандартный C# паттерн:
        //   if (arr == null || arr.Length == 0) ptr = null;
        //   else ptr = &arr[0];
        //
        // incoming — метка предыдущего done, прикрепляется к первой инструкции блока.
        // Возвращает done-метку — нужно передать incoming следующему блоку.
        Label EmitFixed(OpCode ldargOp, LocalBuilder pinned, LocalBuilder ptr, Label? incoming = null)
        {
            var lblNull = il.DefineLabel();
            var lblNotNull = il.DefineLabel();
            var lblDone = il.DefineLabel();

            var firstInstr = new CodeInstruction(ldargOp);
            if (incoming.HasValue) firstInstr.labels.Add(incoming.Value);

            emit.Add(firstInstr);                                                                 // ldarg
            emit.Add(new CodeInstruction(OpCodes.Dup));                                          // dup
            emit.Add(new CodeInstruction(OpCodes.Stloc, pinned));                                // stloc pinned
            emit.Add(new CodeInstruction(OpCodes.Brfalse_S, lblNull));                           // brfalse NULL
            emit.Add(new CodeInstruction(OpCodes.Ldloc, pinned));                                // ldloc pinned
            emit.Add(new CodeInstruction(OpCodes.Ldlen));                                         // ldlen
            emit.Add(new CodeInstruction(OpCodes.Conv_I4));                                      // conv.i4
            emit.Add(new CodeInstruction(OpCodes.Brtrue_S, lblNotNull));                         // brtrue NOTNULL
            emit.Add(new CodeInstruction(OpCodes.Ldc_I4_0).WithLabels(lblNull));                 // [NULL:] ldc.i4.0
            emit.Add(new CodeInstruction(OpCodes.Conv_U));                                        // conv.u
            emit.Add(new CodeInstruction(OpCodes.Stloc, ptr));                                   // stloc ptr
            emit.Add(new CodeInstruction(OpCodes.Br_S, lblDone));                                // br DONE
            emit.Add(new CodeInstruction(OpCodes.Ldloc, pinned).WithLabels(lblNotNull));         // [NOTNULL:] ldloc pinned
            emit.Add(new CodeInstruction(OpCodes.Ldc_I4_0));                                     // ldc.i4.0
            emit.Add(new CodeInstruction(OpCodes.Ldelema, fType));                               // ldelema float
            emit.Add(new CodeInstruction(OpCodes.Conv_U));                                        // conv.u
            emit.Add(new CodeInstruction(OpCodes.Stloc, ptr));                                   // stloc ptr
            // lblDone прикрепится к первой инструкции следующего блока через incoming
            return lblDone;
        }

        // ── fixed()-блоки ─────────────────────────────────────────────────────────
        //   arg1 = a  →  pinA, pa
        //   arg2 = b  →  pinB, pb
        //   arg0 = output → pinO, pout
        var donePa = EmitFixed(OpCodes.Ldarg_1, pinA, pa);
        var donePb = EmitFixed(OpCodes.Ldarg_2, pinB, pb, donePa);
        var donePout = EmitFixed(OpCodes.Ldarg_0, pinO, pout, donePb);

        // ── Кешируем 16 элементов матрицы A ──────────────────────────────────────
        // Первая инструкция несёт метку donePout (вход в тело после fixed-пролога)
        LoadPtrElem(pa, 0, aM[0], donePout);
        for (int i = 1; i < 16; i++)
            LoadPtrElem(pa, i, aM[i]);

        // ── 4 строки матрицы B × 4 элемента выхода ───────────────────────────────
        for (int row = 0; row < 4; row++)
        {
            int baseIdx = row * 4;

            // Загружаем b0..b3 из pb[baseIdx .. baseIdx+3]
            for (int k = 0; k < 4; k++)
                LoadPtrElem(pb, baseIdx + k, bV[k]);

            // pout[baseIdx+j] = bV[0]*aM[j] + bV[1]*aM[4+j] + bV[2]*aM[8+j] + bV[3]*aM[12+j]
            for (int j = 0; j < 4; j++)
            {
                PtrAddr(pout, baseIdx + j);              // адрес назначения на стек
                MulAdd4(j);                               // значение на стек
                emit.Add(new CodeInstruction(OpCodes.Stind_R4)); // *addr = value
            }
        }

        // ── Снимаем пин (unpin) ───────────────────────────────────────────────────
        emit.Add(new CodeInstruction(OpCodes.Ldnull));
        emit.Add(new CodeInstruction(OpCodes.Stloc, pinA));
        emit.Add(new CodeInstruction(OpCodes.Ldnull));
        emit.Add(new CodeInstruction(OpCodes.Stloc, pinB));
        emit.Add(new CodeInstruction(OpCodes.Ldnull));
        emit.Add(new CodeInstruction(OpCodes.Stloc, pinO));

        // ── return output ─────────────────────────────────────────────────────────
        emit.Add(new CodeInstruction(OpCodes.Ldarg_0));
        emit.Add(new CodeInstruction(OpCodes.Ret));

        return emit;
    }
}