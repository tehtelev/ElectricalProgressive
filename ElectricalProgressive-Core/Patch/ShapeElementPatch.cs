using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Patch;

public class ShapeElementPatch
{
    // ── Layout extended inverseModelTransform ─────────────────────────────────
    // [0..15]  = обратная модельная матрица (существующее поле, используется извне)
    // [16..31] = V0 base matrix  (animVersion=0, tf=identity)
    // [32..47] = V1 base matrix  (animVersion=1, tf=identity)
    private const int V0_OFFSET = 16;
    private const int V1_OFFSET = 32;
    private const int TOTAL_SIZE = 48;

    internal static readonly ElementPose NoTransform = new ElementPose();

    // ── Reflection ────────────────────────────────────────────────────────────
    private static readonly FieldInfo _fiInvMat =
        typeof(ShapeElement).GetField("inverseModelTransform",
            BindingFlags.Public | BindingFlags.Instance)!;

    private static readonly MethodInfo _miEnsureCache =
        typeof(ShapeElementPatch).GetMethod(nameof(EnsureCache),
            BindingFlags.Public | BindingFlags.Static)!;

    private static readonly MethodInfo _miMat4fCreate =
        typeof(Mat4f).GetMethod("Create",
            BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null)!;

    private static readonly MethodInfo _miArrayCopy5 =
        typeof(Array).GetMethod("Copy",
            new[] { typeof(Array), typeof(int), typeof(Array), typeof(int), typeof(int) })!;

