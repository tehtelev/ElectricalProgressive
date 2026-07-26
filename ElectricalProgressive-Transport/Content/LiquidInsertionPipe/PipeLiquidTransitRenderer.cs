using System;
using System.Collections.Generic;
using ElectricalProgressive.Content;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.LiquidInsertionPipe;

public class PipeLiquidTransitRenderer : IRenderer
{
    private const double MaxRenderDistanceSq = 96 * 96;
    private const long ActiveFlowDurationMs = 1400;
    private const double MaxPipeSegmentLengthSq = 1.05 * 1.05;
    private const float PipeArmFullReach = 1.0051f;
    private const float PipeInnerDiameter = 0.49f;
    private const float LiquidRadius = PipeInnerDiameter * 0.5f;
    private const float FlowUvTilesPerBlock = 1.35f;
    private const float FlowUvSpeed = 1.25f;
    private const float EndpointSlopeLength = 0.2f;
    private const int FlowAnimationFrames = 24;
    private const int CylinderSides = 24;
    private const int CylinderRings = 6;
    private const int MaxLiquidLanes = 4;
    private const float LaneWallClearance = 0.01f;
    private const float Alpha = 0.9f;

    private readonly ICoreClientAPI capi;
    private readonly List<ActivePipeFlow> activeFlows = [];
    private readonly Dictionary<string, Vec4f> colorCache = [];
    private readonly Dictionary<string, MeshRef?[]> meshCache = [];
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

        List<FilledPipeSegment> segments = BuildFilledPipeSegments(points);
        if (segments.Count == 0)
            return;

        List<BlockPos> pathBlocks = CollectPathBlocks(points);

        string liquidKey = GetLiquidColorKey(renderStack);
        LiquidTexture texture = GetLiquidTexture(renderStack, liquidKey);
        Vec4f color = GetLiquidColor(renderStack);

        long now = capi.ElapsedMilliseconds;
        string flowKey = BuildFlowKey(liquidKey, points);
        ActivePipeFlow? existing = FindActiveFlow(flowKey);
        if (existing != null)
        {
            existing.ExpiresMs = now + ActiveFlowDurationMs;
            existing.Segments = segments;
            existing.PathBlocks = pathBlocks;
            existing.MeshKey = texture.MeshKey;
            existing.TextureId = texture.TextureId;
            existing.TexturePosition = texture.TexturePosition;
            existing.Color = color;
            return;
        }

