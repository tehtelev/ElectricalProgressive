using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
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
    private readonly Func<float> _getTemp;
    private readonly Func<ItemStack?>? _getStack;
    private MultiTextureMeshRef? _meshRef;
    private MeshRef? _outlineRef;
    private readonly Matrixf _modelMat = new();
    private readonly Vec3f _origin = new(0, 0, 0);
    private readonly Vec4f _outlineColor = new(1, 1, 1, 1);

    public double RenderOrder => HideWhenCold ? 0.6 : 0.5;
    public int RenderRange => 24;
    public bool HideWhenCold { get; set; }

    /// <summary>
    /// Заготовка наковальни: шейдер и контур как у механического молота.
    /// Пресс и домна этот флаг не ставят.
    /// </summary>
    public bool VoxelWorkItem { get; set; }

    public SmithingWorkItemRenderer(ICoreClientAPI capi, Func<BlockPos> getPos, Func<ItemStack?> getStack)
    {
        _capi = capi;
        _getPos = getPos;
        _getStack = getStack;
        _getTemp = () =>
        {
            var stack = getStack();
            if (stack?.Collectible == null) return 20f;
            return stack.Collectible.GetTemperature(capi.World, stack);
        };
    }

    public SmithingWorkItemRenderer(ICoreClientAPI capi, Func<BlockPos> getPos, Func<float> getTemp)
    {
        _capi = capi;
        _getPos = getPos;
        _getTemp = getTemp;
    }

    public void SetMesh(MeshData? mesh)
    {
        _meshRef?.Dispose();
        _meshRef = null;
        if (mesh != null && mesh.VerticesCount > 0)
            _meshRef = _capi.Render.UploadMultiTextureMesh(mesh);
    }

    public void SetOutline(MeshData? mesh)
    {
        _outlineRef?.Dispose();
        _outlineRef = null;
        if (mesh != null && mesh.VerticesCount > 0)
            _outlineRef = _capi.Render.UploadMesh(mesh);
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (VoxelWorkItem && stage == EnumRenderStage.AfterFinalComposition)
        {
            RenderOutline();
            return;
        }

        if (stage != EnumRenderStage.Opaque || _meshRef == null)
            return;

        if (_getStack != null && _getStack()?.Collectible == null)
            return;

        var prog = _capi.ModLoader.GetModSystem<SurvivalCoreSystem>()?.smithingWorkItemShader;
        if (prog == null)
            return;

        var pos = _getPos();
        float temp = _getTemp();
        if (HideWhenCold && temp < 400)
            return;
        float[] incand = ColorUtil.GetIncandescenceColorAsColor4f((int)temp);
        int extraGlow;
        Vec4f glowRgba;
        int dontWarp;
        int tempGlowMode;
        if (VoxelWorkItem)
        {
            extraGlow = GameMath.Clamp((int)((temp - 550f) / 2f), 0, 255);
            glowRgba = new Vec4f(incand[0], incand[1], incand[2], extraGlow / 255f);
            dontWarp = 0;
            tempGlowMode = 0;
        }
        else
        {
            extraGlow = GameMath.Clamp((int)temp - 550, 0, 255);
            glowRgba = new Vec4f(incand[0], incand[1], incand[2], 255f);
            dontWarp = 1;
            tempGlowMode = 1;
        }

        var light = _capi.World.BlockAccessor.GetLightRGBs(pos.X, pos.Y, pos.Z);
        var cam = _capi.World.Player.Entity.CameraPos;

        _capi.Render.GlDisableCullFace();
        if (HideWhenCold)
            _capi.Render.GLDepthMask(false);
        prog.Use();
        prog.Uniform("rgbaAmbientIn", _capi.Render.AmbientColor);
        prog.Uniform("rgbaFogIn", _capi.Render.FogColor);
        prog.Uniform("fogMinIn", _capi.Render.FogMin);
        prog.Uniform("fogDensityIn", _capi.Render.FogDensity);
        prog.Uniform("dontWarpVertices", dontWarp);
        prog.Uniform("addRenderFlags", 0);
        prog.Uniform("rgbaTint", ColorUtil.WhiteArgbVec);
        prog.Uniform("rgbaLightIn", light);
        prog.Uniform("rgbaGlowIn", glowRgba);
        prog.Uniform("extraGlow", extraGlow);
        prog.Uniform("tempGlowMode", tempGlowMode);

        _modelMat.Identity()
            .Translate(pos.X - cam.X, pos.Y - cam.Y, pos.Z - cam.Z);
        prog.UniformMatrix("modelMatrix", _modelMat.Values);
        prog.UniformMatrix("viewMatrix", _capi.Render.CameraMatrixOriginf);
        prog.UniformMatrix("projectionMatrix", _capi.Render.CurrentProjectionMatrix);

        _capi.Render.RenderMultiTextureMesh(_meshRef, "tex");
        prog.Stop();
        if (HideWhenCold)
            _capi.Render.GLDepthMask(true);
    }

    private void RenderOutline()
    {
        if (_outlineRef == null || _capi.HideGuis)
            return;

        var pos = _getPos();
        var player = _capi.World.Player?.Entity;
        if (player == null)
            return;

        var cam = player.CameraPos;
        var distance = (float)Math.Sqrt(player.Pos.SquareDistanceTo(pos.X, pos.Y, pos.Z));
        _outlineColor.A = 1f - GameMath.Clamp(distance / 5f - 1f, 0f, 1f);

        var rpi = _capi.Render;
        var lineWidth = 2f * _capi.Settings.Float["wireframethickness"];
        rpi.LineWidth = lineWidth;
        rpi.GLEnableDepthTest();
        rpi.GlToggleBlend(true);

        var prog = rpi.GetEngineShader(EnumShaderProgram.Wireframe);
        prog.Use();
        prog.Uniform("origin", _origin);
        prog.UniformMatrix("projectionMatrix", rpi.CurrentProjectionMatrix);
        _modelMat.Set(rpi.CameraMatrixOriginf).Translate(pos.X - cam.X, pos.Y - cam.Y, pos.Z - cam.Z);
        prog.UniformMatrix("modelViewMatrix", _modelMat.Values);
        prog.Uniform("colorIn", _outlineColor);
        rpi.RenderMesh(_outlineRef);
        prog.Stop();

        if (lineWidth != 1.6f)
            rpi.LineWidth = 1.6f;

        if (RuntimeEnv.OS != OS.Mac)
            return;

        rpi.GLDisableDepthTest();
        rpi.GlToggleBlend(false);
    }

    public void Dispose()
    {
        _meshRef?.Dispose();
        _meshRef = null;
        _outlineRef?.Dispose();
        _outlineRef = null;
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        if (VoxelWorkItem)
            _capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterFinalComposition);
    }
}

