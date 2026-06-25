using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.ItemInsertionPipe;

public class PipeItemTransitRenderer : IRenderer, ITexPositionSource
{
    private const double MaxRenderDistanceSq = 96 * 96;
    private const double TravelDurationSeconds = 0.8;
    private const float PipeInnerDiameter = 0.34f;
    private const float PipeClearance = 0.88f;
    private const float MaxItemScale = 0.35f;
    private const float MaxBlockScale = 0.5f;
    private const float MinRenderScale = 0.035f;

    private readonly ICoreClientAPI capi;
    private readonly List<TransitItem> items = [];
    private readonly Dictionary<string, float> modelFitDimensionCache = [];
    private readonly float[] modelMatrix = Mat4f.Create();
    private CollectibleObject? nowTesselatingObj;
    private Shape? nowTesselatingShape;

    public static PipeItemTransitRenderer? Instance { get; set; }

    public double RenderOrder => 0.52;
    public int RenderRange => 96;

    public PipeItemTransitRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    public void AddTransit(ItemStack stack, List<Vec3d> points)
    {
        if (stack?.Collectible == null || points == null || points.Count < 2)
            return;

        if (capi.World.Player?.Entity?.CameraPos is Vec3d cameraPos
            && points[0].SquareDistanceTo(cameraPos) > MaxRenderDistanceSq)
            return;

        ItemStack renderStack = stack.Clone();
        renderStack.StackSize = 1;

        var slot = new DummySlot(renderStack);

        items.Add(new TransitItem
        {
            Slot = slot,
            Points = points,
            StartedMs = capi.ElapsedMilliseconds,
            ModelFitDimension = GetModelFitDimension(renderStack, slot)
        });
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (items.Count == 0 || capi.IsGamePaused)
            return;

        if (capi.World.Player?.Entity?.CameraPos is not Vec3d cameraPos)
            return;

        long now = capi.ElapsedMilliseconds;

        for (int i = items.Count - 1; i >= 0; i--)
        {
            TransitItem item = items[i];
            double progress = (now - item.StartedMs) / (TravelDurationSeconds * 1000.0);
            if (progress >= 1)
            {
                items.RemoveAt(i);
                continue;
            }

            Vec3d pos = Interpolate(item.Points, GameMath.Clamp(progress, 0, 1), out Vec3d direction);
            if (pos.SquareDistanceTo(cameraPos) > MaxRenderDistanceSq)
                continue;

            RenderItem(item, pos, direction, cameraPos, deltaTime);
        }
    }

    private void RenderItem(TransitItem item, Vec3d pos, Vec3d direction, Vec3d cameraPos, float deltaTime)
    {
        ItemRenderInfo renderInfo = capi.Render.GetItemStackRenderInfo(item.Slot, EnumItemRenderTarget.Ground, deltaTime);
        if (renderInfo?.ModelRef == null || renderInfo.ModelRef.Disposed || !renderInfo.ModelRef.Initialized)
            return;

        Mat4f.Identity(modelMatrix);
        Mat4f.Translate(
            modelMatrix,
            modelMatrix,
            (float)(pos.X - cameraPos.X),
            (float)(pos.Y - cameraPos.Y),
            (float)(pos.Z - cameraPos.Z));

        float yaw = (float)Math.Atan2(direction.X, direction.Z);
        Mat4f.RotateY(modelMatrix, modelMatrix, yaw);
        Mat4f.RotateX(modelMatrix, modelMatrix, GameMath.PIHALF);
        float scale = GetRenderScale(item.Slot.Itemstack, item.ModelFitDimension, renderInfo.Transform);
        Mat4f.Scale(modelMatrix, modelMatrix, scale, scale, scale);

        if (renderInfo.Transform != null)
            Mat4f.Mul(modelMatrix, modelMatrix, renderInfo.Transform.AsMatrix);

        IStandardShaderProgram shader = capi.Render.PreparedStandardShader(
            (int)Math.Floor(pos.X),
            (int)Math.Floor(pos.Y),
            (int)Math.Floor(pos.Z));

        shader.ModelMatrix = modelMatrix;
        shader.NormalShaded = renderInfo.NormalShaded ? 1 : 0;
        shader.AlphaTest = renderInfo.AlphaTest;
        shader.DamageEffect = renderInfo.DamageEffect;
        shader.OverlayOpacity = renderInfo.OverlayOpacity;

        capi.Render.RenderMultiTextureMesh(renderInfo.ModelRef, "tex", 0);
        shader.Stop();
    }

    public Size2i AtlasSize => capi.BlockTextureAtlas.Size;

    public TextureAtlasPosition this[string textureCode]
    {
        get
        {
            AssetLocation? assetLocation = null;

            if (nowTesselatingObj is Vintagestory.API.Common.Item item)
            {
                if (item.Textures.TryGetValue(textureCode, out CompositeTexture compositeTexture)
                    || item.Textures.TryGetValue("all", out compositeTexture))
                {
                    assetLocation = compositeTexture.Baked.BakedName;
                }
            }
            else if (nowTesselatingObj is Vintagestory.API.Common.Block block)
            {
                if (block.Textures.TryGetValue(textureCode, out CompositeTexture compositeTexture)
                    || block.Textures.TryGetValue("all", out compositeTexture))
                {
                    assetLocation = compositeTexture.Baked.BakedName;
                }
            }

            if (assetLocation == null && nowTesselatingShape != null)
                nowTesselatingShape.Textures.TryGetValue(textureCode, out assetLocation);

            if (assetLocation == null)
            {
                string domain = nowTesselatingObj?.Code?.Domain ?? "game";
                assetLocation = new AssetLocation(domain, "textures/item/" + textureCode);
            }

            return GetOrCreateTexPos(assetLocation);
        }
    }

