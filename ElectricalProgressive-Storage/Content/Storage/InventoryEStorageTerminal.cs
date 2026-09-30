using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage;

public class ItemSlotNetwork : ItemSlot
{
    public int PublishedSize;
    private static int _takeDepth;

    public ItemSlotNetwork(InventoryBase inventory) : base(inventory)
    {
    }

    public override int MaxSlotStackSize => 999999;

    public bool CraftOnly => Itemstack?.Attributes.GetBool(StorageAccess.CraftGhostKey) == true;

    public bool CanOrder => CraftOnly || Itemstack?.Attributes.GetBool(StorageAccess.OrderKey) == true;

    public override bool CanTake() => !CraftOnly && base.CanTake();

    private InventoryEStorageTerminal View => (InventoryEStorageTerminal)Inventory;

    public override int GetRemainingSlotSpace(ItemStack forStack)
    {
        var view = View;
        if (view.Api?.World == null || forStack == null)
            return 0;
        if (Itemstack != null && !StorageAccess.Same(view.Api.World, Itemstack, forStack))
            return 0;

        return view.RoomForCached(forStack);
    }

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority)
    {
        if (CraftOnly || sourceSlot.Empty || !sourceSlot.CanTake() || View.Api?.World == null)
            return false;
        if (Itemstack != null && !StorageAccess.Same(View.Api.World, Itemstack, sourceSlot.Itemstack))
            return false;

        return GetRemainingSlotSpace(sourceSlot.Itemstack) > 0;
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (sourceSlot.Empty || View.Api?.World == null)
            return false;
        if (Itemstack != null && !StorageAccess.Same(View.Api.World, Itemstack, sourceSlot.Itemstack))
            return false;

        return GetRemainingSlotSpace(sourceSlot.Itemstack) > 0;
    }

    public override ItemStack TakeOut(int quantity)
    {
        if (CraftOnly || Itemstack == null || quantity <= 0)
            return null!;

        quantity = Math.Min(quantity, Itemstack.StackSize);
        var view = View;
        if (_takeDepth == 0 && !view.Rebuilding && view.Api?.Side == EnumAppSide.Server && view.Api.World != null)
        {
            _takeDepth++;
            try
            {
                var taken = view.Pull(Itemstack, quantity);
                if (taken == null)
                    return null!;

                ShrinkLocal(taken.StackSize);
                return taken;
            }
            finally
            {
                _takeDepth--;
            }
        }

        if (quantity >= Itemstack.StackSize)
            return DetachLocal();

        var split = Itemstack.Clone();
        split.StackSize = quantity;
        ShrinkLocal(quantity);
        return split;
    }

    public override ItemStack TakeOutWhole()
    {
        if (Itemstack == null)
            return null!;

        return TakeOut(Itemstack.StackSize);
    }

    private void ShrinkLocal(int amount)
    {
        if (Itemstack == null || amount <= 0)
            return;

        Itemstack.StackSize -= amount;
        if (Itemstack.StackSize <= 0)
            Itemstack = null;
        PublishedSize = Itemstack?.StackSize ?? 0;
    }

    private ItemStack DetachLocal()
    {
        var stack = Itemstack;
        Itemstack = null;
        PublishedSize = 0;
        return stack!;
    }

    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        if (CraftOnly)
            return;

        var view = View;
        if (view.Api?.World == null)
            return;

        if (!sourceSlot.Empty)
        {
            if (Itemstack != null && StorageAccess.Same(view.Api.World, Itemstack, sourceSlot.Itemstack))
            {
                var room = StorageAccess.RoomFor(view.Api.World, view.Origin, sourceSlot.Itemstack);
                var maxStack = Math.Max(1, sourceSlot.Itemstack.Collectible?.MaxStackSize ?? 1);
                var move = Math.Min(sourceSlot.StackSize, Math.Min(room, maxStack));
                if (move <= 0)
                    return;

                var movedStack = sourceSlot.TakeOut(move);
                if (movedStack == null)
                    return;

                Itemstack.StackSize += movedStack.StackSize;
                op.MovedQuantity += movedStack.StackSize;
                sourceSlot.OnItemSlotModified(sourceSlot.Itemstack);
                OnItemSlotModified(Itemstack);
                return;
            }

            if (Itemstack != null)
                return;
            if (StorageAccess.RoomFor(view.Api.World, view.Origin, sourceSlot.Itemstack) <= 0)
                return;

            base.ActivateSlot(sourceSlot, ref op);
            return;
        }

        if (Empty)
            return;

        var max = Itemstack.Collectible.MaxStackSize;
        var qty = op.MouseButton == EnumMouseButton.Right ? 1 : max;
        qty = System.Math.Min(qty, Itemstack.StackSize);
        var taken = TakeOut(qty);
        if (taken == null)
            return;

        sourceSlot.Itemstack = taken;
        op.MovedQuantity += taken.StackSize;
        sourceSlot.OnItemSlotModified(taken);
        OnItemSlotModified(Itemstack);
    }

    public override void OnItemSlotModified(ItemStack sinkStack)
    {
        base.OnItemSlotModified(sinkStack);
        View.OnViewModified(this);
    }
}

