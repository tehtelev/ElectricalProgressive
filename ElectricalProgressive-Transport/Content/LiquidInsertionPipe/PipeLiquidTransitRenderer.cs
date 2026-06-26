using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.LiquidInsertionPipe;

public class PipeLiquidTransitRenderer : IRenderer
{
    private const double MaxRenderDistanceSq = 96 * 96;
    private const double TravelDurationSeconds = 2.2;
    private const float PipeInnerDiameter = 0.34f;
    private const float LiquidRadius = PipeInnerDiameter * 0.43f;
    private const float LiquidSegmentLength = 0.72f;
    private const float MinSegmentLength = 0.18f;
    private const float Alpha = 0.82f;

    private readonly ICoreClientAPI capi;
    private readonly List<TransitLiquid> liquids = [];
    private readonly Dictionary<string, Vec4f> colorCache = [];
    private readonly Dictionary<string, MeshRef> meshCache = [];
    private readonly float[] modelMatrix = Mat4f.Create();
    private int whiteTextureId;

    public static PipeLiquidTransitRenderer? Instance { get; set; }

    public double RenderOrder => 0.54;
    public int RenderRange => 96;

    public PipeLiquidTransitRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    public void AddTransit(ItemStack liquidStack, List<Vec3d> points, float litres)
    {
        if (liquidStack?.Collectible == null || points == null || points.Count < 2 || litres <= 0)
            return;

        if (capi.World.Player?.Entity?.CameraPos is Vec3d cameraPos
            && points[0].SquareDistanceTo(cameraPos) > MaxRenderDistanceSq)
        {
            return;
        }

        ItemStack renderStack = liquidStack.Clone();
        renderStack.StackSize = 1;
        string liquidKey = GetLiquidColorKey(renderStack);
        LiquidTexture texture = GetLiquidTexture(renderStack, liquidKey);

        liquids.Add(new TransitLiquid
        {
            Points = points,
            StartedMs = capi.ElapsedMilliseconds,
            MeshKey = texture.MeshKey,
            TextureId = texture.TextureId,
            Color = GetLiquidColor(renderStack),
            TexturePosition = texture.TexturePosition,
            Length = GameMath.Clamp(LiquidSegmentLength * (0.65f + litres), MinSegmentLength, LiquidSegmentLength)
        });
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (liquids.Count == 0 || capi.IsGamePaused)
            return;

        if (capi.World.Player?.Entity?.CameraPos is not Vec3d cameraPos)
            return;

        long now = capi.ElapsedMilliseconds;

        for (int i = liquids.Count - 1; i >= 0; i--)
        {
            TransitLiquid liquid = liquids[i];
            double progress = (now - liquid.StartedMs) / (TravelDurationSeconds * 1000.0);
            if (progress >= 1)
            {
                liquids.RemoveAt(i);
                continue;
            }

            Vec3d pos = Interpolate(liquid.Points, GameMath.Clamp(progress, 0, 1), out Vec3d direction);
            if (pos.SquareDistanceTo(cameraPos) > MaxRenderDistanceSq)
                continue;

            MeshRef mesh = GetMeshRef(liquid);
            if (mesh.Disposed)
                continue;

            RenderLiquid(mesh, liquid, pos, direction, cameraPos);
        }
    }

    private void RenderLiquid(MeshRef mesh, TransitLiquid liquid, Vec3d pos, Vec3d direction, Vec3d cameraPos)
    {
        Mat4f.Identity(modelMatrix);
        Mat4f.Translate(
            modelMatrix,
            modelMatrix,
            (float)(pos.X - cameraPos.X),
            (float)(pos.Y - cameraPos.Y),
            (float)(pos.Z - cameraPos.Z));

        ApplyDirectionRotation(direction);
        Mat4f.Scale(modelMatrix, modelMatrix, LiquidRadius, LiquidRadius, liquid.Length);

        IStandardShaderProgram shader = capi.Render.PreparedStandardShader(
            (int)Math.Floor(pos.X),
            (int)Math.Floor(pos.Y),
            (int)Math.Floor(pos.Z),
            liquid.Color);

        shader.ModelMatrix = modelMatrix;
        shader.RgbaTint = liquid.Color;
        shader.Tex2D = liquid.TextureId;
        shader.AlphaTest = 0.01f;
        shader.NormalShaded = 0;

        capi.Render.RenderMesh(mesh);
        shader.Stop();
    }

