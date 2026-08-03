using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Construction;

/// <summary>
/// Универсальная пошаговая сборка (как waterwheel): ПКМ ресурсами по блоку.
/// Активируется атрибутом <c>attributes.construction.levels</c>.
/// </summary>
public class BEBehaviorMachineConstruct : BlockEntityBehavior
{
    private RightClickConstruction? _rcc;
    private ConstructionLevel[]? _levels;
    private float _brokenDropsRatio = 1f;
    private ITreeAttribute? _pendingTree;
    private bool _forming;

    private MeshData? _stageMesh;
    private int _stageMeshIndex = -1;

    private string _stateVariant = "state";
    private string _incompleteState = "incomplete";
    private string _formedState = "formed";

    private readonly List<ItemStack> _consumedMaterials = new();

    public BEBehaviorMachineConstruct(BlockEntity blockentity) : base(blockentity)
    {
    }

    /// <summary>Сформирована (state=formed) или нет вариантов — по флагу.</summary>
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

    /// <summary>Можно ли пользоваться машиной (собрана / formed).</summary>
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

        _levels = conf["levels"].AsObject<ConstructionLevel[]>(null!);
        if (_levels == null || _levels.Length == 0)
            return;

        // Formed-вариант: сборка уже закончена
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

        InvalidateMesh();
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

        // После OnInteract CurrentCompletedStage = стейдж, чьи ресурсы только что потрачены.
        // Нельзя полагаться на RightClickConstruction.GetDrops: он
        //  • возвращает пусто при CurrentCompletedStage <= 1
        //  • не включает материалы последнего завершённого стейджа (i < stage)
        if (Api.Side == EnumAppSide.Server)
            RecordMaterialsForCompletedStage(_rcc.CurrentCompletedStage);

        InvalidateMesh();
        Blockentity.MarkDirty(true);

        if (ShouldFormMachineNow())
        {
            if (Api.Side == EnumAppSide.Server)
                TryFormMachine();
        }

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

        // Нет state-варианта — просто считаем готовым
        if (Blockentity.Block?.Variant == null ||
            !Blockentity.Block.Variant.ContainsKey(_stateVariant))
        {
            Blockentity.MarkDirty(true);
            return;
        }