        activeFlows.Add(new ActivePipeFlow
        {
            FlowKey = flowKey,
            ExpiresMs = now + ActiveFlowDurationMs,
            FlowStartedMs = now,
            Segments = segments,
            PathBlocks = pathBlocks,
            MeshKey = texture.MeshKey,
            TextureId = texture.TextureId,
            TexturePosition = texture.TexturePosition,
            Color = color
        });
    }

    /// <summary>
    /// Immediately drop liquid visuals that pass through a broken/removed pipe cell.
    /// </summary>
    public void CancelTransitsThrough(BlockPos brokenPos)
    {
        if (brokenPos == null || activeFlows.Count == 0)
            return;

        for (int i = activeFlows.Count - 1; i >= 0; i--)
        {
            if (FlowTouchesBlock(activeFlows[i], brokenPos))
                activeFlows.RemoveAt(i);
        }
    }

    private static List<BlockPos> CollectPathBlocks(List<Vec3d> points)
    {
        var list = new List<BlockPos>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            Vec3d p = points[i];
            var pos = new BlockPos(
                (int)Math.Floor(p.X),
                (int)Math.Floor(p.Y),
                (int)Math.Floor(p.Z));
            if (list.Count == 0 || !list[list.Count - 1].Equals(pos))
                list.Add(pos);
        }

        return list;
    }

    private static bool FlowTouchesBlock(ActivePipeFlow flow, BlockPos pos)
    {
        if (flow.PathBlocks != null)
        {
            for (int i = 0; i < flow.PathBlocks.Count; i++)
            {
                if (flow.PathBlocks[i].Equals(pos))
                    return true;
            }
        }

        // Fallback for flows without path metadata.
        if (flow.Segments == null)
            return false;

        foreach (FilledPipeSegment segment in flow.Segments)
        {
            if (PointInBlock(segment.Center, pos))
                return true;
        }

        return false;
    }

    private static bool PointInBlock(Vec3d point, BlockPos pos)
        => (int)Math.Floor(point.X) == pos.X
           && (int)Math.Floor(point.Y) == pos.Y
           && (int)Math.Floor(point.Z) == pos.Z;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (activeFlows.Count == 0 || capi.IsGamePaused)
            return;

        if (capi.World.Player?.Entity?.CameraPos is not Vec3d cameraPos)
            return;

        long now = capi.ElapsedMilliseconds;

        for (int i = activeFlows.Count - 1; i >= 0; i--)
        {
            ActivePipeFlow flow = activeFlows[i];
            if (now >= flow.ExpiresMs)
            {
                activeFlows.RemoveAt(i);
                continue;
            }

            foreach (FilledPipeSegment segment in flow.Segments)
            {
                if (segment.Center.SquareDistanceTo(cameraPos) > MaxRenderDistanceSq)
                    continue;

                if (!TryGetSegmentLane(flow, segment, now, out int laneIndex, out int laneCount))
                    continue;

                int frame = GetFlowFrame(flow, segment, now);
                MeshRef mesh = GetMeshRef(flow, frame, segment.MeshKind);
                if (mesh.Disposed)
                    continue;

                RenderLiquidSegment(mesh, flow, segment, cameraPos, laneIndex, laneCount);
            }
        }
    }

    private void RenderLiquidSegment(
        MeshRef mesh,
        ActivePipeFlow flow,
        FilledPipeSegment segment,
        Vec3d cameraPos,
        int laneIndex,
        int laneCount)
    {
        float laneRadius = GetLaneRadius(laneCount);
        Vec3d laneOffset = GetLaneOffset(segment.Direction, laneIndex, laneCount, laneRadius);

        Mat4f.Identity(modelMatrix);
        Mat4f.Translate(
            modelMatrix,
            modelMatrix,
            (float)(segment.Center.X + laneOffset.X - cameraPos.X),
            (float)(segment.Center.Y + laneOffset.Y - cameraPos.Y),
            (float)(segment.Center.Z + laneOffset.Z - cameraPos.Z));

        ApplyDirectionRotation(segment.Direction);
        Mat4f.Scale(modelMatrix, modelMatrix, laneRadius, laneRadius, segment.Length);

        IStandardShaderProgram shader = capi.Render.PreparedStandardShader(
            (int)Math.Floor(segment.Center.X),
            (int)Math.Floor(segment.Center.Y),
            (int)Math.Floor(segment.Center.Z),
            flow.Color);

        shader.ModelMatrix = modelMatrix;
        shader.RgbaTint = flow.Color;
        shader.Tex2D = flow.TextureId;
        shader.AlphaTest = 0.01f;
        shader.NormalShaded = 0;

        capi.Render.RenderMesh(mesh);
        shader.Stop();
    }

    private bool TryGetSegmentLane(ActivePipeFlow currentFlow, FilledPipeSegment segment, long now, out int laneIndex, out int laneCount)
    {
        var flowKeys = new List<string>();

        foreach (ActivePipeFlow flow in activeFlows)
        {
            if (now >= flow.ExpiresMs || !FlowTouchesSegment(flow, segment.SegmentKey))
                continue;

            if (!flowKeys.Contains(flow.FlowKey))
                flowKeys.Add(flow.FlowKey);
        }

        flowKeys.Sort(StringComparer.Ordinal);
        laneIndex = flowKeys.IndexOf(currentFlow.FlowKey);
        laneCount = Math.Min(flowKeys.Count, MaxLiquidLanes);

        return laneIndex >= 0 && laneIndex < laneCount;
    }

    private static bool FlowTouchesSegment(ActivePipeFlow flow, string segmentKey)
    {
        foreach (FilledPipeSegment segment in flow.Segments)
        {
            if (segment.SegmentKey == segmentKey)
                return true;
        }

        return false;
    }

    private static float GetLaneRadius(int laneCount)
    {
        return laneCount switch
        {
            <= 1 => LiquidRadius,
            2 => LiquidRadius * 0.44f,
            3 => LiquidRadius * 0.36f,
            _ => LiquidRadius * 0.32f
        };
    }

    private static Vec3d GetLaneOffset(Vec3d direction, int laneIndex, int laneCount, float laneRadius)
    {
        if (laneCount <= 1)
            return new Vec3d();

        Vec3d axis = Normalize(direction);
        Vec3d reference = Math.Abs(axis.Y) < 0.85 ? new Vec3d(0, 1, 0) : new Vec3d(1, 0, 0);
        Vec3d right = Normalize(Cross(axis, reference));
        Vec3d up = Normalize(Cross(right, axis));

        double angle = GameMath.TWOPI * laneIndex / laneCount;
        double distance = Math.Max(0, LiquidRadius - laneRadius - LaneWallClearance);

        return new Vec3d(
            (right.X * Math.Cos(angle) + up.X * Math.Sin(angle)) * distance,
            (right.Y * Math.Cos(angle) + up.Y * Math.Sin(angle)) * distance,
            (right.Z * Math.Cos(angle) + up.Z * Math.Sin(angle)) * distance);
    }

    private static Vec3d Normalize(Vec3d value)
    {
        double length = Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        if (length < 0.0001)
            return new Vec3d(0, 0, 1);

        return new Vec3d(value.X / length, value.Y / length, value.Z / length);
    }

    private static Vec3d Cross(Vec3d left, Vec3d right)
    {
        return new Vec3d(
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);
    }

    private void ApplyDirectionRotation(Vec3d direction)
    {
        float yaw = (float)Math.Atan2(direction.X, direction.Z);
        float horizontal = GameMath.Sqrt((float)(direction.X * direction.X + direction.Z * direction.Z));
        float pitch = -(float)Math.Atan2(direction.Y, horizontal);

        Mat4f.RotateY(modelMatrix, modelMatrix, yaw);
        Mat4f.RotateX(modelMatrix, modelMatrix, pitch);
    }

    private MeshRef GetMeshRef(ActivePipeFlow flow, int frame, FlowMeshKind meshKind)
    {
        string cacheKey = $"{flow.MeshKey}:{meshKind}";
        if (!meshCache.TryGetValue(cacheKey, out MeshRef?[]? frames))
        {
            frames = new MeshRef?[FlowAnimationFrames];
            meshCache[cacheKey] = frames;
        }

        MeshRef? mesh = frames[frame];
        if (mesh == null || mesh.Disposed)
        {
            float uvOffset = frame / (float)FlowAnimationFrames;
            mesh = capi.Render.UploadMesh(CreateFlowingLiquidMesh(flow.TexturePosition, uvOffset, meshKind));
            frames[frame] = mesh;
        }

        return mesh;
    }

    private int GetFlowFrame(ActivePipeFlow flow, FilledPipeSegment segment, long now)
    {
        float elapsedSeconds = (now - flow.FlowStartedMs) / 1000f;
        float offset = segment.PathDistance * FlowUvTilesPerBlock - elapsedSeconds * FlowUvSpeed;
        return (int)Math.Floor(Fract(offset) * FlowAnimationFrames) % FlowAnimationFrames;
    }

    private int GetWhiteTextureId()
    {
        if (whiteTextureId == 0)
#pragma warning disable CS0618
            whiteTextureId = capi.Render.LoadTextureFromRgba([unchecked((int)0xffffffff)], 1, 1, false, 0);
#pragma warning restore CS0618

        return whiteTextureId;
    }

    private LiquidTexture GetLiquidTexture(ItemStack liquidStack, string liquidKey)
    {
        if (TryGetKnownWorldLiquidTexture(liquidStack, out TextureAtlasPosition texPos))
            return CreateLiquidTexture($"flow:{liquidKey}:world", texPos);

        CompositeTexture? containableTexture = BlockLiquidContainerBase.GetContainableProps(liquidStack)?.Texture
            ?? liquidStack.Collectible.Attributes?["inContainerTexture"]?.AsObject<CompositeTexture>(null, liquidStack.Collectible.Code.Domain);

        if (containableTexture != null && TryGetTextureAtlasPosition(containableTexture, out texPos))
            return CreateLiquidTexture($"flow:{liquidKey}:container", texPos);

        if (liquidStack.Block != null)
        {
            TextureAtlasPosition? blockTexPos = GetTextureFromCompositeTextures(liquidStack.Block.Textures);
            if (blockTexPos != null)
                return CreateLiquidTexture($"flow:{liquidKey}:block", blockTexPos);
        }

        if (liquidStack.Item != null)
        {
            TextureAtlasPosition? itemTexPos = GetTextureFromCompositeTextures(liquidStack.Item.Textures);
            if (itemTexPos != null)
                return CreateLiquidTexture($"flow:{liquidKey}:item", itemTexPos);
        }

        return new LiquidTexture($"flow:{liquidKey}:white", GetWhiteTextureId(), null);
    }

    private LiquidTexture CreateLiquidTexture(string meshKey, TextureAtlasPosition texPos)
    {
        return new LiquidTexture(
            $"{meshKey}:{texPos.atlasTextureId}:{texPos.x1}:{texPos.y1}:{texPos.x2}:{texPos.y2}",
            texPos.atlasTextureId,
            texPos);
    }

    private bool TryGetKnownWorldLiquidTexture(ItemStack liquidStack, out TextureAtlasPosition texPos)
    {
        texPos = null!;
        string code = liquidStack.Collectible?.Code?.ToString() ?? "";
        string? path = null;

        if (code.Contains("rapidwater", StringComparison.OrdinalIgnoreCase))
            path = "block/liquid/rapidwater";
        else if (code.Contains("saltwater", StringComparison.OrdinalIgnoreCase))
            path = "block/liquid/saltwater";
        else if (code.Contains("water", StringComparison.OrdinalIgnoreCase))
            path = "block/liquid/water";
        else if (code.Contains("lava", StringComparison.OrdinalIgnoreCase))
            path = "block/liquid/lava";

        return path != null && TryGetTextureAtlasPosition(new AssetLocation("survival", path), out texPos);
    }

    private TextureAtlasPosition? GetTextureFromCompositeTextures(IDictionary<string, CompositeTexture>? textures)
    {
        if (textures == null || textures.Count == 0)
            return null;

        string[] preferredTextureCodes =
        [
            "flow", "flowing", "all", "liquid", "contents", "content", "still",
            "water", "lava", "oil", "top", "up", "side", "north", "east", "south", "west"
        ];

        foreach (string textureCode in preferredTextureCodes)
        {
            if (textures.TryGetValue(textureCode, out CompositeTexture? texture)
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
        return texturePath != null && TryGetTextureAtlasPosition(texturePath, out texPos);
    }

    private bool TryGetTextureAtlasPosition(AssetLocation texturePath, out TextureAtlasPosition texPos)
    {
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
        if (colorCache.TryGetValue(key, out Vec4f? cached))
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

    private static bool TryGetKnownLiquidColor(string code, out Vec4f color)
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

        if (code.Contains("oil", StringComparison.OrdinalIgnoreCase) || code.Contains("tar", StringComparison.OrdinalIgnoreCase))
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

    private ActivePipeFlow? FindActiveFlow(string flowKey)
    {
        foreach (ActivePipeFlow flow in activeFlows)
        {
            if (flow.FlowKey == flowKey)
                return flow;
        }

        return null;
    }

    private List<FilledPipeSegment> BuildFilledPipeSegments(List<Vec3d> points)
    {
        var segments = new List<FilledPipeSegment>();
        float pathDistance = 0f;

        for (int i = 0; i < points.Count - 1; i++)
        {
            Vec3d start = points[i];
            Vec3d end = points[i + 1];
            Vec3d delta = end.SubCopy(start);
            double lengthSq = delta.LengthSq();
            if (lengthSq < 0.0001 || lengthSq > MaxPipeSegmentLengthSq)
                continue;

            FlowMeshKind meshKind = ClipPipeInventorySegment(ref start, ref end);
            delta = end.SubCopy(start);
            lengthSq = delta.LengthSq();
            if (lengthSq < 0.0001 || lengthSq > MaxPipeSegmentLengthSq)
                continue;

            double length = Math.Sqrt(lengthSq);
            Vec3d direction = delta.Mul(1 / length);
            segments.Add(new FilledPipeSegment
            {
                Center = new Vec3d(
                    (start.X + end.X) * 0.5,
                    (start.Y + end.Y) * 0.5,
                    (start.Z + end.Z) * 0.5),
                Direction = direction,
                Length = (float)length,
                PathDistance = pathDistance,
                MeshKind = meshKind,
                SegmentKey = BuildSegmentKey(start, end)
            });

            pathDistance += (float)length;
        }

        return segments;
    }

    private FlowMeshKind ClipPipeInventorySegment(ref Vec3d start, ref Vec3d end)
    {
        BlockPos startPos = ToBlockPos(start);
        BlockPos endPos = ToBlockPos(end);
        bool startIsPipe = IsPipeAt(startPos);
        bool endIsPipe = IsPipeAt(endPos);

        if (startIsPipe == endIsPipe)
            return FlowMeshKind.Normal;

        if (startIsPipe)
        {
            if (!TryGetFacingIndex(startPos, endPos, out int sideIndex))
                return FlowMeshKind.Normal;

            float reach = PipeNeighborMeshClipper.GetReachAlongFacing(capi, startPos, sideIndex, endPos, PipeArmFullReach);
            end = OffsetFromBlockCenter(startPos, BlockFacing.ALLFACES[sideIndex], reach);
            return FlowMeshKind.Outlet;
        }

        if (!TryGetFacingIndex(endPos, startPos, out int reverseSideIndex))
            return FlowMeshKind.Normal;

        float reverseReach = PipeNeighborMeshClipper.GetReachAlongFacing(capi, endPos, reverseSideIndex, startPos, PipeArmFullReach);
        start = OffsetFromBlockCenter(endPos, BlockFacing.ALLFACES[reverseSideIndex], reverseReach);
        return FlowMeshKind.Inlet;
    }

    private bool IsPipeAt(BlockPos pos)
    {
        return capi.World.BlockAccessor.GetBlock(pos) is BlockPipeBase
            || capi.World.BlockAccessor.GetBlockEntity(pos) is BEPipe or BlockEntityPipeBase;
    }

    private static bool TryGetFacingIndex(BlockPos from, BlockPos to, out int sideIndex)
    {
        int dx = to.X - from.X;
        int dy = to.Y - from.Y;
        int dz = to.Z - from.Z;

        for (int i = 0; i < BlockFacing.ALLFACES.Length; i++)
        {
            Vec3i normal = BlockFacing.ALLFACES[i].Normali;
            if (normal.X == dx && normal.Y == dy && normal.Z == dz)
            {
                sideIndex = i;
                return true;
            }
        }

        sideIndex = -1;
        return false;
    }

    private static Vec3d OffsetFromBlockCenter(BlockPos pos, BlockFacing facing, float reach)
    {
        return new Vec3d(
            pos.X + 0.5 + facing.Normali.X * reach,
            pos.Y + 0.5 + facing.Normali.Y * reach,
            pos.Z + 0.5 + facing.Normali.Z * reach);
    }

    private static BlockPos ToBlockPos(Vec3d point)
    {
        return new BlockPos((int)Math.Floor(point.X), (int)Math.Floor(point.Y), (int)Math.Floor(point.Z));
    }

    private static string BuildSegmentKey(Vec3d start, Vec3d end)
    {
        string first = BuildPointKey(start);
        string second = BuildPointKey(end);
        return string.CompareOrdinal(first, second) <= 0 ? $"{first}>{second}" : $"{second}>{first}";
    }

    private static string BuildPointKey(Vec3d point)
    {
        return $"{(int)Math.Round(point.X * 1000)},{(int)Math.Round(point.Y * 1000)},{(int)Math.Round(point.Z * 1000)}";
    }

    private static string BuildFlowKey(string liquidKey, List<Vec3d> points)
    {
        string key = liquidKey;
        foreach (Vec3d point in points)
        {
            key += $":{(int)Math.Floor(point.X)},{(int)Math.Floor(point.Y)},{(int)Math.Floor(point.Z)}";
        }

        return key;
    }

    private static MeshData CreateFlowingLiquidMesh(TextureAtlasPosition? texPos, float uvOffset, FlowMeshKind meshKind)
    {
        List<(float Start, float End)> strips = GetFlowUvStrips(uvOffset);
        int totalRings = 0;
        foreach ((float start, float end) in strips)
            totalRings += GetStripRingCount(start, end);

        int vertexCount = CylinderSides * totalRings;
        int indexCount = CylinderSides * (totalRings - strips.Count) * 6;
        var mesh = new MeshData(vertexCount, indexCount, withNormals: false, withUv: true, withRgba: true, withFlags: false);

        mesh.xyz = new float[vertexCount * 3];
        mesh.Uv = new float[vertexCount * 2];
        mesh.Indices = new int[indexCount];
        mesh.Rgba = new byte[vertexCount * 4];

        int ringOffset = 0;
        int index = 0;

        for (int stripIndex = 0; stripIndex < strips.Count; stripIndex++)
        {
            (float start, float end) = strips[stripIndex];
            int stripRings = GetStripRingCount(start, end);
            for (int ring = 0; ring < stripRings; ring++)
            {
                float localT = ring / (stripRings - 1f);
                float t = GameMath.Lerp(start, end, localT);
                float z = GameMath.Lerp(-0.5f, 0.5f, t);
                float v = Fract(t * FlowUvTilesPerBlock + uvOffset);
                if (stripIndex < strips.Count - 1 && ring == stripRings - 1)
                    v = 1f;
                else if (stripIndex > 0 && ring == 0)
                    v = 0f;

                for (int side = 0; side < CylinderSides; side++)
                {
                    float u = side / (float)CylinderSides;
                    float angle = GameMath.TWOPI * u;
                    int vertex = (ringOffset + ring) * CylinderSides + side;
                    int xyzIndex = vertex * 3;
                    int uvIndex = vertex * 2;
                    int rgbaIndex = vertex * 4;

                    float x = GameMath.Cos(angle);
                    float y = GameMath.Sin(angle);
                    mesh.xyz[xyzIndex] = x;
                    mesh.xyz[xyzIndex + 1] = y;
                    mesh.xyz[xyzIndex + 2] = GetSlopedEndpointZ(z, t, y, meshKind);

                    mesh.Uv[uvIndex] = texPos == null ? u : GameMath.Lerp(texPos.x1, texPos.x2, u);
                    mesh.Uv[uvIndex + 1] = texPos == null ? v : GameMath.Lerp(texPos.y1, texPos.y2, v);

                    float sideLight = Math.Max(0f, GameMath.Sin(angle) * 0.7f + GameMath.Cos(angle) * 0.3f);
                    float shade = 0.82f + 0.18f * sideLight;
                    mesh.Rgba[rgbaIndex] = FloatColorToByte(shade);
                    mesh.Rgba[rgbaIndex + 1] = FloatColorToByte(shade);
                    mesh.Rgba[rgbaIndex + 2] = FloatColorToByte(shade);
                    mesh.Rgba[rgbaIndex + 3] = 255;
                }
            }

            for (int ring = 0; ring < stripRings - 1; ring++)
            {
                int ringStart = (ringOffset + ring) * CylinderSides;
                int nextRingStart = (ringOffset + ring + 1) * CylinderSides;

                for (int side = 0; side < CylinderSides; side++)
                {
                    int next = (side + 1) % CylinderSides;
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

            ringOffset += stripRings;
        }

        mesh.VerticesCount = vertexCount;
        mesh.IndicesCount = index;
        return mesh;
    }

    private static List<(float Start, float End)> GetFlowUvStrips(float uvOffset)
    {
        var strips = new List<(float Start, float End)>();
        float start = 0f;

        for (int wrap = 1; wrap <= (int)Math.Ceiling(FlowUvTilesPerBlock + 1f); wrap++)
        {
            float boundary = (wrap - uvOffset) / FlowUvTilesPerBlock;
            if (boundary <= 0f)
                continue;

            if (boundary >= 1f)
                break;

            strips.Add((start, boundary));
            start = boundary;
        }

        strips.Add((start, 1f));
        return strips;
    }

    private static int GetStripRingCount(float start, float end)
    {
        return Math.Max(2, (int)Math.Ceiling((end - start) * (CylinderRings - 1)) + 1);
    }

    private static float GetSlopedEndpointZ(float z, float t, float y, FlowMeshKind meshKind)
    {
        float topFactor = (y + 1f) * 0.5f;

        return meshKind switch
        {
            FlowMeshKind.Inlet => z + EndpointSlopeLength * topFactor * SmoothStep(GameMath.Clamp(1f - t / EndpointSlopeLength, 0f, 1f)),
            FlowMeshKind.Outlet => z - EndpointSlopeLength * topFactor * SmoothStep(GameMath.Clamp((t - (1f - EndpointSlopeLength)) / EndpointSlopeLength, 0f, 1f)),
            _ => z
        };
    }

    private static float SmoothStep(float value)
    {
        return value * value * (3f - 2f * value);
    }

    private static float Fract(float value)
    {
        return value - (float)Math.Floor(value);
    }

    private static byte FloatColorToByte(float value)
    {
        return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(value * 255f)));
    }

    public void Dispose()
    {
        activeFlows.Clear();

        foreach (MeshRef?[] frames in meshCache.Values)
        {
            foreach (MeshRef? mesh in frames)
                mesh?.Dispose();
        }
        meshCache.Clear();

        if (whiteTextureId != 0)
        {
            capi.Render.GLDeleteTexture(whiteTextureId);
            whiteTextureId = 0;
        }
    }

    private sealed class ActivePipeFlow
    {
        public string FlowKey = "unknown";
        public long ExpiresMs;
        public long FlowStartedMs;
        public List<FilledPipeSegment> Segments = null!;
        public List<BlockPos>? PathBlocks;
        public string MeshKey = "unknown";
        public int TextureId;
        public TextureAtlasPosition? TexturePosition;
        public Vec4f Color = null!;
    }

    private sealed class FilledPipeSegment
    {
        public Vec3d Center = null!;
        public Vec3d Direction = null!;
        public float Length;
        public float PathDistance;
        public FlowMeshKind MeshKind;
        public string SegmentKey = "unknown";
    }

    private enum FlowMeshKind
    {
        Normal,
        Inlet,
        Outlet
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