public class InventoryEStorageTerminal : InventoryGeneric
{
    public BlockPos Origin = new(0, 0, 0);
    public bool Rebuilding;
    public Action? AfterRebuild;

    private ItemStack? _roomStack;
    private int _room;
    private long _roomGeneration = long.MinValue;

    public InventoryEStorageTerminal()
        : this(false, "estorageterminal")
    {
    }

    protected InventoryEStorageTerminal(bool liquid)
        : this(liquid, liquid ? "estorageliquidterminal" : "estorageterminal")
    {
    }

    protected InventoryEStorageTerminal(bool liquid, string className)
        : base(StorageAccess.ViewSlots, className, "0", null,
            (_, inv) => liquid ? new ItemSlotLiquid(inv) : new ItemSlotNetwork(inv))
    {
    }

    public virtual int Room(ItemStack proto)
        => Api?.World == null ? 0 : StorageAccess.RoomFor(Api.World, Origin, proto);

    public virtual int Push(ItemStack proto, int count)
        => Api?.World == null ? 0 : StorageAccess.Insert(Api.World, Origin, proto, count);

    public virtual ItemStack? Pull(ItemStack proto, int quantity)
        => Api?.World == null ? null : StorageAccess.Extract(Api.World, Origin, proto, quantity);

    public virtual List<ItemStack> Collect()
        => Api?.World == null ? new List<ItemStack>() : StorageAccess.Aggregate(Api.World, Origin);

    /// <summary>
    /// Шифт по хотбару спрашивает свободное место у каждой из 300 ячеек.
    /// Без этого каждая ячейка заново читает все диски, и игра надолго замирает.
    /// </summary>
    public int RoomForCached(ItemStack proto)
    {
        if (Api?.World == null || proto?.Collectible == null)
            return 0;

        var generation = StorageAccess.ContentGeneration;
        if (_roomGeneration == generation && _roomStack != null && StorageAccess.Same(Api.World, _roomStack, proto))
            return _room;

        _room = Room(proto);
        _roomStack = proto;
        _roomGeneration = generation;
        return _room;
    }

    /// <summary>
    /// Шифт в уже занятую ячейку. Ваниль останавливается на MaxStackSize,
    /// здесь вся стопка из хотбара дописывается в ту же ячейку и на диск.
    /// </summary>
    public void AcceptMerge(ItemStackMergeOperation op)
    {
        var source = op.SourceSlot;
        if (source?.Itemstack?.Collectible == null || op.SinkSlot is not ItemSlotNetwork dest || Api?.World == null)
            return;
        if (dest.Itemstack != null && !StorageAccess.Same(Api.World, dest.Itemstack, source.Itemstack))
            return;

        var room = RoomForCached(source.Itemstack);
        if (room <= 0)
            return;

        var maxStack = Math.Max(1, source.Itemstack.Collectible.MaxStackSize);
        var move = source is ItemSlotCreative
            ? Math.Min(maxStack, room)
            : Math.Min(source.StackSize, room);
        if (move <= 0)
            return;

        var taken = source.TakeOut(move);
        if (taken == null)
            return;

        if (dest.Empty)
            dest.Itemstack = taken;
        else
            dest.Itemstack.StackSize += taken.StackSize;

        if (source is ItemSlotCreative)
        {
            // Креатив сам не пустеет, а запрошенное число бывает отрицательным,
            // когда в ячейке уже больше обычного стака. Иначе шифт крутится тысячи раз.
            dest.OnItemSlotModified(dest.Itemstack);
            op.MovedQuantity = op.RequestedQuantity;
            return;
        }

        op.MovedQuantity += taken.StackSize;
    }

