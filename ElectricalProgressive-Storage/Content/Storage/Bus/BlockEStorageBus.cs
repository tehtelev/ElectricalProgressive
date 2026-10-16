using System;
using System.Collections.Generic;
using System.Text;
using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Storage.Bus;

/// <summary>
/// Плоская шина на уже проложенном кабеле. Под жилой — сундук, бочка или машина.
/// </summary>
public class BlockEStorageBus : BlockEStorageTerminal
{
    protected override bool MountsOn(IWorldAccessor world, BlockPos pos, Facing facing)
        => ContainerMount(world, pos, facing);

    protected override bool CanStay(IWorldAccessor world, BlockPos pos, Facing facing)
        => ContainerMount(world, pos, facing) && base.CanStay(world, pos, facing);
}

/// <summary>
/// Образец фильтра шины. В слоте остаётся тип предмета, размер всегда 1.
/// </summary>
public class ItemSlotBusFilter : ItemSlot
{
    public const int Columns = 9;

    public ItemSlotBusFilter(InventoryBase inventory) : base(inventory)
    {
    }

    public override bool CanTake() => false;

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        => false;

    public override bool CanHold(ItemSlot sourceSlot) => true;

    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        SetFilter(sourceSlot);
    }

    protected override void ActivateSlotRightClick(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        SetFilter(sourceSlot);
    }

    public override bool TryFlipWith(ItemSlot itemSlot)
    {
        SetFilter(itemSlot);
        return true;
    }

    /// <summary>В слоте только тип. Пустая рука снимает образец, курсор не забирается.</summary>
    private void SetFilter(ItemSlot? sourceSlot)
    {
        if (sourceSlot == null || sourceSlot.Empty || sourceSlot.Itemstack == null)
        {
            Itemstack = null;
            MarkDirty();
            return;
        }

        var ghost = sourceSlot.Itemstack.Clone();
        ghost.StackSize = 1;
        Itemstack = ghost;
        MarkDirty();
    }
}

/// <summary>
/// Девять образцов одной шины.
/// </summary>
public class InventoryEStorageBus : InventoryGeneric
{
    public InventoryEStorageBus()
        : base(ItemSlotBusFilter.Columns, "estoragebus", "0", null, NewSlot)
    {
    }

    private static ItemSlot NewSlot(int id, InventoryGeneric inventory)
        => new ItemSlotBusFilter(inventory);

    public override WeightedSlot GetBestSuitedSlot(ItemSlot sourceSlot, ItemStackMoveOperation op, List<ItemSlot> skipSlots)
        => new();
}

/// <summary>
/// Шина забирает или кладёт до восьми предметов за такт по образцам.
/// </summary>
public class BlockEntityEStorageBus : BlockEntityOpenableContainer, IPanelHost, IPanelStack
{
    // Карты скорости нет: за один проход уходит до восьми предметов.
    private const int Batch = 8;
    private const int TickMs = 200;

    private readonly InventoryEStorageBus _inventory;
    private readonly List<ItemStack> _filters = [];
    private int _cursor;
    public Facing Facing = Facing.UpNorth;
    public Facing Cable = Facing.None;
    public readonly int[] CableLines = new int[6];
    public string CableCode = "";
    public PanelStack Stack { get; } = new();
    public string Seat { get; set; } = "";

    Facing IPanelHost.Facing { get => Facing; set => Facing = value; }
    Facing IPanelHost.Cable { get => Cable; set => Cable = value; }
    int[] IPanelHost.CableLines => CableLines;
    string IPanelHost.CableCode { get => CableCode; set => CableCode = value; }

    public BlockEntityEStorageBus()
    {
        _inventory = new InventoryEStorageBus();
    }

    public override InventoryBase Inventory => _inventory;

    public override string InventoryClassName => "estoragebus";

    public bool Holds(string inventoryId) => inventoryId == _inventory.InventoryID;