    private void ApplyDirectionRotation(Vec3d direction)
    {
        float yaw = (float)Math.Atan2(direction.X, direction.Z);
        float horizontal = GameMath.Sqrt((float)(direction.X * direction.X + direction.Z * direction.Z));
        float pitch = -(float)Math.Atan2(direction.Y, horizontal);

        Mat4f.RotateY(modelMatrix, modelMatrix, yaw);
        Mat4f.RotateX(modelMatrix, modelMatrix, pitch);
    }

    private MeshRef GetMeshRef(TransitLiquid liquid)
    {
        if (!meshCache.TryGetValue(liquid.MeshKey, out MeshRef mesh) || mesh.Disposed)
        {
            mesh = capi.Render.UploadMesh(CreateLiquidSegmentMesh(liquid.TexturePosition, liquid.Color));
            meshCache[liquid.MeshKey] = mesh;
        }

        return mesh;
    }

    private int GetWhiteTextureId()
    {
        if (whiteTextureId == 0)
            whiteTextureId = capi.Render.LoadTextureFromRgba([unchecked((int)0xffffffff)], 1, 1, false, 0);

        return whiteTextureId;
    }

    private LiquidTexture GetLiquidTexture(ItemStack liquidStack, string liquidKey)
    {
        TextureAtlasPosition? texPos = null;

        if (liquidStack.Block != null)
            texPos = GetTextureFromCompositeTextures(liquidStack.Block.Textures);

        if (texPos == null && liquidStack.Item != null)
            texPos = GetTextureFromCompositeTextures(liquidStack.Item.Textures);

        if (texPos != null)
        {
            string meshKey = $"{liquidKey}:{texPos.atlasTextureId}:{texPos.x1}:{texPos.y1}:{texPos.x2}:{texPos.y2}";
            return new LiquidTexture(meshKey, texPos.atlasTextureId, texPos);
        }

        return new LiquidTexture($"{liquidKey}:white", GetWhiteTextureId(), null);
    }

    private TextureAtlasPosition? GetTextureFromCompositeTextures(IDictionary<string, CompositeTexture>? textures)
    {
        if (textures == null || textures.Count == 0)
            return null;

        string[] preferredTextureCodes =
        [
            "liquid", "contents", "content", "all", "still", "water", "lava", "oil",
            "top", "up", "side", "north", "east", "south", "west"
        ];

        foreach (string textureCode in preferredTextureCodes)
        {
            if (textures.TryGetValue(textureCode, out CompositeTexture texture)
                && TryGetTextureAtlasPosition(texture, out TextureAtlasPosition texPos))
            {
                return texPos;
            }
        }

        foreach (CompositeTexture texture in textures.Values)
        {
            if (TryGetTextureAtlasPosition(texture, out TextureAtlasPosition texPos))
                return texPos;
        }

        return null;
    }