    public static bool MergeIntoTerminal(ItemStackMergeOperation op)
    {
        if (op?.SinkSlot is not ItemSlotNetwork || op.SinkSlot.Inventory is not InventoryEStorageTerminal term)
            return true;

        term.AcceptMerge(op);
        return false;
    }

    public override WeightedSlot GetBestSuitedSlot(ItemSlot sourceSlot, ItemStackMoveOperation op, List<ItemSlot> skipSlots)
    {
        var result = new WeightedSlot();
        if (sourceSlot?.Itemstack == null || Api?.World == null)
            return result;

        ItemSlotNetwork? match = null;
        ItemSlotNetwork? empty = null;
        for (var i = 0; i < Count; i++)
        {
            if (this[i] is not ItemSlotNetwork slot)
                continue;
            if (skipSlots != null && skipSlots.Contains(slot))
                continue;

            if (slot.CraftOnly)
                continue;

            if (slot.Empty)
            {
                empty ??= slot;
                continue;
            }

            if (match == null && StorageAccess.Same(Api.World, slot.Itemstack, sourceSlot.Itemstack))
                match = slot;
        }

        var priority = op?.CurrentPriority ?? EnumMergePriority.AutoMerge;
        ItemSlotNetwork? chosen = null;
        var merging = false;
        if (match != null && match.CanTakeFrom(sourceSlot, priority))
        {
            chosen = match;
            merging = true;
        }
        else if (empty != null && empty.CanTakeFrom(sourceSlot, priority))
            chosen = empty;

        if (chosen == null)
            return result;

        result.slot = chosen;
        result.weight = GetSuitability(sourceSlot, chosen, merging);
        return result;
    }

    public override object ActivateSlot(int slotId, ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        if (op.ShiftDown && TryTakeOneStack(slotId, op))
            return InvNetworkUtil.GetActivateSlotPacket(slotId, op);

        return base.ActivateSlot(slotId, sourceSlot, ref op);
    }

    /// <summary>
    /// Shift забирает один стак предмета. Остаток в ячейке не трогаем.
    /// </summary>
    private bool TryTakeOneStack(int slotId, ItemStackMoveOperation op)
    {
        if (slotId < 0 || slotId >= Count || this[slotId] is not ItemSlotNetwork net || net.CraftOnly)
            return false;
        if (net.Itemstack?.Collectible == null || op.ActingPlayer == null)
            return false;

        var max = Math.Max(1, net.Itemstack.Collectible.MaxStackSize);
        if (net.Itemstack.StackSize <= max)
            return false;

        var taken = net.TakeOut(max);
        if (taken == null)
            return false;

        if (!op.ActingPlayer.InventoryManager.TryGiveItemstack(taken, true) && taken.StackSize > 0)
        {
            if (Api?.Side == EnumAppSide.Server && Api.World != null)
                Push(taken, taken.StackSize);
            else if (net.Itemstack == null)
                net.Itemstack = taken;
            else
                net.Itemstack.StackSize += taken.StackSize;
        }

        if (Api?.Side == EnumAppSide.Server)
            Rebuild();

        return true;
    }

