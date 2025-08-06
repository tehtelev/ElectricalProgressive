using ElectricalProgressive.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Block.EWoodcutter;

public class BlockEntityEWoodcutter : BlockEntityOpenableContainer
{
    private BlockPos? _currentTreePos;
    private Stack<BlockPos> _allTreePos = new();

    /// <summary>
    /// Радиус посадки саженцев
    /// </summary>
    private int _plantSaplingRadius;
    /// <summary>
    /// Радиус поиска деревьев
    /// </summary>
    /// <remarks>Больше радиуса посадки, чтобы гарантировать срубание деревьев больше 1 блока</remarks>
    private int _treeChopRadius;
    /// <summary>
    /// Радиус поиска летающих деревьев
    /// </summary>
    /// <remarks>Иногда деревья не полностью срубаются и остаются висеть в воздухе</remarks>
    private int _flyTreeRadius;

    /// <summary>
    /// Сколько блоков ломает за 1 тик
    /// </summary>
    private int _maxBlocksPerBatch;

    public bool IsNotEnoughEnergy { get; set; }
    public int WoodTier { get; private set; }
    public int TreeResistance { get; private set; }
    public WoodcutterStage Stage { get; private set; }

    #region ElectricalProgressive

    protected BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();

    public (EParams, int) Eparams
    {
        get => ElectricalProgressive?.Eparams ?? (new(), 0);
        set => ElectricalProgressive!.Eparams = value;
    }

    public EParams[] AllEparams
    {
        get => ElectricalProgressive?.AllEparams ?? new EParams[]
        {
            new(),
            new(),
            new(),
            new(),
            new(),
            new()
        };
        set
        {
            if (ElectricalProgressive != null)
                ElectricalProgressive.AllEparams = value;
        }
    }

    public const string AllEparamsKey = "electricalprogressive:allEparams";

    #endregion

    #region Inventory
    public override string InventoryClassName => "InvEWoodcutter";


    private InventoryEWoodcutter _inventory;

    /// <summary>
    /// SlotID: 0 = input, 1-5 = output
    /// </summary>
    public override InventoryBase Inventory => _inventory;

    public bool HasSeed => !_inventory[0].Empty;

    #endregion

    public BlockEntityEWoodcutter()
    {
        _inventory = new(null!);
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);

        _plantSaplingRadius = MyMiniLib.GetAttributeInt(Block, "plantSaplingRadius", 3);
        _treeChopRadius = MyMiniLib.GetAttributeInt(Block, "treeChopRadius", 5);
        _flyTreeRadius = MyMiniLib.GetAttributeInt(Block, "flyTreeRadius", 5);
        _maxBlocksPerBatch = MyMiniLib.GetAttributeInt(Block, "maxBlocksPerBatch", 10);

        _inventory.Pos = Pos;
        _inventory.LateInitialize($"{InventoryClassName}-{Pos.X}/{Pos.Y}/{Pos.Z}", api);

        if (Api.Side.IsClient())
            return;

        var canPlantAndChop = _treeChopRadius != 0 && _plantSaplingRadius != 0;
        if (canPlantAndChop)
        {
            RegisterGameTickListener(StageWatcher, 500);
            RegisterGameTickListener(ChopTreeUpdate, 500);
        }

