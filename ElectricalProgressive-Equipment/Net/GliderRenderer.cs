using System;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.Client.NoObf;

namespace ElectricalProgressive.Net;

public class GliderCameraRollRenderer : IRenderer
{
    // Кэшированные ссылки — инициализируются ОДИН раз в конструкторе
    private readonly ClientMain _clientMain;
    private readonly PlayerCamera _camera;
    private readonly StackMatrix4 _mvMatrix;
    private readonly StackMatrix4 _pMatrix;
    private readonly FrustumCulling _frustum;
    private readonly EGliderFlightPacketHandler _handler;
    private EntityPlayer _player; // может обновляться при респавне

    public double RenderOrder => 0.99;
    public int RenderRange => 9999;

    public GliderCameraRollRenderer(ICoreClientAPI capi, EGliderFlightPacketHandler handler)
    {
        _handler = handler;

        // === Кэшируем ВСЕ ссылки здесь — никаких кастов в OnRenderFrame ===
        _clientMain = (ClientMain)capi.World;
        _camera = _clientMain.MainCamera;
        _mvMatrix = _clientMain.MvMatrix;
        _pMatrix = _clientMain.PMatrix;
        _frustum = capi.Render.DefaultFrustumCuller;
        _player = capi.World.Player?.Entity;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        // === Ранний выход: 99% кадров пропустят остальной код ===
        float roll = -_handler.BankAngle;
        if (roll == 0f || _clientMain.IsPaused)
            return;

        // Точная проверка только если прошло первую
        if (Math.Abs(roll) < 0.0001f)
            return;

        // === Кэшируем тригонометрию (один вызов на кадр) ===
        double cos = Math.Cos(roll);
        double sin = Math.Sin(roll);

        // === 1. Модифицируем матрицы камеры (для рендера) ===
        ApplyRoll(_camera.CameraMatrix, cos, sin);
        ApplyRoll(_camera.CameraMatrixOrigin, cos, sin);
        ApplyRollFloat(_camera.CameraMatrixOriginf, roll);

        // === 2. Модифицируем MvMatrix.Top (для frustum) ===
        // .Top возвращает reference на внутренний массив — копирования нет
        double[] mvTop = _mvMatrix.Top;
        ApplyRoll(mvTop, cos, sin);

        // === 3. Пересчитываем frustum ===
        if (_frustum != null)
        {
            // Обновляем ссылку на игрока, если нужно (редкий случай)
            _player ??= _clientMain.player?.Entity;
            if (_player != null)
            {
                _frustum.CalcFrustumEquations(
                    _player.Pos.AsBlockPos,
                    _pMatrix.Top,  // проекция — без изменений
                    mvTop          // вид — с применённым roll
                );
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyRoll(double[] mat, double cos, double sin)
    {
        // Column-major: вращаем первые две строки каждой колонки
        // Разворачиваем цикл вручную — компилятор лучше оптимизирует
        int i0 = 0, i1 = 4, i2 = 8, i3 = 12;

        double r0 = mat[i0], r1 = mat[i0 + 1];
        mat[i0] = cos * r0 - sin * r1;
        mat[i0 + 1] = sin * r0 + cos * r1;

        r0 = mat[i1]; r1 = mat[i1 + 1];
        mat[i1] = cos * r0 - sin * r1;
        mat[i1 + 1] = sin * r0 + cos * r1;

        r0 = mat[i2]; r1 = mat[i2 + 1];
        mat[i2] = cos * r0 - sin * r1;
        mat[i2 + 1] = sin * r0 + cos * r1;

        r0 = mat[i3]; r1 = mat[i3 + 1];
        mat[i3] = cos * r0 - sin * r1;
        mat[i3 + 1] = sin * r0 + cos * r1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyRollFloat(float[] mat, float roll)
    {
        if (mat == null || mat.Length < 16) return;
        float cos = (float)Math.Cos(roll);
        float sin = (float)Math.Sin(roll);

        int i0 = 0, i1 = 4, i2 = 8, i3 = 12;

        float r0 = mat[i0], r1 = mat[i0 + 1];
        mat[i0] = cos * r0 - sin * r1;
        mat[i0 + 1] = sin * r0 + cos * r1;

        r0 = mat[i1]; r1 = mat[i1 + 1];
        mat[i1] = cos * r0 - sin * r1;
        mat[i1 + 1] = sin * r0 + cos * r1;

        r0 = mat[i2]; r1 = mat[i2 + 1];
        mat[i2] = cos * r0 - sin * r1;
        mat[i2 + 1] = sin * r0 + cos * r1;

        r0 = mat[i3]; r1 = mat[i3 + 1];
        mat[i3] = cos * r0 - sin * r1;
        mat[i3 + 1] = sin * r0 + cos * r1;
    }

    public void Dispose() { }
}