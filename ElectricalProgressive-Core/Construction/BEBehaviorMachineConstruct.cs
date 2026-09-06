using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Construction;

/// <summary>
/// Сборка машины ПКМ: incomplete → blueprint полной модели, затем stage-shape, formed.
/// </summary>
public class BEBehaviorMachineConstruct : BlockEntityBehavior
{
    private static readonly AssetLocation FlatTexLoc =
        new("electricalprogressivecore", "block/blueprint-flat");

    private static TextureAtlasPosition? _flatTexPos;

    private RightClickConstruction? _rcc;
    private ConstructionLevel[]? _levels;
    private float _brokenDropsRatio = 1f;
    private ITreeAttribute? _pendingTree;
    private bool _forming;

    private MeshData? _mesh;
    private bool _meshReady;
    private int _meshStage = int.MinValue;

    /// <summary>Визуальный прогресс (синк клиент/сервер).</summary>
    private int _revealedStages;

    private string _stateVariant = "state";
    private string _incompleteState = "incomplete";
    private string _formedState = "formed";
    private string? _blueprintShape;

    private byte _fillR = 75, _fillG = 135, _fillB = 190, _fillA = 255;

    private readonly List<ItemStack> _consumedMaterials = new();

    public BEBehaviorMachineConstruct(BlockEntity blockentity) : base(blockentity)
    {
    }

    /// <summary>Incomplete: BE не должен дорисовывать свой default shape.</summary>
    public bool IsRenderingBlueprint => HasConstruction && !IsReady && !IsFormed;

    public bool IsFormed
    {
        get
        {
            if (Blockentity.Block?.Variant == null)
                return false;
            if (!Blockentity.Block.Variant.ContainsKey(_stateVariant))
                return _rcc != null && IsConstructionComplete;
            return Blockentity.Block.Variant[_stateVariant] == _formedState;
        }
    }

    public bool IsIncomplete
    {
        get
        {
            if (Blockentity.Block?.Variant == null || !Blockentity.Block.Variant.ContainsKey(_stateVariant))
                return !IsConstructionComplete;
            return Blockentity.Block.Variant[_stateVariant] == _incompleteState;
        }
    }

    public bool IsConstructionComplete =>
        IsFormed || (_rcc?.Stages != null && _rcc.CurrentCompletedStage >= _rcc.Stages.Length - 1);

    public bool IsReady => IsFormed || IsConstructionComplete;