    private bool TryGetTextureAtlasPosition(CompositeTexture texture, out TextureAtlasPosition texPos)
    {
        texPos = null!;

        if (texture == null)
            return false;

        if (texture.Baked == null)
        {
            try
            {
                texture.RuntimeBake(capi, capi.BlockTextureAtlas);
            }
            catch
            {
                // Some generated liquid textures cannot be baked again at render time.
            }
        }

        AssetLocation? texturePath = texture.Baked?.BakedName ?? texture.Base;
        if (texturePath == null)
            return false;

        texPos = capi.BlockTextureAtlas[texturePath];
        if (texPos != null)
            return true;

        AssetLocation lookupPath = texturePath.Clone();
        int pos = lookupPath.Path.IndexOf("++", StringComparison.Ordinal);
        if (pos >= 0)
            lookupPath.Path = lookupPath.Path[..pos];

        IAsset asset = capi.Assets.TryGet(lookupPath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
        if (asset == null)
            return false;

        capi.BlockTextureAtlas.GetOrInsertTexture(lookupPath, out _, out texPos, null, 0.005f);
        return texPos != null;
    }

    private Vec4f GetLiquidColor(ItemStack liquidStack)
    {
        string key = GetLiquidColorKey(liquidStack);
        if (colorCache.TryGetValue(key, out Vec4f cached))
            return cached;

        string code = liquidStack.Collectible?.Code?.ToString() ?? "";
        Vec4f color;
        if (!TryGetKnownLiquidColor(code, out color) && !TryGetStackColor(liquidStack, out color))
        {
            int hash = code.GetHashCode();
            int r = 80 + Math.Abs(hash & 0x7f);
            int g = 90 + Math.Abs((hash >> 8) & 0x7f);
            int b = 110 + Math.Abs((hash >> 16) & 0x7f);
            color = new Vec4f(r / 255f, g / 255f, b / 255f, Alpha);
        }

        colorCache[key] = color;
        return color;
    }

    private static string GetLiquidColorKey(ItemStack liquidStack)
    {
        return liquidStack.Collectible?.Code?.ToString() ?? "unknown";
    }

    private bool TryGetKnownLiquidColor(string code, out Vec4f color)
    {
        if (code.Contains("water", StringComparison.OrdinalIgnoreCase))
        {
            color = new Vec4f(70 / 255f, 140 / 255f, 230 / 255f, Alpha);
            return true;
        }

        if (code.Contains("lava", StringComparison.OrdinalIgnoreCase) || code.Contains("metal", StringComparison.OrdinalIgnoreCase))
        {
            color = new Vec4f(255 / 255f, 120 / 255f, 30 / 255f, Alpha);
            return true;
        }

        if (code.Contains("oil", StringComparison.OrdinalIgnoreCase))
        {
            color = new Vec4f(40 / 255f, 38 / 255f, 34 / 255f, Alpha);
            return true;
        }

        color = null!;
        return false;
    }

    private bool TryGetStackColor(ItemStack liquidStack, out Vec4f color)
    {
        try
        {
            int argb;
            if (liquidStack.Block != null)
            {
                argb = liquidStack.Block.GetRandomColor(capi, new BlockPos(0, 0, 0), BlockFacing.UP);
                color = new Vec4f(ColorUtil.ColorR(argb) / 255f, ColorUtil.ColorG(argb) / 255f, ColorUtil.ColorB(argb) / 255f, Alpha);
                return true;
            }

            if (liquidStack.Item != null)
            {
                argb = liquidStack.Item.GetRandomColor(capi, liquidStack);
                color = new Vec4f(ColorUtil.ColorR(argb) / 255f, ColorUtil.ColorG(argb) / 255f, ColorUtil.ColorB(argb) / 255f, Alpha);
                return true;
            }
        }
        catch
        {
            // Fall through to deterministic fallback color.
        }

        color = null!;
        return false;
    }

    private static MeshData CreateLiquidSegmentMesh(TextureAtlasPosition? texPos, Vec4f color)
    {
        const int sides = 16;
        const int rings = 13;
        int vertexCount = sides * rings + 2;
        int indexCount = sides * rings * 6;
        var mesh = new MeshData(vertexCount, indexCount, withNormals: false, withUv: true, withRgba: true, withFlags: false);

        mesh.xyz = new float[vertexCount * 3];
        mesh.Uv = new float[vertexCount * 2];
        mesh.Indices = new int[indexCount];

        for (int ring = 0; ring < rings; ring++)
        {
            float t = (ring + 1f) / (rings + 1f);
            float z = GameMath.Lerp(-0.5f, 0.5f, t);
            float radius = GetSlugRadius(t);

            for (int side = 0; side < sides; side++)
            {
                float angle = GameMath.TWOPI * side / sides;
                int vertex = ring * sides + side;
                int xyzIndex = vertex * 3;
                int uvIndex = vertex * 2;

                mesh.xyz[xyzIndex] = GameMath.Cos(angle) * radius;
                mesh.xyz[xyzIndex + 1] = GameMath.Sin(angle) * radius;
                mesh.xyz[xyzIndex + 2] = z;

                float u = texPos == null ? side / (float)sides : GameMath.Lerp(texPos.x1, texPos.x2, side / (float)sides);
                mesh.Uv[uvIndex] = u;
                mesh.Uv[uvIndex + 1] = texPos == null ? t : GameMath.Lerp(texPos.y1, texPos.y2, t);
            }
        }

        int bottomCenter = sides * rings;
        int topCenter = bottomCenter + 1;
        mesh.xyz[bottomCenter * 3 + 2] = -0.5f;
        mesh.xyz[topCenter * 3 + 2] = 0.5f;
        mesh.Uv[bottomCenter * 2] = texPos == null ? 0.5f : (texPos.x1 + texPos.x2) * 0.5f;
        mesh.Uv[bottomCenter * 2 + 1] = texPos == null ? 0.5f : (texPos.y1 + texPos.y2) * 0.5f;
        mesh.Uv[topCenter * 2] = mesh.Uv[bottomCenter * 2];
        mesh.Uv[topCenter * 2 + 1] = mesh.Uv[bottomCenter * 2 + 1];

        int index = 0;
        for (int side = 0; side < sides; side++)
        {
            int next = (side + 1) % sides;

            mesh.Indices[index++] = bottomCenter;
            mesh.Indices[index++] = next;
            mesh.Indices[index++] = side;

            mesh.Indices[index++] = topCenter;
            mesh.Indices[index++] = (rings - 1) * sides + side;
            mesh.Indices[index++] = (rings - 1) * sides + next;
        }

        for (int ring = 0; ring < rings - 1; ring++)
        {
            int ringStart = ring * sides;
            int nextRingStart = (ring + 1) * sides;

            for (int side = 0; side < sides; side++)
            {
                int next = (side + 1) % sides;
                int bottom0 = ringStart + side;
                int bottom1 = ringStart + next;
                int top0 = nextRingStart + side;
                int top1 = nextRingStart + next;

                mesh.Indices[index++] = bottom0;
                mesh.Indices[index++] = bottom1;
                mesh.Indices[index++] = top1;
                mesh.Indices[index++] = bottom0;
                mesh.Indices[index++] = top1;
                mesh.Indices[index++] = top0;
            }
        }

        mesh.Rgba = new byte[vertexCount * 4];
        byte a = FloatColorToByte(color.A);
        for (int vertex = 0; vertex < vertexCount; vertex++)
        {
            float shade = 1f;
            if (vertex < sides * rings)
            {
                int side = vertex % sides;
                float angle = GameMath.TWOPI * side / sides;
                shade = 0.78f + 0.22f * Math.Max(0f, GameMath.Sin(angle) * 0.75f + GameMath.Cos(angle) * 0.25f);
            }

            int i = vertex * 4;
            mesh.Rgba[i] = FloatColorToByte(color.R * shade);
            mesh.Rgba[i + 1] = FloatColorToByte(color.G * shade);
            mesh.Rgba[i + 2] = FloatColorToByte(color.B * shade);
            mesh.Rgba[i + 3] = a;
        }

        mesh.VerticesCount = vertexCount;
        mesh.IndicesCount = index;
        return mesh;
    }

    private static float GetSlugRadius(float t)
    {
        const float capLength = 0.24f;
        float radius = 1f;

        if (t < capLength)
        {
            float n = GameMath.Clamp(t / capLength, 0f, 1f);
            radius = GameMath.Sqrt(Math.Max(0f, 1f - (1f - n) * (1f - n)));
        }
        else if (t > 1f - capLength)
        {
            float n = GameMath.Clamp((1f - t) / capLength, 0f, 1f);
            radius = GameMath.Sqrt(Math.Max(0f, 1f - (1f - n) * (1f - n)));
        }

        return radius * (0.96f + 0.04f * GameMath.Sin(GameMath.PI * t));
    }

    private static byte FloatColorToByte(float value)
    {
        return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(value * 255f)));
    }

    private static Vec3d Interpolate(List<Vec3d> points, double progress, out Vec3d direction)
    {
        double segmentProgress = progress * (points.Count - 1);
        int index = Math.Min((int)segmentProgress, points.Count - 2);
        double local = segmentProgress - index;

        Vec3d a = points[index];
        Vec3d b = points[index + 1];

        direction = b.SubCopy(a);
        if (direction.LengthSq() < 0.0001)
            direction.Set(0, 0, 1);
        else
            direction.Normalize();

        return new Vec3d(
            GameMath.Lerp(a.X, b.X, local),
            GameMath.Lerp(a.Y, b.Y, local),
            GameMath.Lerp(a.Z, b.Z, local));
    }

    public void Dispose()
    {
        liquids.Clear();
        foreach (MeshRef mesh in meshCache.Values)
            mesh.Dispose();
        meshCache.Clear();

        if (whiteTextureId != 0)
        {
            capi.Render.GLDeleteTexture(whiteTextureId);
            whiteTextureId = 0;
        }
    }

    private sealed class TransitLiquid
    {
        public List<Vec3d> Points = null!;
        public long StartedMs;
        public string MeshKey = "unknown";
        public int TextureId;
        public TextureAtlasPosition? TexturePosition;
        public Vec4f Color = null!;
        public float Length;
    }

    private readonly struct LiquidTexture
    {
        public readonly string MeshKey;
        public readonly int TextureId;
        public readonly TextureAtlasPosition? TexturePosition;

        public LiquidTexture(string meshKey, int textureId, TextureAtlasPosition? texturePosition)
        {
            MeshKey = meshKey;
            TextureId = textureId;
            TexturePosition = texturePosition;
        }
    }
}
