using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block;

/// <summary>
/// Слиток/заготовка шейдером горна (каление, не перекрас альбедо).
/// </summary>
public class SmithingWorkItemRenderer : IRenderer
{
    private readonly ICoreClientAPI _capi;
    private readonly Func<BlockPos> _getPos;
    private readonly Func<ItemStack?> _getStack;
    private MultiTextureMeshRef? _meshRef;
    private readonly Matrixf _modelMat = new();

    public double RenderOrder => 0.5;
    public int RenderRange => 24;

    public SmithingWorkItemRenderer(ICoreClientAPI capi, Func<BlockPos> getPos, Func<ItemStack?> getStack)
    {
        _capi = capi;
        _getPos = getPos;
        _getStack = getStack;
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

        var stack = _getStack();
        if (stack?.Collectible == null)
            return;

        var prog = _capi.ModLoader.GetModSystem<SurvivalCoreSystem>()?.smithingWorkItemShader;
        if (prog == null)
            return;

        var pos = _getPos();
        float temp = stack.Collectible.GetTemperature(_capi.World, stack);
        float[] incand = ColorUtil.GetIncandescenceColorAsColor4f((int)temp);
        int extraGlow = GameMath.Clamp((int)temp - 550, 0, 255);
        var glowRgba = new Vec4f(incand[0], incand[1], incand[2], 255f);
        var light = _capi.World.BlockAccessor.GetLightRGBs(pos.X, pos.Y, pos.Z);
        var cam = _capi.World.Player.Entity.CameraPos;

        _capi.Render.GlDisableCullFace();
        prog.Use();
        prog.Uniform("rgbaAmbientIn", _capi.Render.AmbientColor);
        prog.Uniform("rgbaFogIn", _capi.Render.FogColor);
        prog.Uniform("fogMinIn", _capi.Render.FogMin);
        prog.Uniform("fogDensityIn", _capi.Render.FogDensity);
        prog.Uniform("dontWarpVertices", 1);
        prog.Uniform("addRenderFlags", 0);
        prog.Uniform("rgbaTint", ColorUtil.WhiteArgbVec);
        prog.Uniform("rgbaLightIn", light);
        prog.Uniform("rgbaGlowIn", glowRgba);
        prog.Uniform("extraGlow", extraGlow);
        prog.Uniform("tempGlowMode", 1);

        _modelMat.Identity()
            .Translate(pos.X - cam.X, pos.Y - cam.Y, pos.Z - cam.Z);
        prog.UniformMatrix("modelMatrix", _modelMat.Values);
        prog.UniformMatrix("viewMatrix", _capi.Render.CameraMatrixOriginf);
        prog.UniformMatrix("projectionMatrix", _capi.Render.CurrentProjectionMatrix);

        _capi.Render.RenderMultiTextureMesh(_meshRef, "tex");
        prog.Stop();
    }

    public void Dispose()
    {
        _meshRef?.Dispose();
        _meshRef = null;
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    }
}
