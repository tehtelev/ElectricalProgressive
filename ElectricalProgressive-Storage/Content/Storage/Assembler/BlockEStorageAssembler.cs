using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Storage.Assembler;

/// <summary>
/// Корпус сборщика. В сеть сам не входит: его кормит интерфейс с грани.
/// </summary>
public class BlockEStorageAssembler : Vintagestory.API.Common.Block
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel == null || !world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityEStorageAssembler assembler)
            return assembler.OnPlayerRightClick(byPlayer, blockSel);

        return false;
    }
}

/// <summary>
/// Сетка 3×3 и слот готового предмета сборщика.
/// </summary>
public class InventoryEStorageAssembler : InventoryGeneric
{
    public const int Output = 9;

    public InventoryEStorageAssembler()
        : base(10, "estorageassembler", "0", null)
    {
    }

    public override ItemSlot? GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
        => null;

    public override ItemSlot? GetAutoPullFromSlot(BlockFacing atBlockFace)
        => this[Output];
}

/// <summary>
/// Сборщик. Интерфейс на грани кладёт шаблон крафта в сетку, здесь он собирается и уходит в сеть.
/// </summary>
public class BlockEntityEStorageAssembler : BlockEntityOpenableContainer, ITexPositionSource
{
    public const float WorkWatts = 24f;
    public const int ProgressPacket = 19112;
    private const float CraftMs = 8000f;

    // Светлое тело темпоральной шестерни. Частицы берут цвет как BGRA, не как ColorFromRgba.
    private static readonly int GearColor = ColorUtil.ToRgba(255, 27, 204, 154);

    private readonly InventoryEStorageAssembler _inventory = new();
    private ItemStack? _expect;
    private ItemStack? _pattern;
    private BlockPos? _reserve;
    private BlockPos? _owner;
    private ItemStack? _yield;
    private bool _busy;
    private float _progress;
    private long _lastMs;
    private float _seenProgress = -1f;
    private long _seenProgressMs;
    private ICoreClientAPI? _capi;
    private MeshData? _center;
    private string? _shownCenter;
    private Shape? _nowTesselatingShape;
    private CollectibleObject? _nowTesselatingObj;

    public bool Working => _busy;
    public float Progress => _busy ? Math.Clamp(_progress, 0f, 1f) : 0f;

    public BlockEntityEStorageAssembler()
    {
        _inventory.SlotModified += _ =>
        {
            if (_capi != null)
                RefreshCenter();
            else if (Api?.Side == EnumAppSide.Server)
                MarkDirty();
        };
    }

    public override InventoryBase Inventory => _inventory;

    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        if (Api?.Side == EnumAppSide.Server && Pos != null)
            Inventory.DropAll(Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        base.OnBlockBroken(byPlayer);
    }

    public override string InventoryClassName => "estorageassembler";

    public ItemSlot CraftSlot(int index) => _inventory[index];

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        _inventory.LateInitialize("estorageassembler-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);
        if (api is ICoreClientAPI capi)
        {
            _capi = capi;
            UpdateCenterMesh();
            _shownCenter = CenterKey();
            api.World.BlockAccessor.MarkBlockDirty(Pos);
            RegisterGameTickListener(_ => CraftParticles(), 90);
        }

        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(_ => Work(), 100);
    }

    public Size2i AtlasSize => _capi!.BlockTextureAtlas.Size;

