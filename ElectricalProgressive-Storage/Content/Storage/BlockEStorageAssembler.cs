using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Storage;

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
public class BlockEntityEStorageAssembler : BlockEntityOpenableContainer
{
    public const float WorkWatts = 24f;
    public const int ProgressPacket = 19112;
    private const float CraftMs = 8000f;

    private readonly InventoryEStorageAssembler _inventory = new();
    private ItemStack? _expect;
    private ItemStack? _pattern;
    private BlockPos? _reserve;
    private BlockPos? _owner;
    private ItemStack? _yield;
    private bool _busy;
    private float _progress;
    private long _lastMs;

    public bool Working => _busy;
    public float Progress => _busy ? Math.Clamp(_progress, 0f, 1f) : 0f;

    public BlockEntityEStorageAssembler()
    {
        _inventory.SlotModified += _ => MarkDirty();
    }

    public override InventoryBase Inventory => _inventory;

    public override string InventoryClassName => "estorageassembler";

    public ItemSlot CraftSlot(int index) => _inventory[index];

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        _inventory.LateInitialize("estorageassembler-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(_ => Work(), 100);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetFloat("craft", Progress);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        _progress = tree.GetFloat("craft");
        _busy = _progress > 0f;
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
        if (MatchRecipe(world, out _, pattern) == null)
            return false;

        _expect = expect?.Clone();
        if (_expect != null && _expect.StackSize < 1)
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
            _busy = _progress > 0f;
            return;
        }

        base.OnReceivedServerPacket(packetid, data);
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
            network = _owner ?? MountedPanel(world);
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

    public bool InterfaceIsFeeding()
    {
        var world = Api?.World;
        if (world == null)
            return false;

        foreach (var face in BlockFacing.ALLFACES)
        {
            var neighbor = Pos.AddCopy(face);
            if (world.BlockAccessor.GetBlockEntity(neighbor) is not BlockEntityEStorageInterface panel)
                continue;
            var mount = panel.MountedOn();
            if (mount == null || !ProcessorCluster.SamePos(mount, Pos))
                continue;
            if (panel.FeedsAssembler())
                return true;
        }

        return false;
    }

    private void Work()
    {
        var world = Api?.World;
        if (world == null || Api?.Side != EnumAppSide.Server)
            return;

        if (!_busy)
        {
            if (!InterfaceIsFeeding() && !GridEmpty())
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
        BlockPos? panel = null;
        if (_owner != null && world.BlockAccessor.GetBlockEntity(_owner) is BlockEntityEStorageInterface)
            panel = _owner;
        panel ??= MountedPanel(world) ?? Pos;
        if (yield != null && world.BlockAccessor.GetBlockEntity(panel) is BlockEntityEStorageInterface face)
            face.NoteAssemblerCraft(yield, pattern);
        ReturnCraftSlots(world, panel);
        Stop();
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
            if (world.BlockAccessor.GetBlockEntity(neighbor) is not BlockEntityEStorageInterface panel)
                continue;
            var mount = panel.MountedOn();
            if (mount == null || !ProcessorCluster.SamePos(mount, Pos))
                continue;
            if (StorageAccess.HasPower(world, StorageAccess.GetScan(world, panel.Pos)))
                return true;
        }

        return false;
    }

    private BlockPos? MountedPanel(IWorldAccessor world)
    {
        foreach (var face in BlockFacing.ALLFACES)
        {
            var neighbor = Pos.AddCopy(face);
            if (world.BlockAccessor.GetBlockEntity(neighbor) is not BlockEntityEStorageInterface panel)
                continue;
            var mount = panel.MountedOn();
            if (mount != null && ProcessorCluster.SamePos(mount, Pos))
                return neighbor;
        }

        return null;
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
}
