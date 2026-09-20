using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EFruitPress;

public class FruitPressLiquidRenderer : IRenderer
{
    private const int Sides = 48;
    private const float Cx = 20.39f / 16f;
    private const float Cz = -4.6f / 16f;
    private const float Radius = 9.0f / 16f;
    private const float YBottom = 10.0f / 16f;
    private const float YRange = 19.5f / 16f;

    private readonly ICoreClientAPI _capi;
    private readonly BlockEntityEFruitPress _be;
    private readonly float[] _modelMatrix = Mat4f.Create();
    private MeshRef? _mesh;
    private string? _meshKey;
    private int _texId;
    private Vec4f _tint = new(1, 1, 1, 0.55f);

    public double RenderOrder => 0.51;
    public int RenderRange => 64;

    public FruitPressLiquidRenderer(ICoreClientAPI capi, BlockEntityEFruitPress be)
    {
        _capi = capi;
        _be = be;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (_be.LiquidSlot == null || _be.LiquidSlot.Empty)
            return;
        var stack = _be.LiquidStack;
        if (stack?.Collectible == null)
            return;

        var fill = _be.LiquidCapacity > 0 ? _be.LiquidAmount / _be.LiquidCapacity : 0f;
        if (fill < 0.02f)
            return;
        fill = GameMath.Clamp(fill, 0.02f, 1f);

        if (!EnsureResources(stack))
            return;

        var cam = _capi.World.Player?.Entity?.CameraPos;
        if (cam == null)
            return;

        var pos = _be.Pos;
        var height = YRange * fill;
        var rotY = GetRotateY() * GameMath.DEG2RAD;

        Mat4f.Identity(_modelMatrix);
        Mat4f.Translate(_modelMatrix, _modelMatrix,
            (float)(pos.X + 0.5 - cam.X),
            (float)(pos.InternalY + 0.5 - cam.Y),
            (float)(pos.Z + 0.5 - cam.Z));
        Mat4f.RotateY(_modelMatrix, _modelMatrix, rotY);
        Mat4f.Translate(_modelMatrix, _modelMatrix, Cx - 0.5f, YBottom - 0.5f + height, Cz - 0.5f);
        Mat4f.Scale(_modelMatrix, _modelMatrix, Radius, 1f, Radius);

        _capi.Render.GlToggleBlend(true);
        _capi.Render.BindTexture2d(_texId);

        var prog = _capi.Render.StandardShader;
        prog.Use();
        prog.RgbaAmbientIn = new Vec3f(1, 1, 1);
        prog.RgbaFogIn = _capi.Render.FogColor;
        prog.FogMinIn = _capi.Render.FogMin;
        prog.FogDensityIn = _capi.Render.FogDensity;
        prog.RgbaLightIn = new Vec4f(1, 1, 1, 1);
        prog.RgbaGlowIn = new Vec4f(0, 0, 0, 0);
        prog.RgbaTint = _tint;
        prog.ExtraGlow = 0;
        prog.DontWarpVertices = 1;
        prog.AddRenderFlags = 0;
        prog.NormalShaded = 0;
        prog.AlphaTest = 0.01f;
        prog.ExtraZOffset = 0f;
        prog.Tex2D = _texId;
        prog.ModelMatrix = _modelMatrix;
        prog.ViewMatrix = _capi.Render.CameraMatrixOriginf;
        prog.ProjectionMatrix = _capi.Render.CurrentProjectionMatrix;
        _capi.Render.RenderMesh(_mesh);
        prog.Stop();
        _capi.Render.GlToggleBlend(false);
    }

    public void Dispose()
    {
        _mesh?.Dispose();
        _mesh = null;
    }

    private bool EnsureResources(ItemStack stack)
    {
        var key = stack.Collectible.Code.ToString();
        if (_mesh is { Disposed: false } && _texId > 0 && _meshKey == key)
            return true;

        if (!TryGetLiquidTile(stack, out var tile))
            return false;

        _texId = tile.atlasTextureId;
        _tint = GetJuiceTint(stack);
        var padU = (tile.x2 - tile.x1) * 0.12f;
        var padV = (tile.y2 - tile.y1) * 0.12f;
        var u0 = tile.x1 + padU;
        var v0 = tile.y1 + padV;
        var u1 = tile.x2 - padU;
        var v1 = tile.y2 - padV;

        _mesh?.Dispose();
        _mesh = _capi.Render.UploadMesh(CreateCylinder(u0, v0, u1, v1));
        _meshKey = key;
        return _mesh is { Disposed: false } && _texId > 0;
    }

    private Vec4f GetJuiceTint(ItemStack stack)
    {
        try
        {
            if (stack.Item != null)
            {
                var argb = stack.Item.GetRandomColor(_capi, stack);
                var r = ColorUtil.ColorR(argb) / 255f;
                var g = ColorUtil.ColorG(argb) / 255f;
                var b = ColorUtil.ColorB(argb) / 255f;
                if (r + g + b > 0.15f)
                    return new Vec4f(r, g, b, 0.38f);
            }
        }
        catch
        {
            // fallback below
        }

        var code = stack.Collectible.Code?.Path ?? "";
        if (code.Contains("redcurrant") || code.Contains("currant") || code.Contains("cranberry") || code.Contains("cherry"))
            return new Vec4f(0.72f, 0.08f, 0.14f, 0.38f);
        if (code.Contains("water"))
            return new Vec4f(0.27f, 0.55f, 0.9f, 0.35f);
        return new Vec4f(0.75f, 0.2f, 0.15f, 0.38f);
    }

