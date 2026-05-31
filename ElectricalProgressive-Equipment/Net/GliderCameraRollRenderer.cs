using ElectricalProgressive.Net;
using System;
using Vintagestory.API.Client;

internal class GliderCameraRollRenderer : IRenderer
{
    private readonly ICoreClientAPI capi;
    private readonly EGliderFlightPacketHandler handler;

    // 0.99 — запускаемся поздно в стадии Before,
    // когда VS уже пересчитал матрицу камеры
    public double RenderOrder => 0.99;
    public int RenderRange => 9999;

    public GliderCameraRollRenderer(ICoreClientAPI capi, EGliderFlightPacketHandler handler)
    {
        this.capi = capi;
        this.handler = handler;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        float roll = - handler.BankAngle;
        if (Math.Abs(roll) < 0.0001f) return;

        if (capi.IsGamePaused)
        {
            return;
        }

        double cos = Math.Cos(roll);
        double sin = Math.Sin(roll);

        // Модифицируем double-версию (используется для рендера мира)
        ApplyRoll(capi.Render.CameraMatrixOrigin, cos, sin);

        // Модифицируем float-версию (используется шейдерами напрямую)
        float cosF = (float)cos;
        float sinF = (float)sin;
        float[] camF = capi.Render.CameraMatrixOriginf;
        for (int c = 0; c < 4; c++)
        {
            int i = c * 4;
            float r0 = camF[i];
            float r1 = camF[i + 1];
            camF[i] = cosF * r0 - sinF * r1;
            camF[i + 1] = sinF * r0 + cosF * r1;
        }
    }

    // Пред-умножение матрицы на Rz (column-major): result = Rz * mat
    // Это вращение в пространстве камеры → чистый крен
    private static void ApplyRoll(double[] mat, double cos, double sin)
    {
        for (int c = 0; c < 4; c++)
        {
            int i = c * 4;
            double r0 = mat[i];
            double r1 = mat[i + 1];
            mat[i] = cos * r0 - sin * r1;
            mat[i + 1] = sin * r0 + cos * r1;
            // строки 2 и 3 не трогаем
        }
    }

    public void Dispose() { }
}