    public void OnViewModified(ItemSlotNetwork slot)
    {
        if (Rebuilding || Api?.Side != EnumAppSide.Server || Api.World == null)
            return;

        var now = slot.Itemstack?.StackSize ?? 0;
        if (slot.Itemstack != null && now > slot.PublishedSize)
        {
            var delta = now - slot.PublishedSize;
            var proto = slot.Itemstack.Clone();
            proto.Attributes.RemoveAttribute(StorageAccess.CraftGhostKey);
            proto.Attributes.RemoveAttribute(StorageAccess.OrderKey);
            var moved = Push(proto, delta);
            var leftover = delta - moved;
            if (leftover > 0)
            {
                var drop = slot.Itemstack.Clone();
                drop.StackSize = leftover;
                Api.World.SpawnItemEntity(drop, Origin.ToVec3d().Add(0.5, 0.5, 0.5));
            }
        }

        Rebuild();
    }

    public bool Rebuild()
    {
        if (Api?.World == null)
            return false;

        Rebuilding = true;
        var stacks = Collect();
        foreach (var stack in stacks)
        {
            stack.Attributes.RemoveAttribute(StorageAccess.CraftGhostKey);
            stack.Attributes.RemoveAttribute(StorageAccess.OrderKey);
        }

        AppendCrafts(stacks);

        var changed = false;
        for (var i = 0; i < Count; i++)
        {
            var slot = (ItemSlotNetwork)this[i];
            ItemStack? next = i < stacks.Count ? stacks[i] : null;
            var prevSize = slot.Itemstack?.StackSize ?? 0;
            var nextSize = next?.StackSize ?? 0;
            var nextCraft = next?.Attributes.GetBool(StorageAccess.CraftGhostKey) == true;
            var sameKind = next != null && slot.Itemstack != null
                && StorageAccess.Same(Api.World, slot.Itemstack, next)
                && prevSize == nextSize
                && slot.CraftOnly == nextCraft;
            var same = next == null ? slot.Itemstack == null : sameKind;
            if (!same)
            {
                slot.Itemstack = next;
                changed = true;
            }

            slot.PublishedSize = slot.Itemstack?.StackSize ?? 0;
        }

        Rebuilding = false;
        if (changed)
            AfterRebuild?.Invoke();
        return changed;
    }

    private void AppendCrafts(System.Collections.Generic.List<ItemStack> stacks)
    {
        if (this is InventoryEStorageLiquid or InventoryEStoragePattern)
            return;
        if (Api?.World == null || stacks.Count >= Count)
            return;
        if (StorageAccess.Link(Api.World, Origin) != StorageLink.Online)
            return;

        var scan = StorageAccess.GetScan(Api.World, Origin);
        if (scan.Conflict)
            return;

        foreach (var pos in scan.Interfaces)
        {
            if (stacks.Count >= Count)
                return;
            if (Api.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityEStorageInterface panel)
                panel.CollectOutputs(stacks, Count);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree)
    {
        Rebuilding = true;
        var loaded = new ItemSlot[Count];
        for (var i = 0; i < loaded.Length; i++)
            loaded[i] = new ItemSlot(this);
        SlotsFromTreeAttributes(tree, loaded, null);
        for (var i = 0; i < Count; i++)
        {
            var slot = (ItemSlotNetwork)this[i];
            slot.Itemstack = loaded[i].Itemstack;
            slot.PublishedSize = slot.Itemstack?.StackSize ?? 0;
        }

        Rebuilding = false;
    }
}

public class ItemSlotLiquid : ItemSlotNetwork
{
    public ItemSlotLiquid(InventoryBase inventory) : base(inventory)
    {
    }

    public override int MaxSlotStackSize => 1_000_000_000;
}

public class InventoryEStorageLiquid : InventoryEStorageTerminal
{
    public InventoryEStorageLiquid()
        : base(true)
    {
    }

    public override int Room(ItemStack proto)
        => Api?.World == null ? 0 : StorageAccess.RoomForLiquid(Api.World, Origin, proto);

    public override int Push(ItemStack proto, int count)
        => Api?.World == null ? 0 : StorageAccess.InsertLiquid(Api.World, Origin, proto, count);

    public override ItemStack? Pull(ItemStack proto, int quantity)
        => Api?.World == null ? null : StorageAccess.ExtractLiquid(Api.World, Origin, proto, quantity);

    public override List<ItemStack> Collect()
        => Api?.World == null ? new List<ItemStack>() : StorageAccess.AggregateLiquid(Api.World, Origin);
}