    private bool TryGetLiquidTile(ItemStack stack, out TextureAtlasPosition tile)
    {
        tile = null!;

        var props = BlockLiquidContainerBase.GetContainableProps(stack);
        if (props?.Texture != null && TryGetAtlasPos(props.Texture, out tile))
            return true;

        var inCont = stack.Collectible.Attributes?["inContainerTexture"]?.AsObject<CompositeTexture>(null, stack.Collectible.Code.Domain);
        if (inCont != null && TryGetAtlasPos(inCont, out tile))
            return true;

        if (stack.Item?.Textures != null)
        {
            foreach (var name in new[] { "liquid", "contents", "all", "flow", "up" })
            {
                if (stack.Item.Textures.TryGetValue(name, out var ct) && TryGetAtlasPos(ct, out tile))
                    return true;
            }
        }

        var juiceType = stack.Collectible.Variant?["type"] ?? stack.Collectible.Code?.Path;
        if (!string.IsNullOrEmpty(juiceType))
        {
            var last = juiceType.Contains('-') ? juiceType[(juiceType.LastIndexOf('-') + 1)..] : juiceType;
            if (TryGetAtlasPos(new AssetLocation("game", "block/liquid/fruitjuice/" + last), out tile))
                return true;
            if (TryGetAtlasPos(new AssetLocation("survival", "block/liquid/fruitjuice/" + last), out tile))
                return true;
        }

        return TryGetAtlasPos(new AssetLocation("game", "block/liquid/water"), out tile);
    }

    private bool TryGetAtlasPos(CompositeTexture texture, out TextureAtlasPosition tile)
    {
        tile = null!;
        if (texture.Baked == null)
        {
            try { texture.RuntimeBake(_capi, _capi.BlockTextureAtlas); }
            catch { /* generated */ }
        }

        var loc = texture.Baked?.BakedName ?? texture.Base;
        return loc != null && TryGetAtlasPos(loc, out tile);
    }

    private bool TryGetAtlasPos(AssetLocation loc, out TextureAtlasPosition tile)
    {
        tile = _capi.BlockTextureAtlas[loc];
        if (IsSmallTile(tile))
            return true;

        _capi.BlockTextureAtlas.GetOrInsertTexture(loc, out _, out tile, null, 0.005f);
        return IsSmallTile(tile);
    }

    private static bool IsSmallTile(TextureAtlasPosition? tile)
        => tile != null && tile.x2 - tile.x1 < 0.05f && tile.y2 - tile.y1 < 0.05f;

    private float GetRotateY()
    {
        if (_be.Block?.Shape != null && Math.Abs(_be.Block.Shape.rotateY) > 0.01f)
            return _be.Block.Shape.rotateY;
        return _be.Block?.Variant?["side"] switch
        {
            "east" => 270,
            "south" => 180,
            "west" => 90,
            _ => 0
        };
    }

    private static MeshData CreateCylinder(float u0, float v0, float u1, float v1)
    {
        // Как бочка: только верхняя поверхность, без боковых стенок столба.
        var vCount = Sides + 1;
        var iCount = Sides * 3;
        var mesh = new MeshData(vCount, iCount, withNormals: false, withUv: true, withRgba: true, withFlags: false)
        {
            xyz = new float[vCount * 3],
            Uv = new float[vCount * 2],
            Rgba = new byte[vCount * 4],
            Indices = new int[iCount]
        };

        void Vert(int vi, float x, float y, float z, float u01, float v01)
        {
            mesh.xyz[vi * 3] = x;
            mesh.xyz[vi * 3 + 1] = y;
            mesh.xyz[vi * 3 + 2] = z;
            mesh.Uv[vi * 2] = u0 + (u1 - u0) * u01;
            mesh.Uv[vi * 2 + 1] = v0 + (v1 - v0) * v01;
            mesh.Rgba[vi * 4] = 255;
            mesh.Rgba[vi * 4 + 1] = 255;
            mesh.Rgba[vi * 4 + 2] = 255;
            mesh.Rgba[vi * 4 + 3] = 255;
        }

        Vert(0, 0f, 0f, 0f, 0.5f, 0.5f);
        for (var i = 0; i < Sides; i++)
        {
            var a = i * GameMath.TWOPI / Sides;
            var x = GameMath.Sin(a);
            var z = -GameMath.Cos(a);
            Vert(1 + i, x, 0f, z, 0.5f + 0.42f * x, 0.5f + 0.42f * z);
        }

        var ii = 0;
        for (var i = 0; i < Sides; i++)
        {
            var n = (i + 1) % Sides;
            mesh.Indices[ii++] = 0;
            mesh.Indices[ii++] = 1 + n;
            mesh.Indices[ii++] = 1 + i;
        }

        mesh.VerticesCount = vCount;
        mesh.IndicesCount = ii;
        return mesh;
    }
}