    private static readonly MethodInfo _miIsTfEmpty =
        typeof(ShapeElementPatch).GetMethod(nameof(IsTfEmpty),
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo _miApplyTfV1 =
        typeof(ShapeElementPatch).GetMethod(nameof(ApplyTfV1),
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo _miComputeV0WithTf =
        typeof(ShapeElementPatch).GetMethod(nameof(ComputeV0WithTf),
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly FieldInfo _fiNoTransform =
        typeof(ShapeElementPatch).GetField(nameof(NoTransform),
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;

    private static HarmonyMethod _transpilerGetLocal;
    private static HarmonyMethod _postfixClone;

    private static unsafe void FastCopy16(float[] src, int srcIdx, float[] dst, int dstIdx)
    {
        fixed (float* pSrc = &src[srcIdx], pDst = &dst[dstIdx])
        {
            // Копируем 16 float = 64 байта
            Unsafe.CopyBlockUnaligned(pDst, pSrc, 16 * sizeof(float));
        }
    }

    private static readonly MethodInfo _miFastCopy16 =
        typeof(ShapeElementPatch).GetMethod(nameof(FastCopy16),
            BindingFlags.NonPublic | BindingFlags.Static);

    // =========================================================================
    public static void RegisterPatch(Harmony harmony, ICoreAPI api)
    {
        if (_fiInvMat == null)
        {
            api.Logger.Error("ShapeElementPatch: inverseModelTransform not found!");
            return;
        }

        if (_miEnsureCache == null)
        {
            api.Logger.Error("ShapeElementPatch: EnsureCache not found!");
            return;
        }

        if (_miMat4fCreate == null)
        {
            api.Logger.Error("ShapeElementPatch: Mat4f.Create not found!");
            return;
        }

        if (_miArrayCopy5 == null)
        {
            api.Logger.Error("ShapeElementPatch: Array.Copy(5) not found!");
            return;
        }

        if (_miIsTfEmpty == null)
        {
            api.Logger.Error("ShapeElementPatch: IsTfEmpty not found!");
            return;
        }

        if (_miApplyTfV1 == null)
        {
            api.Logger.Error("ShapeElementPatch: ApplyTfV1 not found!");
            return;
        }

        if (_miComputeV0WithTf == null)
        {
            api.Logger.Error("ShapeElementPatch: ComputeV0WithTf not found!");
            return;
        }

        if (_fiNoTransform == null)
        {
            api.Logger.Error("ShapeElementPatch: NoTransform not found!");
            return;
        }

        var getLocalMethod = typeof(ShapeElement).GetMethod(
                                 "GetLocalTransformMatrix",
                                 BindingFlags.Public | BindingFlags.Instance, null,
                                 new[] { typeof(int), typeof(float[]), typeof(ElementPose) }, null);

        if (getLocalMethod == null)
        {
            api.Logger.Error("ShapeElementPatch: GetLocalTransformMatrix not found!");
            return;
        }

        _transpilerGetLocal = new HarmonyMethod(
            typeof(ShapeElementPatch).GetMethod(nameof(TranspilerGetLocal),
                BindingFlags.Static | BindingFlags.NonPublic));
        harmony.Patch(getLocalMethod, transpiler: _transpilerGetLocal);

        // Clone() уже делает inverseModelTransform?.Clone() — весь 48-элементный
        // массив скопируется автоматически. Постфикс не нужен.
        // Но добавим на случай если Clone был перегружен и не копирует поле:
        var cloneMethod = typeof(ShapeElement).GetMethod(
            "Clone", BindingFlags.Public | BindingFlags.Instance);

        if (cloneMethod == null)
        {
            api.Logger.Error("ShapeElementPatch: Clone not found!");
            return;
        }

        _postfixClone = new HarmonyMethod(
            typeof(ShapeElementPatch).GetMethod(nameof(PostfixClone),
                BindingFlags.Static | BindingFlags.NonPublic));
        harmony.Patch(cloneMethod, postfix: _postfixClone);
    }

    public static void UnregisterPatch(Harmony harmony)
    {
        var m1 = typeof(ShapeElement).GetMethod("GetLocalTransformMatrix",
            BindingFlags.Public | BindingFlags.Instance, null,
            new[] { typeof(int), typeof(float[]), typeof(ElementPose) }, null);
        if (m1 != null && _transpilerGetLocal != null)
            harmony?.Unpatch(m1, _transpilerGetLocal.method);

        var m2 = typeof(ShapeElement).GetMethod("Clone",
            BindingFlags.Public | BindingFlags.Instance);
        if (m2 != null && _postfixClone != null)
            harmony?.Unpatch(m2, _postfixClone.method);
    }

    // =========================================================================
    // Транспайлер: полная замена тела GetLocalTransformMatrix
    //
    // Генерируемая логика:
    //   cache = EnsureCache(this)          ← только проверка arr.Length после первого вызова
    //   if (tf == null) tf = NoTransform
    //   output ??= Mat4f.Create()
    //   tfEmpty = IsTfEmpty(tf)
    //   if (animVersion == 1) {
    //       Array.Copy(cache, 32, output, 0, 16)
    //       if (!tfEmpty) ApplyTfV1(output, tf)
    //   } else {
    //       if (tfEmpty) Array.Copy(cache, 16, output, 0, 16)
    //       else         ComputeV0WithTf(this, output, tf)
    //   }
    //   return output
    // =========================================================================
    static IEnumerable<CodeInstruction> TranspilerGetLocal(
        IEnumerable<CodeInstruction> _,
        ILGenerator il)
    {
        var localCache = il.DeclareLocal(typeof(float[]));
        var localTfEmpty = il.DeclareLocal(typeof(bool));

        var lblTfNotNull = il.DefineLabel();
        var lblOutputNotNull = il.DefineLabel();
        var lblV0Branch = il.DefineLabel();
        var lblV0TfEmpty = il.DefineLabel();
        var lblWithTf = il.DefineLabel();
        var lblDone = il.DefineLabel();

        var emit = new List<CodeInstruction>();

        // ── cache = EnsureCache(this) ─────────────────────────────────────────
        // Горячий путь: одна проверка arr != null && arr.Length >= 48, ноль словарей
        emit.Add(new CodeInstruction(OpCodes.Ldarg_0));
        emit.Add(new CodeInstruction(OpCodes.Call, _miEnsureCache));
        emit.Add(new CodeInstruction(OpCodes.Stloc, localCache));

        // ── if (tf == null) tf = NoTransform ─────────────────────────────────
        emit.Add(new CodeInstruction(OpCodes.Ldarg_3));
        emit.Add(new CodeInstruction(OpCodes.Brtrue_S, lblTfNotNull));
        emit.Add(new CodeInstruction(OpCodes.Ldsfld, _fiNoTransform));
        emit.Add(new CodeInstruction(OpCodes.Starg_S, (byte)3));

        // ── output ??= Mat4f.Create() ─────────────────────────────────────────
        emit.Add(new CodeInstruction(OpCodes.Ldarg_2).WithLabels(lblTfNotNull));
        emit.Add(new CodeInstruction(OpCodes.Brtrue_S, lblOutputNotNull));
        emit.Add(new CodeInstruction(OpCodes.Call, _miMat4fCreate));
        emit.Add(new CodeInstruction(OpCodes.Starg_S, (byte)2));

        // ── tfEmpty = IsTfEmpty(tf) ───────────────────────────────────────────
        emit.Add(new CodeInstruction(OpCodes.Ldarg_3).WithLabels(lblOutputNotNull));
        emit.Add(new CodeInstruction(OpCodes.Call, _miIsTfEmpty));
        emit.Add(new CodeInstruction(OpCodes.Stloc, localTfEmpty));

        // ── if (animVersion != 1) goto V0Branch ──────────────────────────────
        emit.Add(new CodeInstruction(OpCodes.Ldarg_1));
        emit.Add(new CodeInstruction(OpCodes.Ldc_I4_1));
        emit.Add(new CodeInstruction(OpCodes.Bne_Un, lblV0Branch));

        emit.Add(new CodeInstruction(OpCodes.Ldloc, localCache));
        emit.Add(new CodeInstruction(OpCodes.Ldc_I4, V1_OFFSET));
        emit.Add(new CodeInstruction(OpCodes.Ldarg_2));
        emit.Add(new CodeInstruction(OpCodes.Ldc_I4_0));
        emit.Add(new CodeInstruction(OpCodes.Call, _miFastCopy16));   // FastCopy16(cache, V1_OFFSET, output, 0)

        // ── if (tfEmpty) goto Done; ApplyTfV1(output, tf) ────────────────────
        emit.Add(new CodeInstruction(OpCodes.Ldloc, localTfEmpty));
        emit.Add(new CodeInstruction(OpCodes.Brtrue, lblDone));
        emit.Add(new CodeInstruction(OpCodes.Ldarg_2));
        emit.Add(new CodeInstruction(OpCodes.Ldarg_3));
        emit.Add(new CodeInstruction(OpCodes.Call, _miApplyTfV1));
        emit.Add(new CodeInstruction(OpCodes.Br, lblDone));

        // ── V0: if (!tfEmpty) goto WithTf ────────────────────────────────────
        emit.Add(new CodeInstruction(OpCodes.Ldloc, localTfEmpty).WithLabels(lblV0Branch));
        emit.Add(new CodeInstruction(OpCodes.Brfalse, lblWithTf));

        // ── V0 + empty tf: Array.Copy(cache, V0_OFFSET=16, output, 0, 16) ────
        emit.Add(new CodeInstruction(OpCodes.Ldloc, localCache));
        emit.Add(new CodeInstruction(OpCodes.Ldc_I4, V0_OFFSET));
        emit.Add(new CodeInstruction(OpCodes.Ldarg_2));
        emit.Add(new CodeInstruction(OpCodes.Ldc_I4_0));
        emit.Add(new CodeInstruction(OpCodes.Call, _miFastCopy16));   // FastCopy16(cache, V0_OFFSET, output, 0)
        emit.Add(new CodeInstruction(OpCodes.Br, lblDone));

        // ── V0 + tf: ComputeV0WithTf(this, output, tf) ───────────────────────
        emit.Add(new CodeInstruction(OpCodes.Ldarg_0).WithLabels(lblWithTf));
        emit.Add(new CodeInstruction(OpCodes.Ldarg_2));
        emit.Add(new CodeInstruction(OpCodes.Ldarg_3));
        emit.Add(new CodeInstruction(OpCodes.Call, _miComputeV0WithTf));

        // ── return output ─────────────────────────────────────────────────────
        emit.Add(new CodeInstruction(OpCodes.Ldarg_2).WithLabels(lblDone));
        emit.Add(new CodeInstruction(OpCodes.Ret));

        return emit;
    }

    // =========================================================================
    // Постфикс Clone: Clone() уже копирует inverseModelTransform через ?.Clone(),
    // поэтому 48-элементный массив переедет в клон автоматически.
    // Постфикс нужен только если оригинальный Clone был переопределён и не копирует поле.
    // =========================================================================
    static void PostfixClone(ShapeElement __instance, ShapeElement __result)
    {
        // Страховка: если клон по какой-то причине не получил расширенный массив
        if (__result.inverseModelTransform?.Length < TOTAL_SIZE
            && __instance.inverseModelTransform?.Length >= TOTAL_SIZE)
        {
            __result.inverseModelTransform =
                (float[])__instance.inverseModelTransform.Clone();
        }
    }

    // =========================================================================
    // EnsureCache — НОЛЬ внешних структур данных, НОЛЬ словарей.
    // Горячий путь: проверка двух условий на массиве, уже лежащем в поле объекта.
    // Холодный путь (первый вызов): расширяем массив и вычисляем V0/V1.
    // =========================================================================
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float[] EnsureCache(ShapeElement elem)
    {
        var arr = elem.inverseModelTransform;
        if (arr != null && arr.Length >= TOTAL_SIZE)
            return arr;             // ← горячий путь: две проверки, возврат
        return BuildCache(elem);    // ← холодный путь: только при первом вызове
    }

    [MethodImpl(MethodImplOptions.NoInlining)]  // не инлайним, чтобы не раздувать горячий путь
    private static float[] BuildCache(ShapeElement elem)
    {
        float ox = 0f, oy = 0f, oz = 0f;
        if (elem.RotationOrigin != null)
        {
            ox = (float)elem.RotationOrigin[0] / 16f;
            oy = (float)elem.RotationOrigin[1] / 16f;
            oz = (float)elem.RotationOrigin[2] / 16f;
        }

        var newArr = new float[TOTAL_SIZE];

        // Сохраняем существующую обратную матрицу если она уже была вычислена
        var existing = elem.inverseModelTransform;
        if (existing != null && existing.Length >= 16)
            Array.Copy(existing, 0, newArr, 0, 16);

        // Вычисляем V0 → newArr[16..31]
        var tmpV0 = Mat4f.Create();
        ComputeBaseV0(elem, tmpV0, ox, oy, oz);
        Array.Copy(tmpV0, 0, newArr, V0_OFFSET, 16);

        // Вычисляем V1 → newArr[32..47]
        var tmpV1 = Mat4f.Create();
        ComputeBaseV1(elem, tmpV1, ox, oy, oz);
        Array.Copy(tmpV1, 0, newArr, V1_OFFSET, 16);

        elem.inverseModelTransform = newArr;
        return newArr;
    }

    /// <summary>
    /// Инвалидирует кеш (вызывать если RotationX/Y/Z, Scale, From или RotationOrigin изменились).
    /// Обрезает массив обратно до 16, чтобы EnsureCache при следующем вызове пересчитал V0/V1.
    /// </summary>
    public static void InvalidateCache(ShapeElement elem)
    {
        var arr = elem.inverseModelTransform;
        if (arr == null || arr.Length < TOTAL_SIZE) return;
        var trimmed = new float[16];
        Array.Copy(arr, trimmed, 16);
        elem.inverseModelTransform = trimmed;
    }

    // =========================================================================
    // Matrix helpers (холодные пути, вызываются редко)
    // =========================================================================

    private static void ComputeBaseV0(ShapeElement e, float[] m, float ox, float oy, float oz)
    {
        Mat4f.Identity(m);
        if (ox != 0f || oy != 0f || oz != 0f)
            Mat4f.Translate(m, m, ox, oy, oz);

        float rx = (float)e.RotationX * GameMath.DEG2RAD; if (rx != 0f) Mat4f.RotateX(m, m, rx);
        float ry = (float)e.RotationY * GameMath.DEG2RAD; if (ry != 0f) Mat4f.RotateY(m, m, ry);
        float rz = (float)e.RotationZ * GameMath.DEG2RAD; if (rz != 0f) Mat4f.RotateZ(m, m, rz);

        float sx = (float)e.ScaleX, sy = (float)e.ScaleY, sz = (float)e.ScaleZ;
        if (sx != 1f || sy != 1f || sz != 1f)
            Mat4f.Scale(m, m, sx, sy, sz);

        float tx = (float)e.From[0] / 16f - ox;
        float ty = (float)e.From[1] / 16f - oy;
        float tz = (float)e.From[2] / 16f - oz;
        if (tx != 0f || ty != 0f || tz != 0f)
            Mat4f.Translate(m, m, tx, ty, tz);
    }

    private static void ComputeBaseV1(ShapeElement e, float[] m, float ox, float oy, float oz)
    {
        Mat4f.Identity(m);
        if (ox != 0f || oy != 0f || oz != 0f)
            Mat4f.Translate(m, m, ox, oy, oz);

        float sx = (float)e.ScaleX, sy = (float)e.ScaleY, sz = (float)e.ScaleZ;
        if (sx != 1f || sy != 1f || sz != 1f)
            Mat4f.Scale(m, m, sx, sy, sz);

        float rx = (float)e.RotationX * GameMath.DEG2RAD; if (rx != 0f) Mat4f.RotateX(m, m, rx);
        float ry = (float)e.RotationY * GameMath.DEG2RAD; if (ry != 0f) Mat4f.RotateY(m, m, ry);
        float rz = (float)e.RotationZ * GameMath.DEG2RAD; if (rz != 0f) Mat4f.RotateZ(m, m, rz);

        float tx = -ox + (float)e.From[0] / 16f;
        float ty = -oy + (float)e.From[1] / 16f;
        float tz = -oz + (float)e.From[2] / 16f;
        if (tx != 0f || ty != 0f || tz != 0f)
            Mat4f.Translate(m, m, tx, ty, tz);
    }

    internal static void ApplyTfV1(float[] m, ElementPose tf)
    {
        if (tf.translateX != 0f || tf.translateY != 0f || tf.translateZ != 0f)
            Mat4f.Translate(m, m, tf.translateX, tf.translateY, tf.translateZ);
        if (tf.scaleX != 1f || tf.scaleY != 1f || tf.scaleZ != 1f)
            Mat4f.Scale(m, m, tf.scaleX, tf.scaleY, tf.scaleZ);

        float rx = (tf.degX + tf.degOffX) * GameMath.DEG2RAD; if (rx != 0f) Mat4f.RotateX(m, m, rx);
        float ry = (tf.degY + tf.degOffY) * GameMath.DEG2RAD; if (ry != 0f) Mat4f.RotateY(m, m, ry);
        float rz = (tf.degZ + tf.degOffZ) * GameMath.DEG2RAD; if (rz != 0f) Mat4f.RotateZ(m, m, rz);
    }

    internal static void ComputeV0WithTf(ShapeElement e, float[] m, ElementPose tf)
    {
        float ox = 0f, oy = 0f, oz = 0f;
        if (e.RotationOrigin != null)
        {
            ox = (float)e.RotationOrigin[0] / 16f;
            oy = (float)e.RotationOrigin[1] / 16f;
            oz = (float)e.RotationOrigin[2] / 16f;
        }
        Mat4f.Identity(m);
        if (ox != 0f || oy != 0f || oz != 0f) Mat4f.Translate(m, m, ox, oy, oz);

        float rx = (float)(e.RotationX + tf.degX + tf.degOffX) * GameMath.DEG2RAD;
        if (rx != 0f) Mat4f.RotateX(m, m, rx);
        float ry = (float)(e.RotationY + tf.degY + tf.degOffY) * GameMath.DEG2RAD;
        if (ry != 0f) Mat4f.RotateY(m, m, ry);
        float rz = (float)(e.RotationZ + tf.degZ + tf.degOffZ) * GameMath.DEG2RAD;
        if (rz != 0f) Mat4f.RotateZ(m, m, rz);

        float sx = (float)e.ScaleX * tf.scaleX;
        float sy = (float)e.ScaleY * tf.scaleY;
        float sz = (float)e.ScaleZ * tf.scaleZ;
        if (sx != 1f || sy != 1f || sz != 1f) Mat4f.Scale(m, m, sx, sy, sz);

        float tx = (float)e.From[0] / 16f + tf.translateX - ox;
        float ty = (float)e.From[1] / 16f + tf.translateY - oy;
        float tz = (float)e.From[2] / 16f + tf.translateZ - oz;
        if (tx != 0f || ty != 0f || tz != 0f) Mat4f.Translate(m, m, tx, ty, tz);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsTfEmpty(ElementPose tf) =>
        tf.translateX == 0f && tf.translateY == 0f && tf.translateZ == 0f
     && tf.scaleX == 1f && tf.scaleY == 1f && tf.scaleZ == 1f
     && tf.degX == 0f && tf.degY == 0f && tf.degZ == 0f
     && tf.degOffX == 0f && tf.degOffY == 0f && tf.degOffZ == 0f;
}