    public TextureAtlasPosition this[string textureCode]
    {
        get
        {
            AssetLocation? assetLocation = null;
            if (_nowTesselatingObj is Vintagestory.API.Common.Item item && item.Textures != null)
            {
                if (item.Textures.TryGetValue(textureCode, out var compositeTexture))
                    assetLocation = compositeTexture?.Baked?.BakedName;
                else if (item.Textures.TryGetValue("all", out compositeTexture))
                    assetLocation = compositeTexture?.Baked?.BakedName;
            }

            if (assetLocation == null)
                _nowTesselatingShape?.Textures?.TryGetValue(textureCode, out assetLocation);

            if (assetLocation == null)
            {
                var domain = _nowTesselatingObj?.Code?.Domain ?? "electricalprogressivestorage";
                assetLocation = new AssetLocation(domain, "textures/item/" + textureCode);
            }

            return TexPos(assetLocation);
        }
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        base.OnTesselation(mesher, tessThreadTesselator);
        if (_center != null)
            mesher.AddMeshData(_center);
        return false;
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat("craft", Progress);
        if (_busy && _expect?.Collectible != null)
            tree["show"] = new ItemstackAttribute(_expect.Clone());
        else
            tree.RemoveAttribute("show");
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        _expect = null;
        if (tree["show"] is ItemstackAttribute { value: { } shown })
        {
            var copy = shown.Clone();
            if (copy.ResolveBlockOrItem(worldAccessForResolve) && copy.Collectible != null)
                _expect = copy;
        }

        _progress = tree.GetFloat("craft");
        _busy = _progress > 0f || _expect != null;
        if (worldAccessForResolve.Side == EnumAppSide.Client)
            RefreshCenter();
    }

    /// <summary>
    /// Сетка уже собрана. Крафт не списывает предметы, пока не дойдёт полоска и сеть отдаёт мощность.
    /// Жидкость на этот крафт уже зарезервирована в процессоре. В слот она попадает только в конце.
    /// </summary>
    public bool Begin(IWorldAccessor world, ItemStack? expect, ItemStack? pattern = null, BlockEntityEStorageProcessor? reserve = null, BlockPos? owner = null)
    {
        if (_busy || Api?.Side != EnumAppSide.Server)
            return false;

        var player = Nearest(world);
        if (pattern != null && !LiquidCraft.Available(world, reserve, pattern, player))
            return false;

        var matched = MatchRecipe(world, out _, pattern);
        var ingredient = matched?.Output;
        var resolved = ingredient?.ResolvedItemStack;
        if (ingredient == null || resolved == null)
            return false;

        _expect = expect?.Clone();
        if (_expect == null)
        {
            _expect = resolved.Clone();
            _expect.StackSize = Math.Max(1, ingredient.StackSize);
        }
        else if (_expect.StackSize < 1)
            _expect.StackSize = 1;
        _pattern = pattern?.Clone();
        _reserve = reserve?.Pos.Copy();
        _owner = owner?.Copy();
        _busy = true;
        _progress = 0f;
        _lastMs = world.ElapsedMilliseconds;
        PushProgress(true);
        return true;
    }

    public override void OnReceivedServerPacket(int packetid, byte[] data)
    {
        if (packetid == ProgressPacket && data is { Length: >= 4 })
        {
            _progress = Math.Clamp(BitConverter.ToSingle(data, 0), 0f, 1f);
            _busy = _progress > 0f || _expect != null;
            RefreshCenter();
            return;
        }

        base.OnReceivedServerPacket(packetid, data);
    }

