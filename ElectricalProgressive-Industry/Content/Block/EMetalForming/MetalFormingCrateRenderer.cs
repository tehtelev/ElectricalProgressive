using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.EMetalForming;

/// <summary>
/// Куча во входном ящике (элемент Crate) и предмет в руке (DynPlate).
/// Ящик: внутренность 6.84×10.08×5.4 вокселя, пол Y=1.08, поворот 8°.
/// </summary>
public class MetalFormingCrateRenderer : IRenderer
{
    private const string CrateElementName = "Crate";
    private const string HandElementName = "DynPlate";

    // Crate.from / rotationOrigin из shape (воксели)
    private const float CrateFromX = 5.58f;
    private const float CrateFromY = 4.5f;
    private const float CrateFromZ = 10.86f;
    private const float CrateOrigX = 6.16f;
    private const float CrateOrigY = 4.5f;
    private const float CrateOrigZ = 10.86f;

    // Внутренность относительно Crate.from (воксели), с зазором от стенок
    private const float InnerMinX = -2.70f;
    private const float InnerMaxX = 3.78f;
    private const float InnerMinZ = -4.80f;
    private const float InnerMaxZ = 4.80f;
    private const float FloorY = 1.12f;

    // Слиток как ingotpile (3×2×7). Пластина 9×9, ящик внутри 6.84 — чуть уменьшаем.
    private const float CrateRotY = 8f;
    private const float IngotPitchX = 2.9f;
    private const float IngotScale = 0.85f;
    private const float PlateScale = 0.75f;

    // work-on 760 кадров: захват ~97, рука к ленте ~291, сброс ~330
    private const float PickupFrame = 97f;
    private const float ApproachBeltFrame = 205f;
    private const float DropOnBeltFrame = 330f;
    // верх роликов ~7.3 вокселя + зазор над лентой
    private const float BeltY = 8.5f / 16f;

    private readonly ICoreClientAPI _capi;
    private readonly BlockEntityEMetalForming _be;
    private readonly Matrixf _modelMat = new();
    private MultiTextureMeshRef? _meshRef;
    private string? _meshKey;

    public double RenderOrder => 0.51;
    public int RenderRange => 24;

    public MetalFormingCrateRenderer(ICoreClientAPI capi, BlockEntityEMetalForming be)
    {
        _capi = capi;
        _be = be;
    }

    public void UpdateMesh()
    {
        var stack = _be.InputSlot?.Itemstack;
        var key = stack?.Collectible?.Code?.ToString();
        if (key == _meshKey && (stack == null) == (_meshRef == null))
            return;

        _meshKey = key;
        _meshRef?.Dispose();
        _meshRef = null;

        if (stack?.Collectible == null)
            return;

        MeshData? mesh = null;
        try
        {
            if (stack.Class == EnumItemClass.Item && stack.Item != null)
                _capi.Tesselator.TesselateItem(stack.Item, out mesh);
            else if (stack.Block != null)
                mesh = _capi.TesselatorManager.GetDefaultBlockMesh(stack.Block)?.Clone();
        }
        catch
        {
            mesh = null;
        }

        if (mesh == null || mesh.VerticesCount <= 0)
            return;

        _meshRef = _capi.Render.UploadMultiTextureMesh(mesh);
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (_meshRef == null)
            return;

        var stack = _be.InputSlot?.Itemstack;
        if (stack?.Collectible == null)
            return;

        var animator = _be.GetAnimator();
        if (animator == null)
            return;

        var pos = _be.Pos;
        var cam = _capi.World.Player.Entity.CameraPos;
        var light = _capi.World.BlockAccessor.GetLightRGBs(pos.X, pos.Y, pos.Z);
        var rotY = _be.GetRotation();

        var temp = stack.Collectible.GetTemperature(_capi.World, stack);
        var extraGlow = GameMath.Clamp((int)temp - 550, 0, 255);
        var incand = ColorUtil.GetIncandescenceColorAsColor4f((int)temp);

        var prog = _capi.Render.StandardShader;
        prog.Use();
        prog.RgbaAmbientIn = _capi.Render.AmbientColor;
        prog.RgbaFogIn = _capi.Render.FogColor;
        prog.FogMinIn = _capi.Render.FogMin;
        prog.FogDensityIn = _capi.Render.FogDensity;
        prog.RgbaLightIn = light;
        prog.RgbaGlowIn = extraGlow > 0
            ? new Vec4f(incand[0], incand[1], incand[2], 1f)
            : new Vec4f(0, 0, 0, 0);
        prog.RgbaTint = ColorUtil.WhiteArgbVec;
        prog.ExtraGlow = extraGlow;
        prog.TempGlowMode = 0;
        prog.DontWarpVertices = 1;
        prog.AddRenderFlags = 0;
        prog.NormalShaded = 1;
        prog.AlphaTest = 0.05f;
        prog.ViewMatrix = _capi.Render.CameraMatrixOriginf;
        prog.ProjectionMatrix = _capi.Render.CurrentProjectionMatrix;

        _capi.Render.GlDisableCullFace();

        var isPlate = stack.Collectible.Code.Path.Contains("plate");
        var itemScale = isPlate ? PlateScale : IngotScale;
        var layerH = isPlate ? PlateScale : 2f * IngotScale;
        var frame = _be.GetWorkOnFrame();
        var held = frame >= PickupFrame;

        var crateCount = stack.StackSize;
        if (held && crateCount > 0)
            crateCount--;
        crateCount = Math.Min(crateCount, 8);

        for (var i = 0; i < crateCount; i++)
            RenderInCrate(prog, pos, cam, rotY, i, layerH, isPlate);

        if (held)
            RenderInHand(prog, animator, pos, cam, rotY, itemScale, frame);

        prog.Stop();
        _capi.Render.GlEnableCullFace();
    }