    public bool Importing => Block?.FirstCodePart() == "estorageimport";

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        PanelStack.ClaimPromote(this);
        BlockEStorageTerminal.ClaimStick(this);
        _inventory.LateInitialize("estoragebus-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z + Seat, api);
        Stack.SpawnPending(api.World, Pos);
        if (api.Side == EnumAppSide.Server)
        {
            StorageAccess.DirtyTopology();
            RegisterGameTickListener(_ => Work(), TickMs);
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("facing", (int)Facing);
        tree.SetInt("cable", (int)Cable);
        tree.SetInt("cableLines", BlockEStorageCable.Pack(CableLines));
        tree.SetString("cableCode", CableCode ?? "");
        Stack.Write(tree);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        var raw = tree.GetInt("facing", (int)Facing.UpNorth);
        Facing = raw == 0 ? Facing.UpNorth : (Facing)raw;
        var cable = (Facing)tree.GetInt("cable");
        var packedLines = tree.GetInt("cableLines");
        var cableCode = tree.GetString("cableCode", "");
        if (cable != Facing.None && string.IsNullOrEmpty(cableCode))
            cableCode = "estoragecable";
        var hadCode = CableCode ?? "";
        if (Cable != Facing.None && string.IsNullOrEmpty(hadCode))
            hadCode = "estoragecable";
        if (cable != Cable || packedLines != BlockEStorageCable.Pack(CableLines) || cableCode != hadCode)
            StorageAccess.DirtyTopology();
        Cable = cable;
        for (var n = 0; n < CableLines.Length; n++)
            CableLines[n] = (packedLines >> (n * 3)) & 7;
        CableCode = cableCode;
        for (var i = 0; i < _inventory.Count; i++)
        {
            var stack = _inventory[i].Itemstack;
            if (stack != null)
                stack.StackSize = 1;
        }

        Stack.Read(tree, worldAccessForResolve, this);
    }

    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
    {
        if (Stack.Forward(this, fromPlayer, ref packetid, data))
            return;
        base.OnReceivedClientPacket(fromPlayer, packetid, data);
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Server)
        {
            byPlayer.InventoryManager.OpenInventory(Inventory);
            return true;
        }

        if (Api is not ICoreClientAPI capi)
            return true;

        if (invDialog != null)
        {
            invDialog.TryClose();
            return true;
        }

        var title = Importing
            ? "electricalprogressivestorage:estorage-import-title"
            : "electricalprogressivestorage:estorage-export-title";
        var hint = Importing
            ? "electricalprogressivestorage:estorage-import-hint"
            : "electricalprogressivestorage:estorage-export-hint";
        var dialog = new GuiDialogEStorageBus(Lang.Get(title), Lang.Get(hint), Inventory, Pos, capi, Facing);
        dialog.OnClosed += () =>
        {
            if (ReferenceEquals(invDialog, dialog))
                invDialog = null;
        };
        invDialog = dialog;
        if (dialog.IsDuplicate || !dialog.TryOpen())
        {
            invDialog = null;
            return true;
        }

        capi.Network.SendPacketClient(Inventory.Open(byPlayer));
        capi.Network.SendBlockEntityPacket(Pos, PanelStack.FacePacket(Facing, (int)EnumBlockEntityPacketId.Open));
        return true;
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        if (Api?.World == null)
            return;

        dsc.AppendLine(Lang.Get(BlockEStorageTerminal.LinkKey(StorageAccess.Link(Api.World, Pos))));
        foreach (var (used, capacity) in StorageAccess.CableLoads(Api.World, Pos))
            dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-channels", used, capacity));
    }

    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        Stack.ReleaseContents(byPlayer);
        for (var i = 0; i < _inventory.Count; i++)
            _inventory[i].Itemstack = null;