    public bool HasConstruction => _levels is { Length: > 0 } || _rcc != null;

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);
        InitFromBlockAttributes(api);
    }

    private void InitFromBlockAttributes(ICoreAPI api)
    {
        var conf = Blockentity.Block?.Attributes?["construction"];
        if (conf == null || !conf.KeyExists("levels"))
            return;

        _brokenDropsRatio = conf["brokenDropsRatio"].AsFloat(1f);
        _stateVariant = conf["stateVariant"].AsString("state");
        _incompleteState = conf["incompleteState"].AsString("incomplete");
        _formedState = conf["formedState"].AsString("formed");
        if (conf.KeyExists("blueprintShape"))
            _blueprintShape = conf["blueprintShape"].AsString(null);

        if (conf.KeyExists("blueprint"))
        {
            var bp = conf["blueprint"];
            if (bp.KeyExists("fillR") || bp.KeyExists("r"))
            {
                _fillR = (byte)GameMath.Clamp(bp.KeyExists("fillR") ? bp["fillR"].AsInt(_fillR) : bp["r"].AsInt(_fillR), 0, 255);
                _fillG = (byte)GameMath.Clamp(bp.KeyExists("fillG") ? bp["fillG"].AsInt(_fillG) : bp["g"].AsInt(_fillG), 0, 255);
                _fillB = (byte)GameMath.Clamp(bp.KeyExists("fillB") ? bp["fillB"].AsInt(_fillB) : bp["b"].AsInt(_fillB), 0, 255);
            }

            if (bp.KeyExists("fillA") || bp.KeyExists("a"))
                _fillA = (byte)GameMath.Clamp(bp.KeyExists("fillA") ? bp["fillA"].AsInt(_fillA) : bp["a"].AsInt(_fillA), 40, 255);
        }

        _levels = conf["levels"].AsObject<ConstructionLevel[]>(null!);
        if (_levels == null || _levels.Length == 0)
            return;

        PatchLevelShapesFromJson(conf["levels"], _levels);

        if (IsFormed)
            return;

        var stages = new ConstructionStage[_levels.Length];
        for (var i = 0; i < _levels.Length; i++)
            stages[i] = _levels[i]?.ToConstructionStage() ?? new ConstructionStage();

        _rcc = new RightClickConstruction();
        _rcc.LateInit(stages, api, () => Blockentity.Pos.ToVec3d(),
            "MachineConstruct " + Blockentity.Block.Code);

        if (_pendingTree != null)
        {
            _rcc.FromTreeAttributes(_pendingTree);
            _pendingTree = null;
        }

        SyncRevealedFromRcc();
        InvalidateMesh();
    }

    private static bool LevelHasMaterials(ConstructionLevel? level)
        => level?.RequireStacks is { Length: > 0 };

    private int CountMaterialStagesCompleted()
    {
        if (_rcc?.Stages == null || _levels == null)
            return 0;

        var max = Math.Min(_rcc.CurrentCompletedStage, _levels.Length - 1);
        if (max < 0)
            return 0;

        var count = 0;
        for (var i = 0; i <= max; i++)
        {
            if (LevelHasMaterials(_levels[i]))
                count++;
        }

        return count;
    }

    private void SyncRevealedFromRcc()
    {
        if (_rcc == null)
            return;
        if (_rcc.CurrentCompletedStage > _revealedStages)
            _revealedStages = _rcc.CurrentCompletedStage;
        var mats = CountMaterialStagesCompleted();
        if (mats > _revealedStages)
            _revealedStages = mats;
    }

    private int VisualStageKey
    {
        get
        {
            SyncRevealedFromRcc();
            var rcc = _rcc?.CurrentCompletedStage ?? -1;
            return Math.Max(_revealedStages, Math.Max(rcc, CountMaterialStagesCompleted()));
        }
    }

    private int RenderStageIndex
    {
        get
        {
            if (_levels == null || _levels.Length == 0)
                return 0;
            var idx = Math.Max(_rcc?.CurrentCompletedStage ?? -1, _revealedStages);
            return GameMath.Clamp(idx, 0, _levels.Length - 1);
        }
    }

    public bool TryConstruct(IPlayer byPlayer)
    {
        if (Api == null || _rcc == null || IsFormed)
            return false;

        if (IsConstructionComplete && ShouldFormMachineNow())
        {
            if (Api.Side == EnumAppSide.Server)
                TryFormMachine();
            return true;
        }

        if (!_rcc.OnInteract(byPlayer.Entity, byPlayer.Entity.RightHandItemSlot))
            return false;

        if (Api.Side == EnumAppSide.Server)
            RecordMaterialsForCompletedStage(_rcc.CurrentCompletedStage);

        _revealedStages = Math.Max(_revealedStages, _rcc.CurrentCompletedStage);
        _revealedStages = Math.Max(_revealedStages, CountMaterialStagesCompleted());

        InvalidateMesh();
        Blockentity.MarkDirty(true);

        if (Api is ICoreClientAPI capi)
            capi.World.BlockAccessor.MarkBlockDirty(Blockentity.Pos);

        if (ShouldFormMachineNow() && Api.Side == EnumAppSide.Server)
            TryFormMachine();

        return true;
    }

    private bool ShouldFormMachineNow()
    {
        if (_levels == null || _rcc == null)
            return IsConstructionComplete;

        var stage = _rcc.CurrentCompletedStage;
        if (stage < 0 || stage >= _levels.Length)
            return IsConstructionComplete;

        if (_levels[stage].FormMachine)
            return true;

        return stage >= _levels.Length - 1 && IsConstructionComplete;
    }

    private void TryFormMachine()
    {
        if (_forming || Api == null || Api.Side != EnumAppSide.Server || IsFormed)
            return;
        if (!ShouldFormMachineNow())
            return;

        if (Blockentity.Block?.Variant == null ||
            !Blockentity.Block.Variant.ContainsKey(_stateVariant))
        {
            Blockentity.MarkDirty(true);
            return;
        }

        _forming = true;
        try
        {
            if (_consumedMaterials.Count == 0)
                RebuildConsumedMaterialsFromStages();

            var formedBlock = Api.World.GetBlock(
                Blockentity.Block.CodeWithVariant(_stateVariant, _formedState));
            if (formedBlock == null)
            {
                Api.World.Logger.Error("MachineConstruct: formed block not found for {0}",
                    Blockentity.Block.Code);
                return;
            }

            var pos = Blockentity.Pos;
            Blockentity.MarkDirty(true);
            Api.World.BlockAccessor.ExchangeBlock(formedBlock.Id, pos);

            var placed = Api.World.BlockAccessor.GetBlock(pos);
            var mb = placed.GetBehavior<BlockBehaviorMultiblock>();
            if (mb != null)
            {
                var handling = EnumHandling.PassThrough;
                mb.OnBlockPlaced(Api.World, pos, ref handling);
            }

            var be = Api.World.BlockAccessor.GetBlockEntity(pos);
            if (be != null)
            {
                be.Block = placed;
                var construct = be.GetBehavior<BEBehaviorMachineConstruct>();
                if (construct != null && !ReferenceEquals(construct, this) && _consumedMaterials.Count > 0)
                    construct.ReplaceConsumedMaterials(_consumedMaterials);
            }

            be?.MarkDirty(true);
            Api.World.BlockAccessor.MarkBlockDirty(pos);
            // Соседи (в т.ч. термогенератор) — пересчитать после form
            Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
            Api.World.PlaySoundAt(
                new AssetLocation("game:sounds/block/anvil"),
                pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null);
        }
        finally
        {
            _forming = false;
        }
    }

    internal void ReplaceConsumedMaterials(IEnumerable<ItemStack> materials)
    {
        _consumedMaterials.Clear();
        foreach (var s in materials)
        {
            if (s is { StackSize: > 0 })
                _consumedMaterials.Add(s.Clone());
        }
    }

    private void RecordMaterialsForCompletedStage(int stageIndex)
    {
        if (_rcc?.Stages == null || Api == null)
            return;
        if (stageIndex < 0 || stageIndex >= _rcc.Stages.Length)
            return;
        AppendResolvedMaterials(_rcc.Stages[stageIndex]?.RequireStacks, _consumedMaterials);
    }

    private void RebuildConsumedMaterialsFromStages()
    {
        _consumedMaterials.Clear();
        foreach (var s in ResolveMaterialsFromStages())
        {
            if (s is { StackSize: > 0 })
                _consumedMaterials.Add(s.Clone());
        }
    }

    private void AppendResolvedMaterials(ConstructionIngredient[]? ingredients, List<ItemStack> target)
    {
        if (ingredients == null || ingredients.Length == 0 || Api == null)
            return;

        foreach (var ing in ingredients)
        {
            if (ing == null)
                continue;

            var toResolve = ing.Clone();
            if (!string.IsNullOrEmpty(ing.StoreWildCard) &&
                _rcc?.StoredWildCards != null &&
                _rcc.StoredWildCards.TryGetValue(ing.StoreWildCard, out var wild) &&
                toResolve.Code != null)
            {
                toResolve.Code.Path = toResolve.Code.Path.Replace("*", wild);
            }

            if (_rcc?.StoredWildCards != null)
            {
                foreach (var kv in _rcc.StoredWildCards)
                    toResolve.FillPlaceHolder(kv.Key, kv.Value);
            }

            if (!toResolve.Resolve(Api.World, "MachineConstruct drop material"))
                continue;

            var stack = toResolve.ResolvedItemStack?.Clone();
            if (stack is { StackSize: > 0 })
                target.Add(stack);
        }
    }

    public WorldInteraction[]? GetInteractionHelp(IWorldAccessor world, IPlayer forPlayer)
    {
        if (_rcc == null || IsConstructionComplete || IsFormed)
            return null;
        return _rcc.GetInteractionHelp(world, forPlayer);
    }

    public ItemStack[] GetMaterialDrops()
    {
        IEnumerable<ItemStack> source;
        if (_consumedMaterials.Count > 0)
            source = _consumedMaterials;
        else if (_rcc != null && !IsFormed)
            source = ResolveMaterialsFromStages();
        else if (IsFormed && _levels != null)
            source = ResolveAllLevelMaterials();
        else
            return Array.Empty<ItemStack>();

        var list = new List<ItemStack>();
        var rand = Api?.World.Rand;
        foreach (var s in source)
        {
            if (s is not { StackSize: > 0 })
                continue;

            var drop = s.Clone();
            if (_brokenDropsRatio < 1f && rand != null)
            {
                drop.StackSize = GameMath.RoundRandom(rand, drop.StackSize * _brokenDropsRatio);
                if (drop.StackSize <= 0)
                    continue;
            }

            list.Add(drop);
        }

        return list.ToArray();
    }

    private List<ItemStack> ResolveMaterialsFromStages()
    {
        var result = new List<ItemStack>();
        if (_rcc?.Stages == null || Api == null)
            return result;

        var max = _rcc.CurrentCompletedStage;
        if (max < 0)
            return result;

        for (var i = 0; i <= max && i < _rcc.Stages.Length; i++)
            AppendResolvedMaterials(_rcc.Stages[i]?.RequireStacks, result);

        return result;
    }

    private List<ItemStack> ResolveAllLevelMaterials()
    {
        var result = new List<ItemStack>();
        if (_levels == null || Api == null)
            return result;

        foreach (var level in _levels)
            AppendResolvedMaterials(level?.RequireStacks, result);

        return result;
    }

    public ItemStack? GetIncompleteControllerStack()
    {
        if (Blockentity.Block?.Variant == null ||
            !Blockentity.Block.Variant.ContainsKey(_stateVariant))
            return new ItemStack(Blockentity.Block);

        var code = Blockentity.Block.CodeWithVariant(_stateVariant, _incompleteState);
        if (Blockentity.Block.Variant.ContainsKey("side"))
            code = Blockentity.Block.CodeWithVariants(
                [_stateVariant, "side"],
                [_incompleteState, "north"]);

        var block = Api?.World.GetBlock(code);
        return block != null ? new ItemStack(block) : null;
    }

    // ——— Mesh / shapes ———

    private void InvalidateMesh()
    {
        _mesh = null;
        _meshReady = false;
        _meshStage = int.MinValue;
    }

    private string? GetStageShapePath()
    {
        if (_levels == null || _levels.Length == 0)
            return GetFullShapePath();

        var idx = RenderStageIndex;
        for (var i = idx; i >= 0; i--)
        {
            if (!string.IsNullOrEmpty(_levels[i]?.Shape))
                return _levels[i]!.Shape;
        }

        for (var i = idx + 1; i < _levels.Length; i++)
        {
            if (!string.IsNullOrEmpty(_levels[i]?.Shape))
                return _levels[i]!.Shape;
        }

        return GetFullShapePath();
    }

    private static void PatchLevelShapesFromJson(JsonObject levelsToken, ConstructionLevel[] levels)
    {
        try
        {
            var arr = levelsToken.AsArray();
            if (arr == null || arr.Length == 0)
                return;

            var n = Math.Min(arr.Length, levels.Length);
            for (var i = 0; i < n; i++)
            {
                var el = arr[i];
                if (el == null || !el.KeyExists("shape"))
                    continue;
                var s = el["shape"].AsString(null);
                if (string.IsNullOrEmpty(s))
                    continue;
                levels[i] ??= new ConstructionLevel();
                levels[i].Shape = s;
            }
        }
        catch
        {
            // AsObject fields remain
        }
    }

    private string? GetFullShapePath()
    {
        if (!string.IsNullOrEmpty(_blueprintShape))
            return _blueprintShape;

        if (_levels is { Length: > 0 })
        {
            for (var i = _levels.Length - 1; i >= 0; i--)
            {
                if (!string.IsNullOrEmpty(_levels[i]?.Shape))
                    return _levels[i]!.Shape;
            }
        }

        return GetFormedBlockShapePath();
    }

    private string? GetFormedBlockShapePath()
    {
        var formed = ResolveFormedBlock();
        var baseLoc = formed?.Shape?.Base;
        if (baseLoc?.Path == null)
            return null;

        var path = baseLoc.Path.Replace('\\', '/');
        if (path.StartsWith("shapes/", StringComparison.OrdinalIgnoreCase))
            path = path["shapes/".Length..];
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            path = path[..^5];

        if (!string.IsNullOrEmpty(baseLoc.Domain) && baseLoc.Domain != "game"
            && formed?.Code != null && baseLoc.Domain != formed.Code.Domain)
            return baseLoc.Domain + ":" + path;

        return path;
    }

    private Block? ResolveFormedBlock()
    {
        var block = Blockentity.Block;
        if (block?.Variant != null && block.Variant.ContainsKey(_stateVariant))
        {
            var formed = Api?.World.GetBlock(block.CodeWithVariant(_stateVariant, _formedState));
            if (formed != null)
                return formed;
        }

        return block;
    }

    private Shape? LoadShape(string shapePath)
    {
        if (string.IsNullOrEmpty(shapePath) || Api == null)
            return null;

        var domain = Blockentity.Block?.Code?.Domain ?? "game";
        var loc = AssetLocation.Create(shapePath, domain)
            .WithPathPrefixOnce("shapes/")
            .WithPathAppendixOnce(".json");
        var shape = Shape.TryGet(Api, loc);
        if (shape != null)
            return shape;

        if (shapePath.Contains(':'))
        {
            shape = Shape.TryGet(Api, AssetLocation.Create(shapePath)
                .WithPathPrefixOnce("shapes/")
                .WithPathAppendixOnce(".json"));
            if (shape != null)
                return shape;
        }

        return null;
    }

    private Shape? LoadFullShape()
    {
        var path = GetFullShapePath();
        if (!string.IsNullOrEmpty(path))
        {
            var s = LoadShape(path!);
            if (s != null)
                return s;
        }

        var formed = ResolveFormedBlock();
        if (formed?.Shape?.Base != null)
        {
            var s = Shape.TryGet(Api, formed.Shape.Base.Clone()
                .WithPathPrefixOnce("shapes/")
                .WithPathAppendixOnce(".json"));
            if (s != null)
                return s;
        }

        if (formed?.ShapeInventory?.Base != null)
        {
            var s = Shape.TryGet(Api, formed.ShapeInventory.Base.Clone()
                .WithPathPrefixOnce("shapes/")
                .WithPathAppendixOnce(".json"));
            if (s != null)
                return s;
        }

        return null;
    }

    private void ApplyBlockShapeOffset(MeshData mesh)
    {
        var sh = Blockentity.Block?.Shape;
        if (sh == null)
            return;
        if (sh.offsetX == 0 && sh.offsetY == 0 && sh.offsetZ == 0)
            return;
        mesh.Translate(sh.offsetX, sh.offsetY, sh.offsetZ);
    }

    private Vec3f GetBlockRotation()
    {
        var block = Blockentity.Block;
        var side = block.Variant.ContainsKey("side") ? block.Variant["side"] : "north";
        var adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
        return new Vec3f(0, adjustedIndex * 90, 0);
    }

    private static TextureAtlasPosition GetFlatTexture(ICoreClientAPI capi)
    {
        if (_flatTexPos != null)
            return _flatTexPos;

        try
        {
            if (capi.BlockTextureAtlas.GetOrInsertTexture(FlatTexLoc, out _, out var pos, null, 0.005f)
                && pos != null)
            {
                _flatTexPos = pos;
                return pos;
            }
        }
        catch (Exception ex)
        {
            capi.Logger.Error("[MachineConstruct] blueprint texture: {0}", ex);
        }

        return capi.BlockTextureAtlas.UnknownTexturePosition;
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

    private static bool HasDrawables(MeshData? mesh)
        => mesh is { VerticesCount: > 0, IndicesCount: > 0 };

    private static void SetRenderPass(MeshData mesh, EnumChunkRenderPass pass)
    {
        var ipf = mesh.IndicesPerFace > 0 ? mesh.IndicesPerFace : 3;
        var faces = Math.Max(1, mesh.IndicesCount / ipf);
        if (faces * ipf != mesh.IndicesCount && mesh.IndicesCount > 0)
            faces = Math.Max(1, mesh.IndicesCount / 3);

        if (mesh.RenderPassesAndExtraBits == null || mesh.RenderPassesAndExtraBits.Length != faces)
            mesh.RenderPassesAndExtraBits = new short[faces];
        mesh.RenderPassesAndExtraBits.Fill((short)pass);
    }

    private MeshData TintBlueprint(MeshData solid, TextureAtlasPosition flat)
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
            for (var vi = 0; vi < mesh.VerticesCount; vi++)
            {
                var i = vi * 4;
                var lum = (rgba[i] + rgba[i + 1] + rgba[i + 2]) / (3f * 255f);
                var shade = 0.62f + lum * 0.38f;
                var col = ColorUtil.ColorFromRgba(
                    GameMath.Clamp((int)(_fillR * shade), 0, 255),
                    GameMath.Clamp((int)(_fillG * shade), 0, 255),
                    GameMath.Clamp((int)(_fillB * shade), 0, 255),
                    _fillA);
                rgba[i] = (byte)col;
                rgba[i + 1] = (byte)(col >> 8);
                rgba[i + 2] = (byte)(col >> 16);
                rgba[i + 3] = (byte)(col >> 24);
            }
        }

        SetRenderPass(mesh, EnumChunkRenderPass.OpaqueNoCull);
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

    private MeshData? BuildMesh(ITesselatorAPI tesselator, ICoreClientAPI capi)
    {
        SyncRevealedFromRcc();
        var rot = GetBlockRotation();

        var showBlueprint = (_rcc?.CurrentCompletedStage ?? 0) <= 0
            && CountMaterialStagesCompleted() <= 0
            && _revealedStages <= 0;

        if (showBlueprint)
        {
            var full = LoadFullShape();
            if (full == null)
                return null;

            var flat = GetFlatTexture(capi);
            tesselator.TesselateShape(
                "MachineConstructBP", full, out var solid, new FlatTexSource(capi, flat), rot,
                0, 0, 0, null, null);
            if (!HasDrawables(solid))
                return null;

            var mesh = TintBlueprint(solid, flat);
            ApplyBlockShapeOffset(mesh);
            mesh.Translate(0f, 0.01f, 0f);
            return mesh;
        }

        var path = GetStageShapePath() ?? GetFullShapePath();
        var shape = !string.IsNullOrEmpty(path) ? LoadShape(path!) : null;
        shape ??= LoadFullShape();
        if (shape == null)
            return null;

        try
        {
            var texSrc = new ShapeTextureSource(capi, shape, "MachineConstructStage");
            tesselator.TesselateShape("MachineConstructStage", shape, out var mesh, texSrc, rot,
                0, 0, 0, null, null);

            if (!HasDrawables(mesh))
            {
                var texBlock = ResolveFormedBlock() ?? Blockentity.Block;
                if (texBlock != null)
                    tesselator.TesselateShape(texBlock, shape, out mesh, rot, null, null);
            }

            if (!HasDrawables(mesh))
                return null;

            SetRenderPass(mesh!, EnumChunkRenderPass.Opaque);
            ApplyBlockShapeOffset(mesh!);
            mesh!.Translate(0f, 0.01f, 0f);
            return mesh;
        }
        catch (Exception ex)
        {
            Api?.World.Logger.Warning("[MachineConstruct] stage mesh: {0}", ex.Message);
            return null;
        }
    }

    private void EnsureMesh(ITesselatorAPI tesselator)
    {
        if (Api is not ICoreClientAPI || IsFormed || _rcc == null)
            return;

        var key = VisualStageKey;
        if (_meshReady && _meshStage == key && HasDrawables(_mesh))
            return;

        _mesh = null;
        _meshReady = false;

        try
        {
            _mesh = BuildMesh(tesselator, (ICoreClientAPI)Api);
            _meshStage = key;
            _meshReady = HasDrawables(_mesh);
        }
        catch (Exception ex)
        {
            Api.World.Logger.Error("[MachineConstruct] mesh build failed: {0}", ex);
            _mesh = null;
            _meshReady = false;
        }
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        if (IsFormed || _rcc == null)
            return false;

        EnsureMesh(tessThreadTesselator);
        if (!HasDrawables(_mesh))
            return false;

        mesher.AddMeshData(_mesh);
        return true;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        var stageBefore = VisualStageKey;

        if (_rcc != null)
            _rcc.FromTreeAttributes(tree);
        else
            _pendingTree = tree.Clone() as ITreeAttribute ?? tree;

        if (tree.HasAttribute("constructRevealedStages"))
            _revealedStages = Math.Max(_revealedStages, tree.GetInt("constructRevealedStages"));
        SyncRevealedFromRcc();

        _consumedMaterials.Clear();
        if (tree["consumedMats"] is TreeAttribute mats)
        {
            foreach (var kv in mats)
            {
                if (kv.Value is ItemstackAttribute { value: not null } isa)
                    _consumedMaterials.Add(isa.value.Clone());
            }
        }

        if (VisualStageKey != stageBefore || !_meshReady)
            InvalidateMesh();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        _rcc?.ToTreeAttributes(tree);
        tree.SetInt("constructRevealedStages", _revealedStages);

        if (_consumedMaterials.Count > 0)
        {
            var mats = new TreeAttribute();
            for (var i = 0; i < _consumedMaterials.Count; i++)
                mats["m" + i] = new ItemstackAttribute(_consumedMaterials[i].Clone());
            tree["consumedMats"] = mats;
        }
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);

        if (!HasConstruction || IsReady)
            return;

        dsc.AppendLine(Lang.Get("electricalprogressivecore:construction-incomplete"));
        dsc.AppendLine(Lang.Get("electricalprogressivecore:construction-blueprint-hint"));
        if (_rcc?.Stages is { Length: > 0 })
        {
            var stage = RenderStageIndex;
            dsc.AppendLine($"Build: {stage + 1}/{_rcc.Stages.Length}");
            var shape = GetStageShapePath();
            if (!string.IsNullOrEmpty(shape))
                dsc.AppendLine(shape);
        }
    }
}