        if (canPlantAndChop && _flyTreeRadius != 0)
            RegisterGameTickListener(ChopFlyingTreeUpdate, 5000);
    }

    /// <summary>
    /// Отслеживает состояние лесоруба и управляет переходами между стадиями
    /// </summary>
    private void StageWatcher(float dt)
    {
        var newTreePos = default(BlockPos?);

        // Если сейчас нет дерева на рубку и не нашли новое, то занимаемся посадкой или ожиданием
        if (_currentTreePos is null && !TryFindNearbyTree(Pos, out newTreePos))
        {
            // Придется постоянно искать саженцы, чтобы корректно отрабатывать стадии сна и ожидания роста
            var (canPlantSapling, anySaplingExists) = TryFindPlantingPosition(Pos, out var plantPos);

            // Если есть семена и есть куда сажать, то сажаем
            if (HasSeed && canPlantSapling)
            {
                ChangeStage(WoodcutterStage.PlantTree);
                PlantSapling(plantPos!);
                return;
            }

            // Если сажать нечего или не было места, но есть хоть один саженец, то ждем роста
            if (anySaplingExists)
            {
                ChangeStage(WoodcutterStage.WaitFullGrowth);
                return;
            }

            // Если нет семян, места или ничего не растет, то спим
            ChangeStage(WoodcutterStage.None);
            return;
        }

        if (_currentTreePos is not null)
        {
            var currentTreeBlock = Api.World.BlockAccessor.GetBlock(_currentTreePos);
            if (currentTreeBlock.Id == 0 || currentTreeBlock is BlockSapling)
            {
                // Обнуляем позицию дерева, если она стала не актуальной
                _currentTreePos = null;
                Stage = WoodcutterStage.None;

                MarkDirty();
                return;
            }

            // Если можем срубить текущее дерево, то рубим
            if (CanBreakBlock(currentTreeBlock))
            {
                ChangeStage(WoodcutterStage.ChopTree);
                return;
            }
        }

        // Если позиции совпадают, но мы не можем срубить текущее, то спим
        if (_currentTreePos == newTreePos)
        {
            ChangeStage(WoodcutterStage.None);
            return;
        }

        var newTreeBlock = Api.World.BlockAccessor.GetBlock(newTreePos);
        // Если и новую позицию не можем срубить, то это очень странно...
        if (!CanBreakBlock(newTreeBlock))
        {
            ChangeStage(WoodcutterStage.None);
            return;
        }

        // Можем начинать срубать новое дерево
        _currentTreePos = newTreePos;
        Stage = WoodcutterStage.ChopTree;

        MarkDirty();
    }

    private void ChangeStage(WoodcutterStage newStage)
    {
        if (newStage == Stage)
            return;

        Stage = newStage;

        MarkDirty();
    }

    /// <summary>
    /// Ищет подходящее место для посадки саженца в радиусе <see cref="_plantSaplingRadius"/>
    /// </summary>
    /// <returns>canPlantSapling - Есть свободное место <br/> anySaplingExists - Есть хотя бы 1 саженец</returns>
    private (bool canPlantSapling, bool anySaplingExists) TryFindPlantingPosition(BlockPos centerPos, out BlockPos? plantPos)
    {
        plantPos = null!;

        var canPlantSapling = false;
        var anySaplingExists = false;

        var blockAccessor = Api.World.BlockAccessor;
        var radius = _plantSaplingRadius;

        for (var dx = -radius; dx <= radius; dx++)
        {
            for (var dz = -radius; dz <= radius; dz++)
            {
                var candidate = centerPos.AddCopy(dx, 0, dz);
                if (candidate == centerPos)
                    continue;

                var block = blockAccessor.GetBlock(candidate);
                if (block.Id != 0)
                {
                    var isSapling = block is BlockSapling
                         || block.Class == "BlockSapling"
                         || (block.Attributes?.KeyExists("treeGen") ?? false);
                    if (isSapling)
                        anySaplingExists = true;

                    continue;
                }

                // Если нашли место под посадку, то искать его больше не нужно, только проверять на саженцы
                if (canPlantSapling)
                    continue;

                var underPos = candidate.DownCopy();
                var underBlock = blockAccessor.GetBlock(underPos);

                if (underBlock.Fertility <= 0)
                    continue;

                plantPos = candidate;
                canPlantSapling = true;
            }
        }

        return (canPlantSapling, anySaplingExists);
    }

    /// <summary>
    /// Ищет ближайшее дерево или саженец в радиусе <see cref="_treeChopRadius"/> блоков на том же уровне Y
    /// </summary>
    private bool TryFindNearbyTree(BlockPos centerPos, out BlockPos treePos)
    {
        treePos = null!;

        var blockAccessor = Api.World.BlockAccessor;
        var radius = _treeChopRadius;
        var closestDist = double.MaxValue;

        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                var candidate = centerPos.AddCopy(dx, 0, dz);
                if (candidate == centerPos)
                    continue;

                var block = blockAccessor.GetBlock(candidate);
                if (block.Id == 0)
                    continue;

                if (!CanBreakBlock(block))
                    continue;

                // Вычисление расстояния по горизонтали
                var dist = Math.Sqrt(dx * dx + dz * dz);
                if (!(dist < closestDist))
                    continue;

                closestDist = dist;
                treePos = candidate;
            }
        }

        return treePos != null;
    }

    /// <summary>
    /// Сажает саженец в указанной позиции
    /// </summary>
    private void PlantSapling(BlockPos pos)
    {
        if (Api.Side.IsClient())
            return;

        if (IsNotEnoughEnergy || !HasSeed)
            return;

        if (_inventory[0]?.Itemstack?.Collectible is not ItemTreeSeed treeSeed)
            return;

        var treeType = treeSeed.Variant["type"];
        var saplingBlock = Api.World.GetBlock(AssetLocation.Create("sapling-" + treeType + "-free", treeSeed.Code.Domain));
        if (saplingBlock == null)
            return;

        Api.World.BlockAccessor.SetBlock(
            saplingBlock.Id,
            pos,
            _inventory[0].Itemstack
        );
        Api.World.PlaySoundAt(saplingBlock.Sounds.Place, pos.X, pos.Y, pos.Z);

        _inventory[0].TakeOut(1);
        _inventory[0].MarkDirty();
    }

    private int _blocksBroken;
    private float _leavesMul = 1;
    private float _leavesBranchyMul = 0.8f;

    /// <summary>
    /// Обрабатывает процесс рубки дерева
    /// </summary>
    private void ChopTreeUpdate(float dt)
    {
        if (Stage != WoodcutterStage.ChopTree || IsNotEnoughEnergy)
            return;

        if (_currentTreePos != null && _allTreePos.Count == 0)
        {
            _allTreePos = FindTree(Api.World, _currentTreePos, out int resistance, out int woodTier);

            TreeResistance = resistance;
            WoodTier = woodTier;

            MarkDirty();

            // Если FindTree не нашел дерево, то скорее всего это бревна поставленные игроком.
            // Это проблемно, рубка таких блоков почти ничего не стоит
            if (_allTreePos.Count == 0)
            {
                var singleTree = Api.World.BlockAccessor.GetBlock(_currentTreePos);
                BreakBlockAndCollect(singleTree, _currentTreePos, 1f);
                return;
            }
        }

        var blocksProcessed = 0;

        while (blocksProcessed < _maxBlocksPerBatch && _allTreePos.Count > 0)
        {
            if (IsNotEnoughEnergy || !_allTreePos.TryPop(out var pos))
                break;

            var block = Api.World.BlockAccessor.GetBlock(pos);
            if (block.BlockMaterial == EnumBlockMaterial.Air)
                continue;

            _blocksBroken++;
            blocksProcessed++;

            var isBranchy = block.Code.Path.Contains("branchy");
            var isLeaves = block.BlockMaterial == EnumBlockMaterial.Leaves;

            var dropMultiplier = isLeaves
                ? _leavesMul
                : isBranchy
                    ? _leavesBranchyMul
                    : 1f;

            BreakBlockAndCollect(block, pos, dropMultiplier);

            // Обновление множителей для листьев
            if (isLeaves && _leavesMul > 0.03f)
                _leavesMul *= 0.85f;

            if (isBranchy && _leavesBranchyMul > 0.015f)
                _leavesBranchyMul *= 0.7f;
        }

        if (_allTreePos.Count == 0)
            ResetChoppingState();
    }

    /// <summary>
    /// Ищет и рубит "летающие" деревья 
    /// </summary>
    private void ChopFlyingTreeUpdate(float dt)
    {
        if (Stage == WoodcutterStage.ChopTree || IsNotEnoughEnergy)
            return;

        var centerPos = Pos.Copy();
        var radius = _flyTreeRadius;

        for (var y = 1; y <= radius; y++)
        {
            var yOffsetPos = centerPos.AddCopy(0, y, 0);

            if (TryFindNearbyTree(yOffsetPos, out var existTreePos))
            {
                _currentTreePos = existTreePos;
                Stage = WoodcutterStage.ChopTree;
                MarkDirty();
                break;
            }
        }
    }

    /// <summary>
    /// Сбрасывает состояние рубки
    /// </summary>
    private void ResetChoppingState()
    {
        Stage = WoodcutterStage.None;
        TreeResistance = WoodTier = 0;

        _blocksBroken = 0;
        _leavesMul = 1;
        _leavesBranchyMul = 0.8f;
        _currentTreePos = null;

        MarkDirty();
    }

    /// <summary>
    /// Разрушает блок и собирает дроп
    /// </summary>
    private void BreakBlockAndCollect(Vintagestory.API.Common.Block block, BlockPos pos, float dropQuantityMultiplier = 1f)
    {
        if (Api.Side.IsClient())
            return;

        var drops = block.GetDrops(Api.World, pos, null, dropQuantityMultiplier);
        if (drops == null)
            return;

        foreach (var stack in drops)
        {
            if (stack == null)
                continue;

            var remainingStack = stack.Clone();
            if (!TryPutStackToInventory(remainingStack))
                Api.World.SpawnItemEntity(remainingStack, pos.ToVec3d());
        }

        block.SpawnBlockBrokenParticles(pos);
        Api.World.BlockAccessor.SetBlock(0, pos);
        Api.World.PlaySoundAt(block.Sounds.Break, pos, 0, null, false, 16);
    }

    /// <summary>
    /// Пытается поместить стак в инвентарь
    /// </summary>
    private bool TryPutStackToInventory(ItemStack stack)
    {
        var isSeed = stack.Collectible.Code.Path.Contains("seed");
        var startSlot = isSeed ? 0 : 1;

        for (var i = startSlot; i < _inventory.Count; i++)
        {
            var slot = _inventory[i];

            // Для семян проверяем только слот 0
            if (isSeed && i != 0)
                continue;

            if (slot.Empty)
            {
                slot.Itemstack = stack.Clone();
                slot.MarkDirty();
                return true;
            }

            if (!slot.Itemstack.Collectible.Equals(slot.Itemstack, stack, GlobalConstants.IgnoredStackAttributes))
                continue;

            var availableSpace = slot.Itemstack.Collectible.MaxStackSize - slot.Itemstack.StackSize;
            if (availableSpace <= 0)
                continue;

            var moveAmount = Math.Min(stack.StackSize, availableSpace);
            if (moveAmount == 0)
                continue;

            slot.Itemstack.StackSize += moveAmount;
            stack.StackSize -= moveAmount;

            slot.MarkDirty();

            if (stack.StackSize <= 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Может ли блок быть срублен
    /// </summary>
    private bool CanBreakBlock([NotNullWhen(true)] Vintagestory.API.Common.Block? block)
    {
        if (block?.Attributes is null || block.Id == 0)
            return false;

        var treeFellingGroupCode = block.Attributes["treeFellingGroupCode"].AsString();
        if (treeFellingGroupCode is null)
            return false;

        var spreadIndex = block.Attributes["treeFellingGroupSpreadIndex"].AsInt(0);
        if (spreadIndex < 2)
            return false;

        if (!block.Attributes["treeFellingCanChop"].AsBool(true))
            return false;

        return true;
    }

    #region BlockEntityCode

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side.IsClient())
        {
            toggleInventoryDialogClient(byPlayer, delegate
            {
                invDialog = new GuiBlockEntityEWoodcutter(_inventory, Pos, Api as ICoreClientAPI);
                return invDialog;
            });
        }

        return true;
    }

    public override void OnBlockPlaced(ItemStack? byItemStack = null)
    {
        base.OnBlockPlaced(byItemStack);

        if (ElectricalProgressive == null || byItemStack is null)
            return;

        var voltage = MyMiniLib.GetAttributeInt(byItemStack.Block, "voltage", 32);
        var maxCurrent = MyMiniLib.GetAttributeFloat(byItemStack.Block, "maxCurrent", 5.0F);
        var isolated = MyMiniLib.GetAttributeBool(byItemStack.Block, "isolated", true);
        var isolatedEnvironment = MyMiniLib.GetAttributeBool(byItemStack.Block, "isolatedEnvironment", true);

        var faceIndex = FacingHelper.Faces(Facing.DownAll).First().Index;

        ElectricalProgressive.Connection = Facing.DownAll;
        ElectricalProgressive.Eparams = (new(voltage, maxCurrent, "", 0, 1, 1, false, isolated, isolatedEnvironment), faceIndex);
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();

        if (invDialog is not null)
        {
            invDialog.TryClose();
            invDialog.Dispose();
            invDialog = null;
        }

        if (ElectricalProgressive != null)
            ElectricalProgressive.Connection = Facing.None;
    }

    /// <summary>
    /// Сохраняет атрибуты
    /// </summary>
    /// <param name="tree"></param>
    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        var inventoryTree = new TreeAttribute();
        _inventory.ToTreeAttributes(inventoryTree);
        tree["inventory"] = inventoryTree;

        tree.SetInt("stage", (int)Stage);
        tree.SetInt("woodTier", WoodTier);
        tree.SetInt("treeResistance", TreeResistance);
        tree.SetBool("isNotEnoughEnergy", IsNotEnoughEnergy);

        if (_currentTreePos != null)
            tree.SetBlockPos("currentTreePos", _currentTreePos);
    }


    /// <summary>
    /// Загружает атрибуты 
    /// </summary>
    /// <param name="tree"></param>
    /// <param name="worldForResolving"></param>
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);

        _inventory.FromTreeAttributes(tree.GetTreeAttribute("inventory"));
        if (Api != null)
            _inventory.AfterBlocksLoaded(worldForResolving);

        Stage = (WoodcutterStage)tree.GetInt("stage");
        WoodTier = tree.GetInt("woodTier");
        TreeResistance = tree.GetInt("treeResistance");
        IsNotEnoughEnergy = tree.GetBool("isNotEnoughEnergy");
        _currentTreePos = tree.GetBlockPos("currentTreePos");
    }

    #endregion

    #region AxeCode

    const int LeafGroups = 7;

    /// <summary>
    /// Resistance is based on 1 for leaves, 2 for branchy leaves, and 4-8 for logs depending on woodTier.
    /// WoodTier is 3 for softwoods (Janka hardness up to about 1000), 4 for temperate hardwoods (Janka hardness 1000-2000), 5 for tropical hardwoods (Janka hardness 2000-3000), and 6 for ebony (Janka hardness over 3000)
    /// </summary>
    /// <param name="world"></param>
    /// <param name="startPos"></param>
    /// <param name="resistance"></param>
    /// <param name="woodTier"></param>
    /// <returns></returns>
    public Stack<BlockPos> FindTree(IWorldAccessor world, BlockPos startPos, out int resistance, out int woodTier)
    {
        var queue = new Queue<Vec4i>();
        var leafqueue = new Queue<Vec4i>();
        var checkedPositions = new HashSet<BlockPos>();
        var foundPositions = new Stack<BlockPos>();
        resistance = 0;
        woodTier = 0;

        var block = world.BlockAccessor.GetBlock(startPos);
        if (block.Code == null)
            return foundPositions;

        var treeFellingGroupCode = block.Attributes?["treeFellingGroupCode"].AsString();
        var spreadIndex = block.Attributes?["treeFellingGroupSpreadIndex"].AsInt(0) ?? 0;
        if (block.Attributes?["treeFellingCanChop"].AsBool(true) == false)
            return foundPositions;

        var bh = EnumTreeFellingBehavior.Chop;

        if (block is ICustomTreeFellingBehavior ctfbh)
        {
            bh = ctfbh.GetTreeFellingBehavior(startPos, null, spreadIndex);
            if (bh == EnumTreeFellingBehavior.NoChop)
            {
                resistance = foundPositions.Count;
                return foundPositions;
            }
        }

        // Must start with a log
        if (spreadIndex < 2)
            return foundPositions;

        if (treeFellingGroupCode == null)
            return foundPositions;

        queue.Enqueue(new(startPos, spreadIndex));
        checkedPositions.Add(startPos);
        var adjacentLeafGroupsCounts = new int[LeafGroups];

        while (queue.Count > 0)
        {
            var pos = queue.Dequeue();
            foundPositions.Push(new(pos.X, pos.Y, pos.Z));   // dimension-correct because pos.Y contains the dimension
            resistance += pos.W + 1;      // leaves -> 1; branchyleaves -> 2; softwood -> 4 etc.

            if (woodTier == 0)
                woodTier = pos.W;

            if (foundPositions.Count > 10000)
                break;

            block = world.BlockAccessor.GetBlockRaw(pos.X, pos.Y, pos.Z, BlockLayersAccess.Solid);
            if (block is ICustomTreeFellingBehavior ctfbhh)
                bh = ctfbhh.GetTreeFellingBehavior(startPos, null, spreadIndex);

            if (bh == EnumTreeFellingBehavior.NoChop)
                continue;

            onTreeBlock(pos,
                world.BlockAccessor,
                checkedPositions,
                startPos,
                bh == EnumTreeFellingBehavior.ChopSpreadVertical,
                treeFellingGroupCode,
                queue,
                leafqueue,
                adjacentLeafGroupsCounts);
        }

        // Find which is the most prevalent of the 7 possible adjacentLeafGroups
        int maxCount = 0;
        int maxI = -1;
        for (int i = 0; i < adjacentLeafGroupsCounts.Length; i++)
        {
            if (adjacentLeafGroupsCounts[i] > maxCount)
            {
                maxCount = adjacentLeafGroupsCounts[i];
                maxI = i;
            }
        }

        // If we found adjacentleaves using the leafgroup system, update the treeFellingGroupCode for the leaves search, using the most commonly found group
        // The purpose of this is to avoid chopping the "wrong" leaf in those cases where trees are growing close together and one of tree 2's leaves is the first leaf found when chopping tree 1
        if (maxI >= 0)
            treeFellingGroupCode = (maxI + 1) + treeFellingGroupCode;

        while (leafqueue.Count > 0)
        {
            var pos = leafqueue.Dequeue();
            foundPositions.Push(new(pos.X, pos.Y, pos.Z));   // dimension-correct because pos.Y contains the dimension
            resistance += pos.W + 1;      // leaves -> 1; branchyleaves -> 2; softwood -> 4 etc.
            if (foundPositions.Count > 10000)
                break;


            onTreeBlock(pos, world.BlockAccessor, checkedPositions, startPos, bh == EnumTreeFellingBehavior.ChopSpreadVertical, treeFellingGroupCode, leafqueue, null!, null!);
        }

        return foundPositions;
    }

    private void onTreeBlock(
        Vec4i pos,
        IBlockAccessor blockAccessor,
        HashSet<BlockPos> checkedPositions,
        BlockPos startPos,
        bool chopSpreadVertical,
        string treeFellingGroupCode,
        Queue<Vec4i> queue,
        Queue<Vec4i> leafqueue,
        int[] adjacentLeaves
    )
    {
        Queue<Vec4i> outqueue;
        for (var i = 0; i < Vec3i.DirectAndIndirectNeighbours.Length; i++)
        {
            var facing = Vec3i.DirectAndIndirectNeighbours[i];
            var neibPos = new BlockPos(pos.X + facing.X, pos.Y + facing.Y, pos.Z + facing.Z);

            if (checkedPositions.Contains(neibPos))
                continue;

            var hordist = GameMath.Sqrt(neibPos.HorDistanceSqTo(startPos.X, startPos.Z));
            var vertdist = (neibPos.Y - startPos.Y);

            // "only breaks blocks inside an upside down square base pyramid"
            var horFactor = chopSpreadVertical ? 0.5f : 2;
            if (hordist - 1 >= horFactor * vertdist)
                continue;

            var block = blockAccessor.GetBlock(neibPos, BlockLayersAccess.Solid);
            if (block.Code == null || block.Id == 0)
                continue;   // Skip air blocks

            var ngcode = block.Attributes?["treeFellingGroupCode"].AsString();

            // Only break the same type tree blocks
            if (ngcode != treeFellingGroupCode)
            {
                if (ngcode == null || leafqueue == null)
                    continue;

                // Leaves now can carry treeSubType value of 1-7 therefore do a separate check for the leaves
                if (block.BlockMaterial == EnumBlockMaterial.Leaves && ngcode.Length == treeFellingGroupCode.Length + 1 && ngcode.EndsWithOrdinal(treeFellingGroupCode))
                {
                    outqueue = leafqueue;
                    int leafGroup = GameMath.Clamp(ngcode[0] - '0', 1, 7);
                    adjacentLeaves[leafGroup - 1]++;
                }
                else
                    continue;
            }
            else
                outqueue = queue;

            // Only spread from "high to low". i.e. spread from log to leaves, but not from leaves to logs
            int nspreadIndex = block.Attributes?["treeFellingGroupSpreadIndex"].AsInt(0) ?? 0;
            if (pos.W < nspreadIndex)
                continue;

            checkedPositions.Add(neibPos);

            if (chopSpreadVertical && !facing.Equals(0, 1, 0) && nspreadIndex > 0)
                continue;

            outqueue.Enqueue(new(neibPos, nspreadIndex));
        }
    }

    #endregion

    public enum WoodcutterStage
    {
        None = 0,

        PlantTree,

        WaitFullGrowth,

        ChopTree
    }
}