    /// <summary>
    /// Пока идёт крафт, с шести внутренних граней рамки к центру летят крупные светящиеся кубики.
    /// </summary>
    private void CraftParticles()
    {
        var world = Api?.World;
        if (!_busy || world == null || Pos == null)
        {
            _seenProgress = -1f;
            return;
        }

        var now = world.ElapsedMilliseconds;
        if (_seenProgress < 0f || Math.Abs(_progress - _seenProgress) > 0.0001f)
        {
            _seenProgress = _progress;
            _seenProgressMs = now;
        }
        else if (now - _seenProgressMs > 700)
        {
            return;
        }

        var rand = world.Rand;
        var origin = Pos.ToVec3d();
        for (var face = 0; face < 6; face++)
        {
            for (var n = 0; n < 3; n++)
            {
                var axis = face / 2;
                var along = (face & 1) == 1 ? 0.84f : 0.16f;
                var u = 0.28f + (float)rand.NextDouble() * 0.44f;
                var v = 0.28f + (float)rand.NextDouble() * 0.44f;
                float x, y, z;
                if (axis == 0)
                {
                    x = along;
                    y = u;
                    z = v;
                }
                else if (axis == 1)
                {
                    x = u;
                    y = along;
                    z = v;
                }
                else
                {
                    x = u;
                    y = v;
                    z = along;
                }

                const float life = 0.36f;
                var velocity = new Vec3f((0.5f - x) / life, (0.5f - y) / life, (0.5f - z) / life);
                var wobble = 0.06f;
                if (axis != 0)
                    velocity.X += ((float)rand.NextDouble() - 0.5f) * wobble;
                if (axis != 1)
                    velocity.Y += ((float)rand.NextDouble() - 0.5f) * wobble;
                if (axis != 2)
                    velocity.Z += ((float)rand.NextDouble() - 0.5f) * wobble;

                var at = origin.AddCopy(x, y, z);
                var props = new SimpleParticleProperties(
                    1,
                    1,
                    ColorUtil.WhiteArgb,
                    at,
                    at,
                    velocity,
                    velocity,
                    life,
                    0f,
                    0.28f,
                    0.42f,
                    EnumParticleModel.Cube)
                {
                    WindAffected = false,
                    WithTerrainCollision = false,
                    VertexFlags = 255,
                    OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -520f),
                    SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -0.45f)
                };
                props.Color = GearColor;
                props.LightEmission = GearColor;
                world.SpawnParticles(props);
            }
        }
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            toggleInventoryDialogClient(byPlayer, () =>
                new GuiDialogEStorageAssembler(
                    Lang.Get("electricalprogressivestorage:estorage-assembler-title"),
                    Inventory,
                    Pos,
                    (ICoreClientAPI)Api,
                    this));
        }

        return true;
    }

    public bool ReadyFor(IWorldAccessor world, ItemStack pattern)
    {
        var grid = GridEmpty();
        var output = _inventory[InventoryEStorageAssembler.Output].Empty;
        if (grid && output)
            return true;
        if (grid)
            return false;
        return GridMatches(world, pattern);
    }

    public bool CraftReady(IWorldAccessor world, ItemStack pattern)
    {
        var served = LiquidCraft.Mask(world, pattern, LiquidCraft.Player(world, Pos));
        for (var i = 0; i < 9; i++)
        {
            var cell = ItemEStoragePattern.Cell(world, pattern, i);
            var slot = _inventory[i];
            if (served[i])
            {
                if (!slot.Empty)
                    return false;
                continue;
            }

            if (cell == null)
            {
                if (!slot.Empty)
                    return false;
                continue;
            }

            var size = Math.Max(1, cell.StackSize);
            if (slot.Empty || slot.Itemstack == null || slot.Itemstack.StackSize < size)
                return false;
            if (!StorageAccess.Same(world, slot.Itemstack, cell))
                return false;
        }

        return true;
    }

    public bool BusyGrid()
    {
        if (!_inventory[InventoryEStorageAssembler.Output].Empty)
            return true;
        return !GridEmpty();
    }

    public bool Serves(BlockPos panel)
        => _owner != null && ProcessorCluster.SamePos(_owner, panel);

    public void PointOwner(BlockPos panel)
        => _owner = panel.Copy();

    /// <summary>
    /// Сетка или уже идущий крафт принадлежат этой панели.
    /// </summary>
    public bool ForPanel(IWorldAccessor world, ItemStack pattern, BlockPos panel)
    {
        if (_owner != null && !ProcessorCluster.SamePos(_owner, panel))
            return false;

        var id = ItemEStoragePattern.JobId(pattern);
        var craftId = ItemEStoragePattern.JobId(_pattern);
        if (craftId != 0 && id != 0)
            return craftId == id;

        if (_busy && _owner == null)
            return false;
        return Holding(world, pattern);
    }

    public void RefundGrid(IWorldAccessor world, BlockEntityEStorageProcessor? cpu)
    {
        Stop();
        if (cpu == null)
            return;

        var bytes = ProcessorCluster.At(world, cpu.Pos).Bytes;
        for (var i = 0; i < 9; i++)
        {
            var slot = _inventory[i];
            var stack = slot.Itemstack;
            if (stack == null || stack.StackSize <= 0)
                continue;

            var put = cpu.Insert(world, stack, stack.StackSize, bytes);
            if (put <= 0)
                continue;

            stack.StackSize -= put;
            if (stack.StackSize <= 0)
                slot.Itemstack = null;
            slot.MarkDirty();
        }
    }

    public bool Holding(IWorldAccessor world, ItemStack pattern)
    {
        if (GridEmpty() && _inventory[InventoryEStorageAssembler.Output].Empty)
            return false;
        if (!GridEmpty() && !GridMatches(world, pattern))
            return false;

        var output = _inventory[InventoryEStorageAssembler.Output].Itemstack;
        if (output == null)
            return !GridEmpty();

        var primary = ItemEStoragePattern.Primary(world, pattern);
        if (primary != null && StorageAccess.Same(world, primary, output))
            return true;
        return !GridEmpty();
    }

    private GridRecipe? MatchRecipe(IWorldAccessor world, out IPlayer? player, ItemStack? pattern = null)
    {
        player = Nearest(world);
        if (player == null)
            return null;

        var served = pattern == null ? null : LiquidCraft.Mask(world, pattern, player);
        var slots = new ItemSlot[9];
        for (var i = 0; i < 9; i++)
        {
            if (served != null && served[i] && _inventory[i].Empty)
            {
                var overlay = ItemEStoragePattern.Cell(world, pattern!, i);
                if (overlay != null)
                    overlay.StackSize = 1;
                slots[i] = new DummySlot(overlay);
                continue;
            }

            slots[i] = _inventory[i];
        }

        foreach (var recipe in world.GridRecipes)
        {
            if (!recipe.Enabled || recipe.Output?.ResolvedItemStack == null)
                continue;
            if (recipe.Matches(player, world, slots, 3))
                return recipe;
        }

        return null;
    }

    public bool TryCraft(IWorldAccessor world, ItemStack? expect)
    {
        var player = Nearest(world);
        if (player == null)
            return false;

        ItemStack? product = null;
        if (expect?.Collectible != null)
        {
            product = expect.Clone();
            product.StackSize = Math.Max(1, expect.StackSize);
            if (!OutputFits(world, product))
                return false;
        }

        var filled = new List<int>();
        BlockPos? network = null;
        BlockEntityEStorageProcessor? reserve = null;
        if (_pattern != null)
        {
            network = _owner ?? PanelStack.FeedingAny(world, Pos)?.Pos;
            if (_reserve != null)
                reserve = world.BlockAccessor.GetBlockEntity(_reserve) as BlockEntityEStorageProcessor;
            if (!LiquidCraft.Fill(world, reserve, network, _pattern, _inventory, filled, player))
                return false;
        }

        var matched = MatchRecipe(world, out player);
        if (player == null || matched?.Output?.ResolvedItemStack == null || matched == null)
        {
            LiquidCraft.ClearShells(world, reserve, network, _inventory, filled);
            return false;
        }

        if (product == null)
        {
            product = matched.Output.ResolvedItemStack.Clone();
            product.StackSize = Math.Max(1, matched.Output.StackSize);
            if (!OutputFits(world, product))
            {
                LiquidCraft.ClearShells(world, reserve, network, _inventory, filled);
                return false;
            }
        }

        var slots = new ItemSlot[9];
        for (var i = 0; i < 9; i++)
            slots[i] = _inventory[i];
        if (!matched.ConsumeInput(player, slots, 3))
        {
            LiquidCraft.ClearShells(world, reserve, network, _inventory, filled);
            return false;
        }

        LiquidCraft.ClearShells(world, reserve, network, _inventory, filled);

        _yield = product.Clone();
        var dest = _inventory[InventoryEStorageAssembler.Output];
        if (dest.Empty || dest.Itemstack == null)
            dest.Itemstack = product;
        else
            dest.Itemstack.StackSize += product.StackSize;
        dest.MarkDirty();
        MarkDirty();
        return true;
    }

    private bool OutputFits(IWorldAccessor world, ItemStack product)
    {
        var dest = _inventory[InventoryEStorageAssembler.Output];
        if (!dest.Empty && dest.Itemstack != null && !StorageAccess.Same(world, dest.Itemstack, product))
            return false;

        var have = dest.Empty || dest.Itemstack == null ? 0 : dest.Itemstack.StackSize;
        var cap = Math.Max(1, product.Collectible?.MaxStackSize ?? 1);
        return have + product.StackSize <= cap;
    }

    public void ReturnCraftSlots(IWorldAccessor world, BlockPos panel)
    {
        for (var i = 0; i < 9; i++)
            Spill(world, panel, _inventory[i]);
    }

    public void GiveAll(IWorldAccessor world, BlockPos panel)
    {
        for (var i = 0; i < _inventory.Count; i++)
            Spill(world, panel, _inventory[i]);
    }

    /// <summary>
    /// Заказ уже заполнил сетку и сейчас сам запустит крафт. Неполная закладка остаётся ему.
    /// </summary>
    private bool JobOwnsGrid(IWorldAccessor world)
    {
        foreach (var face in BlockFacing.ALLFACES)
        {
            var panel = PanelStack.Feeding(world, Pos.AddCopy(face), Pos);
            if (panel != null && panel.OwnsReadyCraft(world, this))
                return true;
        }

        return false;
    }

    private void Work()
    {
        var world = Api?.World;
        if (world == null || Api?.Side != EnumAppSide.Server)
            return;

        HandOutput(world);

        if (!_busy)
        {
            // Без питания Begin запирает сетку: прогресс стоит, а заказ видит Working и больше не кладёт предметы.
            if (!GridEmpty() && Powered(world) && !JobOwnsGrid(world))
                Begin(world, null);
            return;
        }

        var recipe = MatchRecipe(world, out var player, _pattern);
        if (player != null && recipe == null)
        {
            Stop();
            return;
        }

        if (_progress < 1f)
        {
            if (!Powered(world))
                return;

            var now = world.ElapsedMilliseconds;
            var step = _lastMs <= 0 ? 0 : now - _lastMs;
            _lastMs = now;
            if (step < 0)
                step = 0;
            if (step > 250)
                step = 250;

            var before = _progress;
            _progress += step / CraftMs;
            if (_progress > 1f)
                _progress = 1f;
            if (step > 0)
                PushProgress(_progress - before >= 0.04f || _progress >= 1f);
            if (_progress < 1f)
                return;
        }

        if (!TryCraft(world, _expect))
            return;

        var yield = _yield;
        var pattern = _pattern;
        // Ручная закладка остаётся в сетке и собирается дальше. В сеть уходит только хвост заказа интерфейса.
        if (_owner != null)
        {
            var craftId = ItemEStoragePattern.JobId(pattern);
            BlockEntityEStorageInterface? face = null;
            foreach (var candidate in PanelStack.Interfaces(world, _owner))
            {
                if (!candidate.Feeds(world, Pos))
                    continue;
                if (craftId != 0 && candidate.OwnsCraft(craftId))
                {
                    face = candidate;
                    break;
                }

                face ??= candidate;
            }

            var panel = face?.Pos ?? _owner;
            if (yield != null && face != null)
                face.NoteAssemblerCraft(yield, pattern);
            if (MatchRecipe(world, out _, pattern) == null)
                ReturnCraftSlots(world, panel);
        }

        HandOutput(world);
        Stop();
    }

    /// <summary>
    /// Выход сборщика забирает соседний интерфейс, в том числе после ручной закладки.
    /// </summary>
    private void HandOutput(IWorldAccessor world)
    {
        var slot = _inventory[InventoryEStorageAssembler.Output];
        if (slot.Empty || slot.Itemstack == null)
            return;

        foreach (var face in BlockFacing.ALLFACES)
        {
            var panel = PanelStack.Feeding(world, Pos.AddCopy(face), Pos);
            if (panel != null && panel.TakeAssemblerOutput(slot))
                return;
        }
    }

    private void Stop()
    {
        _busy = false;
        _progress = 0f;
        _expect = null;
        _pattern = null;
        _reserve = null;
        _owner = null;
        _yield = null;
        _lastMs = 0;
        PushProgress(true);
    }

    private void PushProgress(bool save)
    {
        if (save)
            MarkDirty();
        if (Api is ICoreServerAPI server)
            server.Network.BroadcastBlockEntityPacket(Pos, ProgressPacket, BitConverter.GetBytes(_busy ? _progress : 0f));
    }

    private bool Powered(IWorldAccessor world)
    {
        foreach (var face in BlockFacing.ALLFACES)
        {
            var neighbor = Pos.AddCopy(face);
            var panel = PanelStack.Feeding(world, neighbor, Pos);
            if (panel != null && StorageAccess.HasPower(world, StorageAccess.GetScan(world, panel.Pos)))
                return true;
        }

        return false;
    }

    private bool GridEmpty()
    {
        for (var i = 0; i < 9; i++)
        {
            if (!_inventory[i].Empty)
                return false;
        }

        return true;
    }

    private bool GridMatches(IWorldAccessor world, ItemStack pattern)
    {
        var any = false;
        for (var i = 0; i < 9; i++)
        {
            var cell = ItemEStoragePattern.Cell(world, pattern, i);
            var slot = _inventory[i];
            if (slot.Empty)
                continue;

            any = true;
            if (cell == null || slot.Itemstack == null || !StorageAccess.Same(world, slot.Itemstack, cell))
                return false;
        }

        return any || GridEmpty();
    }

    private void Spill(IWorldAccessor world, BlockPos panel, ItemSlot slot)
    {
        var stack = slot.Itemstack;
        if (stack == null || stack.StackSize <= 0)
            return;

        var moved = StorageAccess.Insert(world, panel, stack, stack.StackSize);
        stack.StackSize -= moved;
        if (stack.StackSize > 0)
            world.SpawnItemEntity(stack.Clone(), Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        slot.Itemstack = null;
        slot.MarkDirty();
    }

    private IPlayer? Nearest(IWorldAccessor world) => LiquidCraft.Player(world, Pos);

    /// <summary>
    /// Чанк пересобирается только когда в рамке меняется предмет. Выгрузка результата в сеть сетку не меняет.
    /// </summary>
    private void RefreshCenter()
    {
        var key = CenterKey();
        if (key == _shownCenter || Api?.World == null || Pos == null)
            return;

        _shownCenter = key;
        UpdateCenterMesh();
        Api.World.BlockAccessor.MarkBlockDirty(Pos);
    }

    private string? CenterKey()
    {
        var world = Api?.World;
        var shown = world == null ? null : ShownCenter(world);
        return shown?.Collectible?.Code?.ToString();
    }

    /// <summary>
    /// В центре рамки предмет только пока идёт сборка, либо пока готовый выход ещё не забрала сеть.
    /// Заполненная сетка между крафтами рамку не занимает.
    /// </summary>
    private ItemStack? ShownCenter(IWorldAccessor world)
    {
        var waiting = _inventory[InventoryEStorageAssembler.Output].Itemstack;
        if (waiting is { StackSize: > 0, Collectible: not null })
            return waiting;
        if (!_busy)
            return null;
        if (_expect?.Collectible != null)
            return _expect;
        return Preview(world);
    }

    private void UpdateCenterMesh()
    {
        _center = null;
        var world = Api?.World;
        if (_capi == null || world == null)
            return;

        var shown = ShownCenter(world);
        if (shown?.Collectible == null)
            return;

        var mesh = ProductMesh(shown);
        if (mesh == null)
            return;

        FitInCenter(mesh);
        mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, 20f * GameMath.DEG2RAD, 0f);
        _center = mesh;
    }

    private ItemStack? Preview(IWorldAccessor world)
    {
        var player = Nearest(world);
        var slots = new ItemSlot[9];
        for (var i = 0; i < 9; i++)
            slots[i] = _inventory[i];

        foreach (var recipe in world.GridRecipes)
        {
            if (!recipe.Enabled || recipe.Output?.ResolvedItemStack?.Collectible == null)
                continue;
            if (player == null || !recipe.Matches(player, world, slots, 3))
                continue;

            var stack = recipe.Output.ResolvedItemStack.Clone();
            stack.StackSize = 1;
            return stack;
        }

        return null;
    }

    private MeshData? ProductMesh(ItemStack stack)
    {
        if (_capi == null)
            return null;

        try
        {
            MeshData? mesh;
            if (stack.Class == EnumItemClass.Block && stack.Block != null)
                mesh = _capi.TesselatorManager.GetDefaultBlockMesh(stack.Block)?.Clone();
            else if (stack.Item != null)
            {
                _nowTesselatingObj = stack.Collectible;
                _nowTesselatingShape = stack.Item.Shape == null
                    ? null
                    : _capi.TesselatorManager.GetCachedShape(stack.Item.Shape.Base);
                _capi.Tesselator.TesselateItem(stack.Item, out mesh, this);
            }
            else
                return null;

            if (mesh == null || mesh.VerticesCount == 0 || mesh.RenderPassesAndExtraBits == null)
                return null;

            var pass = (short)EnumChunkRenderPass.OpaqueNoCull;
            for (var i = 0; i < mesh.RenderPassesAndExtraBits.Length; i++)
                mesh.RenderPassesAndExtraBits[i] = pass;
            LightUp(mesh);
            return mesh;
        }
        catch (Exception e)
        {
            Api?.World.Logger.Error("Не удалось нарисовать предмет в сборщике {0}: {1}", stack.Collectible?.Code, e.Message);
            return null;
        }
    }

    /// <summary>Ночью предмет в рамке остаётся ярким, иначе не видно, куда летят частицы.</summary>
    private static void LightUp(MeshData mesh)
    {
        var count = mesh.VerticesCount;
        if (count <= 0)
            return;
        if (mesh.Flags == null || mesh.Flags.Length < count)
            mesh.Flags = new int[count];

        var glow = 255 & VertexFlags.GlowLevelBitMask;
        for (var i = 0; i < count; i++)
            mesh.Flags[i] = (mesh.Flags[i] & ~VertexFlags.GlowLevelBitMask) | glow;
    }

    /// <summary>Самая длинная сторона становится 0.42 блока, центр сетки садится в центр рамки.</summary>
    private static void FitInCenter(MeshData mesh)
    {
        var xyz = mesh.xyz;
        var count = mesh.VerticesCount;
        if (xyz == null || count <= 0 || xyz.Length < count * 3)
            return;

        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var minZ = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;
        var maxZ = float.MinValue;
        for (var v = 0; v < count; v++)
        {
            var i = v * 3;
            var x = xyz[i];
            var y = xyz[i + 1];
            var z = xyz[i + 2];
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (z < minZ) minZ = z;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
            if (z > maxZ) maxZ = z;
        }

        var largest = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
        if (largest < 0.001f)
            return;

        var cx = (minX + maxX) * 0.5f;
        var cy = (minY + maxY) * 0.5f;
        var cz = (minZ + maxZ) * 0.5f;
        var scale = 0.42f / largest;
        mesh.Scale(new Vec3f(cx, cy, cz), scale, scale, scale);
        mesh.Translate(0.5f - cx, 0.5f - cy, 0.5f - cz);
    }

    private TextureAtlasPosition TexPos(AssetLocation texturePath)
    {
        var capi = _capi ?? throw new InvalidOperationException();
        var found = capi.BlockTextureAtlas[texturePath];
        if (found != null)
            return found;

        var path = texturePath.Clone();
        var cut = path.Path.IndexOf("++", StringComparison.Ordinal);
        if (cut >= 0)
            path.Path = path.Path.Substring(0, cut);

        TextureAtlasPosition? inserted = null;
        var asset = capi.Assets.TryGet(path.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
        if (asset != null)
        {
            capi.BlockTextureAtlas.GetOrInsertTexture(path, out _, out var placed, null, 0.005f);
            inserted = placed;
        }

        return inserted ?? capi.BlockTextureAtlas.UnknownTexturePosition;
    }
}