    private TextureAtlasPosition? GetOrCreateTexPos(AssetLocation texturePath)
    {
        TextureAtlasPosition textureAtlasPosition = capi.BlockTextureAtlas[texturePath];
        if (textureAtlasPosition != null)
            return textureAtlasPosition;

        int pos = texturePath.Path.IndexOf("++", StringComparison.Ordinal);
        if (pos >= 0)
            texturePath.Path = texturePath.Path[..pos];

        IAsset asset = capi.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
        if (asset != null)
            capi.BlockTextureAtlas.GetOrInsertTexture(texturePath, out _, out textureAtlasPosition, null, 0.005f);

        return textureAtlasPosition;
    }

    private static float GetRenderScale(ItemStack stack, float modelFitDimension, ModelTransform transform)
    {
        float naturalScale = stack.Block != null ? MaxBlockScale : MaxItemScale;
        float unscaledRenderedSize = modelFitDimension * GetMaxTransformScale(transform);
        float allowedSize = PipeInnerDiameter * PipeClearance;

        if (unscaledRenderedSize <= 0)
            return naturalScale;

        float fitScale = allowedSize / unscaledRenderedSize;
        return GameMath.Clamp(Math.Min(naturalScale, fitScale), MinRenderScale, naturalScale);
    }

    private float GetModelFitDimension(ItemStack stack, ItemSlot slot)
    {
        string cacheKey = GetModelFitDimensionCacheKey(stack);
        if (modelFitDimensionCache.TryGetValue(cacheKey, out float cached))
            return cached;

        float fitDimension = GetFallbackFitDimension(stack);

        try
        {
            MeshData mesh = GenModelMesh(stack, slot);
            if (mesh?.xyz != null && mesh.VerticesCount > 0)
                fitDimension = GetMeshFitDimension(mesh);
        }
        catch (Exception e)
        {
            capi.World.Logger.Warning(
                "Failed to measure transit model size for {0}: {1}",
                stack.Collectible?.Code,
                e.Message);
        }
        finally
        {
            nowTesselatingObj = null;
            nowTesselatingShape = null;
            capi.TesselatorManager.ThreadDispose();
        }

        modelFitDimensionCache[cacheKey] = fitDimension;
        return fitDimension;
    }

    private MeshData GenModelMesh(ItemStack stack, ItemSlot slot)
    {
        if (stack.Collectible is IContainedMeshSource meshSource)
            return meshSource.GenMesh(slot, capi.BlockTextureAtlas, new BlockPos(0, 0, 0));

        if (stack.Class == EnumItemClass.Block)
            return capi.TesselatorManager.GetDefaultBlockMesh(stack.Block).Clone();

        nowTesselatingObj = stack.Collectible;
        nowTesselatingShape = stack.Item.Shape == null
            ? null
            : capi.TesselatorManager.GetCachedShape(stack.Item.Shape.Base);

        capi.Tesselator.TesselateItem(stack.Item, out MeshData mesh, this);
        return mesh;
    }

    private static string GetModelFitDimensionCacheKey(ItemStack stack)
    {
        return $"{stack.Class}:{stack.Collectible?.Id}:{stack.Collectible?.Code}";
    }

    private static float GetMeshFitDimension(MeshData mesh)
    {
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float minZ = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        float maxZ = float.MinValue;

        int vertexCount = Math.Min(mesh.VerticesCount, mesh.xyz.Length / 3);
        for (int vertex = 0; vertex < vertexCount; vertex++)
        {
            int index = vertex * 3;
            float x = mesh.xyz[index];
            float y = mesh.xyz[index + 1];
            float z = mesh.xyz[index + 2];

            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            minZ = Math.Min(minZ, z);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
            maxZ = Math.Max(maxZ, z);
        }

        float width = Math.Max(maxX - minX, 0f);
        float height = Math.Max(maxY - minY, 0f);
        float length = Math.Max(maxZ - minZ, 0f);
        float max = Math.Max(width, Math.Max(height, length));

        return max > 0 ? max : 1f;
    }

    private static float GetFallbackFitDimension(ItemStack stack)
    {
        Size3f? dimensions = stack.Collectible?.Dimensions;
        if (dimensions == null)
            return 1f;

        float width = Math.Max(dimensions.Width, 0f);
        float height = Math.Max(dimensions.Height, 0f);
        float length = Math.Max(dimensions.Length, 0f);
        float max = Math.Max(width, Math.Max(height, length));

        return max > 0 ? max : 1f;
    }

    private static float GetMaxTransformScale(ModelTransform transform)
    {
        if (transform == null)
            return 1f;

        float scale = Math.Max(Math.Abs(transform.ScaleXYZ.X), Math.Abs(transform.ScaleXYZ.Y));
        scale = Math.Max(scale, Math.Abs(transform.ScaleXYZ.Z));

        return scale > 0 ? scale : 1f;
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
        items.Clear();
    }

    private sealed class TransitItem
    {
        public DummySlot Slot = null!;
        public List<Vec3d> Points = null!;
        public long StartedMs;
        public float ModelFitDimension;
    }
}