        _forming = true;
        try
        {
            // Материалы уже накоплены в TryConstruct; при пустом списке (старый сейв) —
            // восстанавливаем по стейджам, включая текущий.
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
            // Сохраняем consumedMats до/после ExchangeBlock (BE обычно сохраняется)
            Blockentity.MarkDirty(true);

            Api.World.BlockAccessor.ExchangeBlock(formedBlock.Id, pos);

            var placed = Api.World.BlockAccessor.GetBlock(pos);
            var mb = placed.GetBehavior<BlockBehaviorMultiblock>();
            if (mb != null)
            {
                var handling = EnumHandling.PassThrough;
                mb.OnBlockPlaced(Api.World, pos, ref handling);
            }

            // BE мог сохраниться — обновим ссылку и снова сохраним материалы
            var be = Api.World.BlockAccessor.GetBlockEntity(pos);
            if (be != null)
            {
                be.Block = placed;

                // Если BE пересоздали — перенесём материалы в новый behavior
                var construct = be.GetBehavior<BEBehaviorMachineConstruct>();
                if (construct != null && !ReferenceEquals(construct, this) &&
                    _consumedMaterials.Count > 0)
                {
                    construct.ReplaceConsumedMaterials(_consumedMaterials);
                }
            }

            be?.MarkDirty(true);
            Api.World.BlockAccessor.MarkBlockDirty(pos);

            Api.World.PlaySoundAt(
                new AssetLocation("game:sounds/block/anvil"),
                pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null);
        }
        finally
        {
            _forming = false;
        }
    }

    /// <summary>Перенос списка материалов (если BE пересоздали при ExchangeBlock).</summary>
    internal void ReplaceConsumedMaterials(IEnumerable<ItemStack> materials)
    {
        _consumedMaterials.Clear();
        foreach (var s in materials)
        {
            if (s is { StackSize: > 0 })
                _consumedMaterials.Add(s.Clone());
        }
    }

    /// <summary>
    /// Записать ресурсы стейджа <paramref name="stageIndex"/> (тот, что только что завершён).
    /// </summary>
    private void RecordMaterialsForCompletedStage(int stageIndex)
    {
        if (_rcc?.Stages == null || Api == null)
            return;
        if (stageIndex < 0 || stageIndex >= _rcc.Stages.Length)
            return;

        AppendResolvedMaterials(_rcc.Stages[stageIndex]?.RequireStacks);
    }

    /// <summary>
    /// Fallback: все requireStacks стейджей 0..CurrentCompletedStage включительно.
    /// (RCC.GetDrops намеренно не используем — у него off-by-one и early-return.)
    /// </summary>
    private void RebuildConsumedMaterialsFromStages()
    {
        _consumedMaterials.Clear();
        foreach (var s in ResolveMaterialsFromStages())
        {
            if (s is { StackSize: > 0 })
                _consumedMaterials.Add(s.Clone());
        }
    }

    private void AppendResolvedMaterials(ConstructionIngredient[]? ingredients)
    {
        AppendResolvedMaterialsInto(ingredients, _consumedMaterials);
    }

    private void AppendResolvedMaterialsInto(ConstructionIngredient[]? ingredients, List<ItemStack> target)
    {
        if (ingredients == null || ingredients.Length == 0 || Api == null)
            return;

        foreach (var ing in ingredients)
        {
            if (ing == null)
                continue;

            // Клонируем, чтобы не портить ингредиент в Stages
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

    /// <summary>
    /// Материалы, вложенные в сборку (с учётом brokenDropsRatio).
    /// Без сайд-эффектов: не мутирует _consumedMaterials.
    /// </summary>
    public ItemStack[] GetMaterialDrops()
    {
        IEnumerable<ItemStack> source;
        if (_consumedMaterials.Count > 0)
        {
            source = _consumedMaterials;
        }
        else if (_rcc != null && !IsFormed)
        {
            // Старые сейвы / mid-build без consumedMats: восстановить по стейджам
            source = ResolveMaterialsFromStages();
        }
        else if (IsFormed && _levels != null)
        {
            // Formed без сохранённых mats (старый form через RCC.GetDrops) —
            // отдать все requireStacks из levels
            source = ResolveAllLevelMaterials();
        }
        else
        {
            return Array.Empty<ItemStack>();
        }

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

    /// <summary>Все requireStacks стейджей 0..CurrentCompletedStage (клоны, без записи в поле).</summary>
    private List<ItemStack> ResolveMaterialsFromStages()
    {
        var result = new List<ItemStack>();
        if (_rcc?.Stages == null || Api == null)
            return result;

        var max = _rcc.CurrentCompletedStage;
        if (max < 0)
            return result;

        for (var i = 0; i <= max && i < _rcc.Stages.Length; i++)
            AppendResolvedMaterialsInto(_rcc.Stages[i]?.RequireStacks, result);

        return result;
    }

    /// <summary>Все requireStacks из levels (для formed без сохранённых mats).</summary>
    private List<ItemStack> ResolveAllLevelMaterials()
    {
        var result = new List<ItemStack>();
        if (_levels == null || Api == null)
            return result;

        foreach (var level in _levels)
            AppendResolvedMaterialsInto(level?.RequireStacks, result);

        return result;
    }

    public ItemStack? GetIncompleteControllerStack()
    {
        if (Blockentity.Block?.Variant == null ||
            !Blockentity.Block.Variant.ContainsKey(_stateVariant))
            return new ItemStack(Blockentity.Block);

        var code = Blockentity.Block.CodeWithVariant(_stateVariant, _incompleteState);
        // side north for inventory consistency if present
        if (Blockentity.Block.Variant.ContainsKey("side"))
            code = Blockentity.Block.CodeWithVariants(
                [_stateVariant, "side"],
                [_incompleteState, "north"]);

        var block = Api?.World.GetBlock(code);
        return block != null ? new ItemStack(block) : null;
    }

    private void InvalidateMesh()
    {
        _stageMeshIndex = -1;
        _stageMesh = null;
    }

    private string? GetShapePathForStage()
    {
        if (_levels == null || _levels.Length == 0)
            return null;

        var stage = _rcc?.CurrentCompletedStage ?? 0;
        if (stage < 0) stage = 0;

        for (var i = Math.Min(stage, _levels.Length - 1); i >= 0; i--)
        {
            var shape = _levels[i]?.Shape;
            if (!string.IsNullOrEmpty(shape))
                return shape;
        }

        return null;
    }

    private MeshData? GetStageMesh(ITesselatorAPI tesselator)
    {
        if (Api?.Side != EnumAppSide.Client || IsFormed || _rcc == null)
            return null;

        var stage = _rcc.CurrentCompletedStage;
        if (_stageMesh != null && _stageMeshIndex == stage)
            return _stageMesh;

        var shapePath = GetShapePathForStage();
        if (string.IsNullOrEmpty(shapePath))
            return null;

        var block = Blockentity.Block;
        var loc = AssetLocation.Create(shapePath, block.Code.Domain)
            .WithPathPrefixOnce("shapes/")
            .WithPathAppendixOnce(".json");

        var shape = Shape.TryGet(Api, loc);
        if (shape == null)
        {
            Api.World.Logger.Warning("MachineConstruct: shape not found: {0}", loc);
            return null;
        }

        tesselator.TesselateShape(block, shape, out var mesh);

        var side = block.Variant.ContainsKey("side") ? block.Variant["side"] : "north";
        var adjustedIndex = ((BlockFacing.FromCode(side)?.HorizontalAngleIndex ?? 1) + 3) & 3;
        var rotY = adjustedIndex * 90;
        if (rotY != 0)
            mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0, rotY * GameMath.DEG2RAD, 0);

        _stageMesh = mesh;
        _stageMeshIndex = stage;
        return _stageMesh;
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        if (IsFormed || _rcc == null)
            return false;

        var mesh = GetStageMesh(tessThreadTesselator);
        if (mesh == null)
            return false;

        mesher.AddMeshData(mesh);
        return true; // не рисовать дефолтный shape incomplete, если есть stage mesh
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);

        if (_rcc != null)
            _rcc.FromTreeAttributes(tree);
        else
            _pendingTree = tree.Clone() as ITreeAttribute ?? tree;

        _consumedMaterials.Clear();
        if (tree["consumedMats"] is TreeAttribute mats)
        {
            foreach (var kv in mats)
            {
                if (kv.Value is ItemstackAttribute { value: not null } isa)
                    _consumedMaterials.Add(isa.value.Clone());
            }
        }

        InvalidateMesh();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        _rcc?.ToTreeAttributes(tree);

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
        dsc.AppendLine(Lang.Get("electricalprogressivecore:construction-hint"));
    }

    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        // Дропы материалов — через BlockBehaviorMachineConstruct.GetDrops
        base.OnBlockBroken(byPlayer);
    }
}
