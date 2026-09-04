using System;
using System.Collections.Generic;
using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Construction;

/// <summary>
/// Blueprint полной машины в точке постановки — тот же вид, что у стоящего incomplete.
/// </summary>
public class MachineConstructPlacementPreview : ModSystem, IRenderer
{
    private static readonly AssetLocation FlatTexLoc =
        new("electricalprogressivecore", "block/blueprint-flat");

    private ICoreClientAPI? _capi;
    private TextureAtlasPosition? _flatTexPos;
    private readonly Dictionary<string, MeshRef> _cache = new();
    private readonly Matrixf _modelMat = new();

    public double RenderOrder => 0.71;
    public int RenderRange => 32;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        _capi = api;
        api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "ep-machine-construct-preview");
    }

    public override void Dispose()
    {
        if (_capi != null)
            _capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);

        foreach (var mesh in _cache.Values)
            mesh?.Dispose();
        _cache.Clear();
        _capi = null;
        base.Dispose();
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (_capi?.World?.Player == null)
            return;

        try
        {
            RenderPreview();
        }
        catch (Exception)
        {
            _capi.Render.GlEnableCullFace();
        }
    }

    private void RenderPreview()
    {
        var player = _capi!.World.Player;
        if (player.Entity?.Controls == null || player.InventoryManager == null)
            return;

        var slot = player.InventoryManager.ActiveHotbarSlot;
        var held = slot?.Itemstack;
        if (held?.Block == null || !MachineConstructSystem.HasConstructionLevels(held.Block))
            return;

        var sel = player.CurrentBlockSelection;
        if (sel?.Position == null)
            return;

        var oriented = GetOrientedBlock(player, held, sel);
        if (oriented == null)
            return;

        var meshRef = GetOrBuildMesh(oriented);
        if (meshRef == null)
            return;

        var pos = GetPlacePos(_capi.World, sel, oriented);
        var cam = player.Entity.CameraPos;
        var light = _capi.World.BlockAccessor.GetLightRGBs(pos.X, pos.Y, pos.Z);

        _capi.Render.GlDisableCullFace();

        var prog = _capi.Render.StandardShader;
        prog.Use();
        prog.RgbaAmbientIn = _capi.Render.AmbientColor;
        prog.RgbaFogIn = _capi.Render.FogColor;
        prog.FogMinIn = _capi.Render.FogMin;
        prog.FogDensityIn = _capi.Render.FogDensity;
        prog.RgbaLightIn = light;
        prog.RgbaGlowIn = new Vec4f(0, 0, 0, 0);
        prog.RgbaTint = ColorUtil.WhiteArgbVec;
        prog.ExtraGlow = 18;
        prog.TempGlowMode = 0;
        prog.DontWarpVertices = 1;
        prog.AddRenderFlags = 0;
        prog.NormalShaded = 1;
        prog.AlphaTest = 0.05f;
        prog.ExtraZOffset = 0.01f;
        prog.Tex2D = GetFlatTexture().atlasTextureId;

        _modelMat.Identity().Translate(pos.X - cam.X, pos.Y - cam.Y + 0.01, pos.Z - cam.Z);
        prog.ModelMatrix = _modelMat.Values;
        prog.ViewMatrix = _capi.Render.CameraMatrixOriginf;
        prog.ProjectionMatrix = _capi.Render.CurrentProjectionMatrix;

        _capi.Render.RenderMesh(meshRef);
        prog.Stop();

        _capi.Render.GlEnableCullFace();
    }

    /// <summary>
    /// Клетка постановки: сам блок, если он заменяемый; иначе соседняя клетка по грани прицела.
    /// DidOffset не используем — клиент часто оставляет Position на целевом блоке.
    /// </summary>
    private static BlockPos GetPlacePos(IWorldAccessor world, BlockSelection sel, Block toPlace)
    {
        var pos = sel.Position.Copy();
        var at = world.BlockAccessor.GetBlock(pos);
        if (at != null && at.IsReplacableBy(toPlace))
            return pos;

        if (sel.Face != null)
            return pos.AddCopy(sel.Face);

        return pos;
    }

    private Block? GetOrientedBlock(IPlayer player, ItemStack stack, BlockSelection sel)
    {
        var block = stack.Block;
        var ho = block.GetBehavior<BlockBehaviorHorizontalOrientable>();
        if (ho != null)
        {
            var oriented = ho.GetLookAwareBlockVariant(player, stack, sel);
            if (oriented != null)
                block = oriented;
        }

        if (block.Variant != null &&
            block.Variant.ContainsKey("state") &&
            block.Variant["state"] != "incomplete")
        {
            var incomplete = _capi!.World.GetBlock(block.CodeWithVariant("state", "incomplete"));
            if (incomplete != null)
                block = incomplete;
        }

        return block;
    }

    private MeshRef? GetOrBuildMesh(Block block)
    {
        var rotY = GetRotationY(block);
        var key = block.Code + "@" + rotY;
        if (_cache.TryGetValue(key, out var cached) && cached is { Disposed: false })
            return cached;

        var mesh = BuildBlueprintMesh(block, rotY);
        if (mesh == null || mesh.VerticesCount <= 0)
            return null;

        var meshRef = _capi!.Render.UploadMesh(mesh);
        _cache[key] = meshRef;
        return meshRef;
    }

    private static int GetRotationY(Block block)
    {
        var side = block.Variant != null && block.Variant.ContainsKey("side")
            ? block.Variant["side"]
            : "north";
        var adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
        return adjustedIndex * 90;
    }

    private MeshData? BuildBlueprintMesh(Block block, int rotY)
    {
        var path = GetBlueprintShapePath(block);
        if (string.IsNullOrEmpty(path))
            return null;

        var domain = block.Code?.Domain ?? "game";
        var loc = AssetLocation.Create(path, domain)
            .WithPathPrefixOnce("shapes/")
            .WithPathAppendixOnce(".json");
        var shape = Shape.TryGet(_capi, loc);
        if (shape == null && path.Contains(':'))
        {
            shape = Shape.TryGet(_capi, AssetLocation.Create(path)
                .WithPathPrefixOnce("shapes/")
                .WithPathAppendixOnce(".json"));
        }

        if (shape == null)
            return null;

        var flat = GetFlatTexture();
        _capi!.Tesselator.TesselateShape(
            "MachineConstructHeldBP", shape, out var solid, new FlatTexSource(_capi, flat),
            new Vec3f(0, rotY, 0), 0, 0, 0, null, null);

        if (solid == null || solid.VerticesCount <= 0)
            return null;

        return TintBlueprint(solid, flat);
    }

    private static string? GetBlueprintShapePath(Block block)
    {
        var conf = block.Attributes?["construction"];
        if (conf == null)
            return null;

        var bp = conf["blueprintShape"]?.AsString(null);
        if (!string.IsNullOrEmpty(bp))
            return bp;

        try
        {
            var levels = conf["levels"].AsObject<ConstructionLevel[]>(null!);
            if (levels is { Length: > 0 })
            {
                for (var i = levels.Length - 1; i >= 0; i--)
                {
                    if (!string.IsNullOrEmpty(levels[i]?.Shape))
                        return levels[i]!.Shape;
                }
            }
        }
        catch
        {
            // ignore
        }

        return block.Shape?.Base?.ToString();
    }

    private TextureAtlasPosition GetFlatTexture()
    {
        if (_flatTexPos != null)
            return _flatTexPos;

        try
        {
            if (_capi!.BlockTextureAtlas.GetOrInsertTexture(FlatTexLoc, out _, out var pos, null, 0.005f)
                && pos != null)
            {
                _flatTexPos = pos;
                return pos;
            }
        }
        catch
        {
            // fallback below
        }

        return _capi!.BlockTextureAtlas.UnknownTexturePosition;
    }

    private static MeshData TintBlueprint(MeshData solid, TextureAtlasPosition flat)
    {
        var mesh = solid.Clone();
        var uv = mesh.Uv;
        if (uv != null)
        {
            for (var i = 0; i < mesh.VerticesCount; i++)
            {
                uv[i * 2] = 0.5f;
                uv[i * 2 + 1] = 0.5f;
            }
        }

        mesh.SetTexPos(flat);
        mesh.TextureIds = [flat.atlasTextureId];
        if (mesh.TextureIndices == null || mesh.TextureIndices.Length < mesh.VerticesCount)
            mesh.TextureIndices = new byte[mesh.VerticesCount];
        mesh.TextureIndices.Fill((byte)0);

        if (mesh.ClimateColorMapIds is { Length: > 0 })
            mesh.ClimateColorMapIds.Fill((byte)0);
        if (mesh.SeasonColorMapIds is { Length: > 0 })
            mesh.SeasonColorMapIds.Fill((byte)0);

        var rgba = mesh.Rgba;
        if (rgba is { Length: >= 4 })
        {
            const byte fillR = 75, fillG = 135, fillB = 190, fillA = 255;
            for (var vi = 0; vi < mesh.VerticesCount; vi++)
            {
                var i = vi * 4;
                var lum = (rgba[i] + rgba[i + 1] + rgba[i + 2]) / (3f * 255f);
                var shade = 0.62f + lum * 0.38f;
                var col = ColorUtil.ColorFromRgba(
                    GameMath.Clamp((int)(fillR * shade), 0, 255),
                    GameMath.Clamp((int)(fillG * shade), 0, 255),
                    GameMath.Clamp((int)(fillB * shade), 0, 255),
                    fillA);
                rgba[i] = (byte)col;
                rgba[i + 1] = (byte)(col >> 8);
                rgba[i + 2] = (byte)(col >> 16);
                rgba[i + 3] = (byte)(col >> 24);
            }
        }

        var ipf = mesh.IndicesPerFace > 0 ? mesh.IndicesPerFace : 3;
        var faces = Math.Max(1, mesh.IndicesCount / ipf);
        if (mesh.RenderPassesAndExtraBits == null || mesh.RenderPassesAndExtraBits.Length != faces)
            mesh.RenderPassesAndExtraBits = new short[faces];
        mesh.RenderPassesAndExtraBits.Fill((short)EnumChunkRenderPass.OpaqueNoCull);

        if (mesh.Flags is { Length: > 0 })
        {
            for (var i = 0; i < mesh.Flags.Length && i < mesh.VerticesCount; i++)
            {
                var f = mesh.Flags[i] & ~VertexFlags.GlowLevelBitMask;
                f |= 18 & VertexFlags.GlowLevelBitMask;
                f = (f & VertexFlags.ClearZOffsetMask) | (6 << VertexFlags.ZOffsetBitPos);
                mesh.Flags[i] = f;
            }
        }

        return mesh;
    }

    private sealed class FlatTexSource : ITexPositionSource
    {
        private readonly TextureAtlasPosition _flat;
        private readonly Size2i _atlasSize;

        public FlatTexSource(ICoreClientAPI capi, TextureAtlasPosition flat)
        {
            _flat = flat;
            _atlasSize = capi.BlockTextureAtlas.Size;
        }

        public Size2i AtlasSize => _atlasSize;
        public TextureAtlasPosition this[string textureCode] => _flat;
    }
}
