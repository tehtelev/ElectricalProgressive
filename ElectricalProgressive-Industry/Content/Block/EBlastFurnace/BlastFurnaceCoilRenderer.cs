using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EBlastFurnace;

/// <summary>
/// Спираль Cube3/153/165: светится, пока печь потребляет энергию.
/// </summary>
public class BlastFurnaceCoilRenderer : IRenderer
{
    private readonly ICoreClientAPI _capi;
    private readonly BlockEntityEBlastFurnace _be;
    private readonly Matrixf _modelMat = new();
    private MultiTextureMeshRef? _meshRef;

    public double RenderOrder => 0.6;
    public int RenderRange => 24;

    public BlastFurnaceCoilRenderer(ICoreClientAPI capi, BlockEntityEBlastFurnace be)
    {
        _capi = capi;
        _be = be;
    }

    public void SetMesh(MeshData? mesh)
    {
        _meshRef?.Dispose();
        _meshRef = null;
        if (mesh != null && mesh.VerticesCount > 0)
            _meshRef = _capi.Render.UploadMultiTextureMesh(mesh);
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (_meshRef == null)
            return;

        if (!_be.IsConsumingEnergy())
            return;

        var pos = _be.Pos;
        var cam = _capi.World.Player.Entity.CameraPos;
        var light = _capi.World.BlockAccessor.GetLightRGBs(pos.X, pos.Y, pos.Z);
        var heat = GameMath.Clamp(_be.RecipeProgress, 0.35f, 1f);
        var temp = (int)(800 + heat * 800);
        var extraGlow = GameMath.Clamp(120 + (int)(heat * 135), 120, 255);
        var incand = ColorUtil.GetIncandescenceColorAsColor4f(temp);

        var prog = _capi.Render.StandardShader;
        prog.Use();
        prog.RgbaAmbientIn = _capi.Render.AmbientColor;
        prog.RgbaFogIn = _capi.Render.FogColor;
        prog.FogMinIn = _capi.Render.FogMin;
        prog.FogDensityIn = _capi.Render.FogDensity;
        prog.RgbaLightIn = light;
        prog.RgbaGlowIn = new Vec4f(incand[0], incand[1], incand[2], 1f);
        prog.RgbaTint = ColorUtil.WhiteArgbVec;
        prog.ExtraGlow = extraGlow;
        prog.TempGlowMode = 0;
        prog.DontWarpVertices = 1;
        prog.AddRenderFlags = 0;
        prog.NormalShaded = 1;
        prog.AlphaTest = 0.05f;
        prog.ExtraZOffset = 0.01f;
        prog.ViewMatrix = _capi.Render.CameraMatrixOriginf;
        prog.ProjectionMatrix = _capi.Render.CurrentProjectionMatrix;

        _modelMat.Identity()
            .Translate(pos.X - cam.X, pos.Y - cam.Y, pos.Z - cam.Z);
        prog.ModelMatrix = _modelMat.Values;

        _capi.Render.GlDisableCullFace();
        _capi.Render.GLDepthMask(false);
        _capi.Render.RenderMultiTextureMesh(_meshRef, "tex");
        _capi.Render.GLDepthMask(true);
        prog.Stop();
    }

    public void Dispose()
    {
        _meshRef?.Dispose();
        _meshRef = null;
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    }
}