        base.OnBlockBroken(byPlayer);
    }

    public override void OnBlockRemoved()
    {
        if (!Stack.Keep)
            Stack.Discard();
        base.OnBlockRemoved();
        StorageAccess.DirtyTopology();
    }

    private void Work()
    {
        var world = Api?.World;
        if (world == null || Pos == null)
            return;
        if (StorageAccess.Link(world, Pos) != StorageLink.Online)
            return;
        if (!Mounted(out var target, out var face))
            return;

        if (Importing)
            Pull(world, target);
        else
            Push(world, target, face);
    }

    private void Pull(IWorldAccessor world, InventoryBase target)
    {
        var count = target.Count;
        if (count <= 0)
            return;

        ReadFilters();
        var start = _cursor % count;
        if (start < 0)
            start = 0;

        for (var n = 0; n < count; n++)
        {
            var index = (start + n) % count;
            var slot = target[index];
            var stack = slot?.Itemstack;
            if (slot == null || stack?.Collectible == null || stack.StackSize <= 0 || !slot.CanTake())
                continue;
            if (stack.Collectible.IsLiquid() || !Wanted(stack))
                continue;

            var room = StorageAccess.RoomFor(world, Pos, stack);
            var want = Math.Min(Batch, Math.Min(stack.StackSize, room));
            if (want <= 0)
                continue;

            var taken = slot.TakeOut(want);
            slot.MarkDirty();
            if (taken == null || taken.StackSize <= 0)
                continue;

            var moved = StorageAccess.Insert(world, Pos, taken, taken.StackSize);
            var left = taken.StackSize - moved;
            if (left > 0)
                ReturnToSlot(slot, taken, left);
            _cursor = index + 1;
            return;
        }
    }

    private void Push(IWorldAccessor world, InventoryBase target, BlockFacing face)
    {
        ReadFilters();
        if (_filters.Count == 0)
            return;

        var start = _cursor % _filters.Count;
        if (start < 0)
            start = 0;

        for (var n = 0; n < _filters.Count; n++)
        {
            var index = (start + n) % _filters.Count;
            var proto = _filters[index];
            if (proto.Collectible == null || proto.Collectible.IsLiquid())
                continue;

            var dest = Destination(world, target, face, proto);
            if (dest == null)
                continue;

            var room = RoomIn(world, dest, proto);
            var want = Math.Min(Batch, room);
            if (want <= 0)
                continue;

            var got = StorageAccess.Extract(world, Pos, proto, want);
            if (got == null || got.StackSize <= 0)
                continue;

            var holder = new DummySlot(got);
            var op = new ItemStackMoveOperation(world, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, got.StackSize);
            holder.TryPutInto(dest, ref op);
            dest.MarkDirty();
            if (got.StackSize > 0)
                ReturnToNetwork(world, got);
            _cursor = index + 1;
            return;
        }
    }

    private bool Mounted(out InventoryBase inventory, out BlockFacing face)
    {
        inventory = null!;
        face = BlockFacing.NORTH;
        if (Api?.World == null || Facing == Facing.None || Pos == null)
            return false;

        var toward = PanelStack.Support(Facing);
        if (toward == null)
            return false;

        var neighborPos = Pos.AddCopy(toward);
        var neighbor = Api.World.BlockAccessor.GetBlock(neighborPos);
        if (StorageAccess.IsPart(neighbor))
            return false;

        var entity = Api.World.BlockAccessor.GetBlockEntity(neighborPos);
        var found = entity is BlockEntityContainer container
            ? container.Inventory
            : (entity as IBlockEntityContainer)?.Inventory as InventoryBase;
        if (found == null)
            return false;

        inventory = found;
        face = toward.Opposite;
        return true;
    }

    private void ReadFilters()
    {
        _filters.Clear();
        var world = Api?.World;
        if (world == null)
            return;

        for (var i = 0; i < _inventory.Count; i++)
        {
            var stack = _inventory[i].Itemstack;
            if (stack?.Collectible == null || stack.Collectible.IsLiquid())
                continue;

            var seen = false;
            foreach (var have in _filters)
            {
                if (!StorageAccess.Same(world, have, stack))
                    continue;
                seen = true;
                break;
            }

            if (!seen)
                _filters.Add(stack);
        }
    }

    private bool Wanted(ItemStack stack)
    {
        if (_filters.Count == 0)
            return true;

        var world = Api?.World;
        if (world == null)
            return false;

        foreach (var filter in _filters)
        {
            if (StorageAccess.Same(world, filter, stack))
                return true;
        }

        return false;
    }

    private static ItemSlot? Destination(IWorldAccessor world, InventoryBase target, BlockFacing face, ItemStack sample)
    {
        var probe = sample.Clone();
        probe.StackSize = 1;
        var holder = new DummySlot(probe);
        var auto = target.GetAutoPushIntoSlot(face, holder);
        if (auto != null && RoomIn(world, auto, sample) > 0)
            return auto;

        ItemSlot? empty = null;
        for (var i = 0; i < target.Count; i++)
        {
            var slot = target[i];
            if (slot == null || !slot.CanTakeFrom(holder))
                continue;
            if (RoomIn(world, slot, sample) <= 0)
                continue;
            if (!slot.Empty)
                return slot;
            empty ??= slot;
        }

        return empty;
    }

    private static int RoomIn(IWorldAccessor world, ItemSlot dest, ItemStack sample)
    {
        if (!dest.Empty && dest.Itemstack != null && !StorageAccess.Same(world, dest.Itemstack, sample))
            return 0;

        var max = Math.Max(1, sample.Collectible?.MaxStackSize ?? 1);
        var slotMax = dest.MaxSlotStackSize;
        if (slotMax > 0 && slotMax < max)
            max = slotMax;
        var have = dest.Empty || dest.Itemstack == null ? 0 : dest.Itemstack.StackSize;
        return Math.Max(0, max - have);
    }

    private static void ReturnToSlot(ItemSlot slot, ItemStack taken, int left)
    {
        taken.StackSize = left;
        if (slot.Empty || slot.Itemstack == null)
            slot.Itemstack = taken;
        else
            slot.Itemstack.StackSize += left;
        slot.MarkDirty();
    }

    private void ReturnToNetwork(IWorldAccessor world, ItemStack stack)
    {
        if (Pos == null || stack.StackSize <= 0)
            return;

        var back = StorageAccess.Insert(world, Pos, stack, stack.StackSize);
        var left = stack.StackSize - back;
        if (left <= 0)
            return;

        var drop = stack.Clone();
        drop.StackSize = left;
        world.SpawnItemEntity(drop, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }
}
