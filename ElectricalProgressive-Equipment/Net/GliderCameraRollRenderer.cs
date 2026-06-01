using System;
using Vintagestory.API.Client;

namespace ElectricalProgressive.Net;

internal class GliderCameraRollRenderer : IRenderer
{
    private readonly ICoreClientAPI capi;
    private readonly EGliderFlightPacketHandler handler;
    private double[] originalMatrix; // для возможного восстановления

    public double RenderOrder => -1000.0; // гарантированно до всех системных операций
    public int RenderRange => 9999;

    public GliderCameraRollRenderer(ICoreClientAPI capi, EGliderFlightPacketHandler handler)
    {
        this.capi = capi;
        this.handler = handler;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        float roll = -handler.BankAngle;
        if (capi.IsGamePaused || Math.Abs(roll) < 0.0001f)
            return;


        double cos = Math.Cos(roll);
        double sin = Math.Sin(roll);

        // Модифицируем double-матрицу
        double[] mat = capi.Render.CameraMatrixOrigin;
        for (int c = 0; c < 4; c++)
        {
            int i = c * 4;
            double r0 = mat[i], r1 = mat[i + 1];
            mat[i] = cos * r0 - sin * r1;
            mat[i + 1] = sin * r0 + cos * r1;
        }

        // Синхронизируем float-версию, если она используется где-то ещё
        float cosF = (float)cos, sinF = (float)sin;
        float[] matF = capi.Render.CameraMatrixOriginf;
        for (int c = 0; c < 4; c++)
        {
            int i = c * 4;
            float r0 = matF[i], r1 = matF[i + 1];
            matF[i] = cosF * r0 - sinF * r1;
            matF[i + 1] = sinF * r0 + cosF * r1;
        }
    }

    public void Dispose() { }
}