    private void RenderInCrate(IStandardShaderProgram prog,
        BlockPos pos, Vec3d cam, int rotY, int index, float layerHVoxels, bool isPlate)
    {
        float lx;
        float lz;
        float ly;
        if (isPlate)
        {
            ly = FloorY + index * layerHVoxels;
            lx = (InnerMinX + InnerMaxX) * 0.5f;
            lz = (InnerMinZ + InnerMaxZ) * 0.5f;
        }
        else
        {
            const int cols = 2;
            var layer = index / cols;
            var col = index % cols;
            ly = FloorY + layer * layerHVoxels;
            lx = (InnerMinX + InnerMaxX) * 0.5f + (col - 0.5f) * IngotPitchX;
            lz = (InnerMinZ + InnerMaxZ) * 0.5f;
        }

        var ox = (CrateFromX + lx - CrateOrigX) / 16f;
        var oy = (CrateFromY + ly - CrateOrigY) / 16f;
        var oz = (CrateFromZ + lz - CrateOrigZ) / 16f;

        BeginBlockMatrix(pos, cam, rotY);
        _modelMat.Translate(CrateOrigX / 16f, CrateOrigY / 16f, CrateOrigZ / 16f);
        _modelMat.RotateYDeg(CrateRotY);
        _modelMat.Translate(ox, oy, oz);
        var itemScale = isPlate ? PlateScale : IngotScale;
        if (itemScale != 1f)
            _modelMat.Scale(itemScale, itemScale, itemScale);
        _modelMat.Translate(-0.5f, 0f, -0.5f);

        prog.ModelMatrix = _modelMat.Values;
        _capi.Render.RenderMultiTextureMesh(_meshRef, "tex");
    }

    private void RenderInHand(IStandardShaderProgram prog, AnimatorBase animator,
        BlockPos pos, Vec3d cam, int rotY, float itemScale, float frame)
    {
        var pose = animator.GetPosebyName(HandElementName);
        if (pose?.AnimModelMatrix == null)
            return;

        if (frame >= DropOnBeltFrame)
        {
            RenderOnBelt(prog, pose, pos, cam, rotY, itemScale);
            return;
        }

        BeginBlockMatrix(pos, cam, rotY);
        _modelMat.Mul(pose.AnimModelMatrix);
        _modelMat.Translate(2f / 16f, 2f / 16f, 0.1f / 16f);
        _modelMat.RotateXDeg(90f);
        _modelMat.Scale(itemScale, itemScale, itemScale);
        _modelMat.Translate(-0.5f, 0f, -0.5f);

        // рука в анимации уходит ниже ленты — не даём предмету провалиться
        if (frame >= ApproachBeltFrame)
        {
            var beltCamY = (float)(pos.Y - cam.Y + BeltY);
            if (_modelMat.Values[13] < beltCamY)
                _modelMat.Values[13] = beltCamY;
        }

        prog.ModelMatrix = _modelMat.Values;
        _capi.Render.RenderMultiTextureMesh(_meshRef, "tex");
    }

    /// <summary>
    /// XZ как у DynPlate (та же матрица, что в руке), Y — высота ленты в мире.
    /// </summary>
    private void RenderOnBelt(IStandardShaderProgram prog, ElementPose pose,
        BlockPos pos, Vec3d cam, int rotY, float itemScale)
    {
        BeginBlockMatrix(pos, cam, rotY);
        _modelMat.Mul(pose.AnimModelMatrix);
        _modelMat.Translate(2f / 16f, 2f / 16f, 0.1f / 16f);
        var cx = _modelMat.Values[12];
        var cz = _modelMat.Values[14];

        _modelMat.Identity()
            .Translate(cx, (float)(pos.Y - cam.Y + BeltY), cz)
            .RotateYDeg(rotY)
            .Scale(itemScale, itemScale, itemScale)
            .Translate(-0.5f, 0f, -0.5f);

        prog.ModelMatrix = _modelMat.Values;
        _capi.Render.RenderMultiTextureMesh(_meshRef, "tex");
    }

    private void BeginBlockMatrix(BlockPos pos, Vec3d cam, int rotY)
    {
        _modelMat.Identity()
            .Translate(pos.X - cam.X, pos.Y - cam.Y, pos.Z - cam.Z)
            .Translate(0.5f, 0f, 0.5f)
            .RotateYDeg(rotY)
            .Translate(-0.5f, 0f, -0.5f);
    }

    public void Dispose()
    {
        _meshRef?.Dispose();
        _meshRef = null;
    }
}
