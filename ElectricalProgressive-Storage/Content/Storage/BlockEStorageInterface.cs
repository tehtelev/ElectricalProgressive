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

namespace ElectricalProgressive.Content.Storage;

/// <summary>
/// Плоский интерфейс сети. Три ряда: кладёт в машину, шаблоны, образец что держать.
/// </summary>
public class BlockEStorageInterface : BlockEStorageTerminal
{
    protected override bool CanStay(IWorldAccessor world, BlockPos pos, Facing facing)
        => base.CanStay(world, pos, facing) || CollidesWithFace(world.BlockAccessor, pos, facing);

    /// <summary>
    /// Жернова и другие машины часто без SideSolid, но коллизия грани есть.
    /// </summary>
    private static bool CollidesWithFace(IBlockAccessor accessor, BlockPos pos, Facing facing)
    {
        BlockFacing? support = null;
        foreach (var face in FacingHelper.Faces(facing))
        {
            support = face;
            break;
        }

        if (support == null)
            return false;

        var neighborPos = pos.AddCopy(support);
        var block = accessor.GetBlock(neighborPos);
        var boxes = block?.GetCollisionBoxes(accessor, neighborPos);
        if (boxes == null)
            return false;

        var touched = support.Opposite;
        const float edge = 0.001f;
        foreach (var box in boxes)
        {
            if (box != null && Reaches(box, touched, edge))
                return true;
        }

        return false;
    }

    private static bool Reaches(Cuboidf box, BlockFacing side, float edge)
    {
        var overlapsX = box.X2 > edge && box.X1 < 1f - edge;
        var overlapsY = box.Y2 > edge && box.Y1 < 1f - edge;
        var overlapsZ = box.Z2 > edge && box.Z1 < 1f - edge;
        if (side == BlockFacing.UP)
            return box.Y2 >= 1f - edge && overlapsX && overlapsZ;
        if (side == BlockFacing.DOWN)
            return box.Y1 <= edge && overlapsX && overlapsZ;
        if (side == BlockFacing.NORTH)
            return box.Z1 <= edge && overlapsX && overlapsY;
        if (side == BlockFacing.SOUTH)
            return box.Z2 >= 1f - edge && overlapsX && overlapsY;
        if (side == BlockFacing.EAST)
            return box.X2 >= 1f - edge && overlapsY && overlapsZ;
        if (side == BlockFacing.WEST)
            return box.X1 <= edge && overlapsY && overlapsZ;
        return false;
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel == null || !world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityEStorageInterface panel)
            return panel.OnPlayerRightClick(byPlayer, blockSel);

        return false;
    }

    public override bool IsReplacableBy(Vintagestory.API.Common.Block block)
        => block is BlockEStorageCable;

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        var boxes = base.GetSelectionBoxes(blockAccessor, pos);
        if (api?.World == null
            || blockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageInterface panel
            || panel.Cable == Facing.None)
            return boxes;

        var extra = BlockEStorageCable.HostBoxes(api.World, panel);
        if (extra.Length == 0)
            return boxes;

        var merged = new Cuboidf[boxes.Length + extra.Length];
        Array.Copy(boxes, merged, boxes.Length);
        Array.Copy(extra, 0, merged, boxes.Length, extra.Length);
        return merged;
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        if (world.Side != EnumAppSide.Server)
            return;
        if (world.BlockAccessor.GetBlock(pos) is not BlockEStorageInterface)
            return;
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageInterface panel || panel.Cable == Facing.None)
            return;

        var face = BlockFacing.FromVector(neibpos.X - pos.X, neibpos.Y - pos.Y, neibpos.Z - pos.Z);
        if (face == null)
            return;

        var mask = FacingHelper.FromFace(face);
        if ((panel.Cable & mask) == Facing.None)
            return;
        if (MyMiniLib.CheckSolidFace(world.BlockAccessor, pos, mask) || SupportFace(panel.Facing) == face)
            return;

        DropCable(world, pos, panel, mask);
    }

    public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageInterface panel || panel.Cable == Facing.None)
        {
            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
            return;
        }

        if (api is ICoreClientAPI)
            return;

        var selected = Facing.None;
        if (byPlayer?.CurrentBlockSelection is { } sel && sel.Position != null && sel.Position.Equals(pos) && sel.HitPosition != null)
            selected = BlockEStorageCable.HostHit(world, panel, sel.HitPosition);

        if (selected == Facing.None)
        {
            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
            return;
        }

        DropCable(world, pos, panel, selected);
    }

    public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
    {
        var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageInterface panel || panel.Cable == Facing.None)
            return drops;

        var count = BlockEStorageCable.HostStrands(panel.Cable, panel.CableLines);
        var cable = BlockEStorageCable.HostBlock(world, panel.CableCode);
        if (count <= 0 || cable == null)
            return drops;

        var merged = new ItemStack[(drops?.Length ?? 0) + 1];
        if (drops != null)
            Array.Copy(drops, merged, drops.Length);
        merged[^1] = new ItemStack(cable, count);
        return merged;
    }

    private static void DropCable(IWorldAccessor world, BlockPos pos, BlockEntityEStorageInterface panel, Facing bits)
    {
        var removed = panel.Cable & bits;
        if (removed == Facing.None)
            return;

        var code = panel.CableCode;
        var count = BlockEStorageCable.HostStrands(removed, panel.CableLines);
        panel.Cable &= ~bits;
        BlockEStorageCable.ClearHostFaces(panel.Cable, panel.CableLines, removed);
        if (panel.Cable == Facing.None)
            panel.CableCode = "";
        panel.MarkDirty(true);
        StorageAccess.DirtyTopology();
        var cable = BlockEStorageCable.HostBlock(world, code);
        if (count > 0 && cable != null)
            world.SpawnItemEntity(new ItemStack(cable, count), pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }

    private static BlockFacing? SupportFace(Facing facing)
    {
        foreach (var face in FacingHelper.Faces(facing))
            return face;
        return null;
    }
}

public enum InterfaceRow
{
    Export,
    Pattern,
    Config
}

public class ItemSlotInterface : ItemSlot
{
    public const int Columns = 9;
    public const int ExportAt = 0;
    public const int PatternAt = 9;
    public const int ConfigAt = 18;
    public const int Total = 27;

    public readonly InterfaceRow Row;

    public ItemSlotInterface(InventoryBase inventory, InterfaceRow row) : base(inventory)
    {
        Row = row;
    }

    public static InterfaceRow RowOf(int index)
    {
        if (index < PatternAt)
            return InterfaceRow.Export;
        if (index < ConfigAt)
            return InterfaceRow.Pattern;
        return InterfaceRow.Config;
    }

    public override int MaxSlotStackSize => Row == InterfaceRow.Pattern ? 1 : base.MaxSlotStackSize;

    public override bool CanTake() => Row != InterfaceRow.Config && base.CanTake();

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
        => Row != InterfaceRow.Config && base.CanTakeFrom(sourceSlot, priority);

    public override bool CanHold(ItemSlot sourceSlot)
    {
        if (Row == InterfaceRow.Config)
            return true;

        if (sourceSlot.Empty)
            return false;

        if (Row == InterfaceRow.Pattern)
        {
            if (!ItemEStoragePattern.IsEncoded(sourceSlot.Itemstack))
                return false;
            if (Empty || Itemstack == null || Inventory.Api?.World == null)
                return true;
            return StorageAccess.Same(Inventory.Api.World, Itemstack, sourceSlot.Itemstack);
        }

        if (Empty || Itemstack == null || Inventory.Api?.World == null)
            return true;

        return StorageAccess.Same(Inventory.Api.World, Itemstack, sourceSlot.Itemstack);
    }

    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        if (Row == InterfaceRow.Config)
        {
            SetConfig(sourceSlot, op.MouseButton != EnumMouseButton.Right);
            return;
        }

        base.ActivateSlot(sourceSlot, ref op);
    }

    protected override void ActivateSlotRightClick(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
        if (Row == InterfaceRow.Config)
        {
            SetConfig(sourceSlot, false);
            return;
        }

        base.ActivateSlotRightClick(sourceSlot, ref op);
    }

    public override bool TryFlipWith(ItemSlot itemSlot)
    {
        if (Row != InterfaceRow.Config)
            return base.TryFlipWith(itemSlot);

        SetConfig(itemSlot, true);
        return true;
    }

    /// <summary>Образец остаётся в курсоре. Пустая рука снимает его.</summary>
    private void SetConfig(ItemSlot? sourceSlot, bool whole)
    {
        if (sourceSlot == null || sourceSlot.Empty || sourceSlot.Itemstack == null)
        {
            Itemstack = null;
            MarkDirty();
            return;
        }

        var ghost = sourceSlot.Itemstack.Clone();
        var cap = Math.Max(1, ghost.Collectible?.MaxStackSize ?? 1);
        ghost.StackSize = whole ? Math.Min(Math.Max(1, sourceSlot.StackSize), cap) : 1;
        Itemstack = ghost;
        MarkDirty();
    }
}

public class InventoryEStorageInterface : InventoryGeneric
{
    public InventoryEStorageInterface()
        : base(ItemSlotInterface.Total, "estorageinterface", "0", null, NewSlot)
    {
    }

    private static ItemSlot NewSlot(int id, InventoryGeneric inventory)
        => new ItemSlotInterface(inventory, ItemSlotInterface.RowOf(id));

    public override WeightedSlot GetBestSuitedSlot(ItemSlot sourceSlot, ItemStackMoveOperation op, List<ItemSlot> skipSlots)
    {
        var result = new WeightedSlot();
        if (sourceSlot?.Itemstack == null)
            return result;

        var world = Api?.World;
        var priority = op?.CurrentPriority ?? EnumMergePriority.AutoMerge;
        var player = op?.ActingPlayer != null;
        ItemSlot? chosen = null;
        var merging = false;

        if (ItemEStoragePattern.IsEncoded(sourceSlot.Itemstack))
        {
            for (var i = ItemSlotInterface.PatternAt; i < ItemSlotInterface.ConfigAt; i++)
            {
                if (this[i] is not ItemSlotInterface slot || !slot.Empty)
                    continue;
                if (skipSlots != null && skipSlots.Contains(slot))
                    continue;
                if (!slot.CanTakeFrom(sourceSlot, priority))
                    continue;

                chosen = slot;
                break;
            }
        }
        else if (world != null)
        {
            for (var i = 0; i < ItemSlotInterface.Columns; i++)
            {
                if (this[i] is not ItemSlotInterface slot)
                    continue;
                if (skipSlots != null && skipSlots.Contains(slot))
                    continue;

                var goal = this[ItemSlotInterface.ConfigAt + i].Itemstack;
                if (goal?.Collectible == null || !StorageAccess.Same(world, goal, sourceSlot.Itemstack))
                    continue;

                var have = slot.Empty || slot.Itemstack == null ? 0 : slot.Itemstack.StackSize;
                if (!player && have >= goal.StackSize)
                    continue;
                if (!slot.CanTakeFrom(sourceSlot, priority))
                    continue;

                if (slot.Empty)
                {
                    chosen ??= slot;
                    continue;
                }

                chosen = slot;
                merging = true;
                break;
            }

            if (chosen == null && player)
            {
                for (var i = 0; i < ItemSlotInterface.Columns; i++)
                {
                    if (this[i] is not ItemSlotInterface slot || !slot.Empty)
                        continue;
                    if (skipSlots != null && skipSlots.Contains(slot))
                        continue;
                    if (!slot.CanTakeFrom(sourceSlot, priority))
                        continue;

                    chosen = slot;
                    break;
                }
            }
        }

        if (chosen == null)
            return result;

        result.slot = chosen;
        result.weight = GetSuitability(sourceSlot, chosen, merging);
        return result;
    }
}

public class BlockEntityEStorageInterface : BlockEntityOpenableContainer
{
    private readonly InventoryEStorageInterface _inventory;
    private readonly List<CraftJob> _jobs = [];
    private readonly List<ItemStack> _inputs = [];
    private bool _ready;
    private bool _busy;
    private bool _ejectBuffer;
    public Facing Facing = Facing.UpNorth;
    public Facing Cable = Facing.None;
    public readonly int[] CableLines = new int[6];
    public string CableCode = "";

    public BlockEntityEStorageInterface()
    {
        _inventory = new InventoryEStorageInterface();
        _inventory.SlotModified += OnSlot;
    }

    public override InventoryBase Inventory => _inventory;

    public override string InventoryClassName => "estorageinterface";

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        _inventory.LateInitialize("estorageinterface-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);
        _ready = true;
        if (api.Side == EnumAppSide.Server)
        {
            EjectLegacyBuffer();
            StorageAccess.DirtyTopology();
            if (HasPattern())
                StorageAccess.TouchContents();
            RegisterGameTickListener(_ => Maintain(), 500);
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("facing", (int)Facing);
        tree.SetInt("cable", (int)Cable);
        tree.SetInt("cableLines", BlockEStorageCable.Pack(CableLines));
        tree.SetString("cableCode", CableCode ?? "");
        for (var i = 0; i < ItemSlotInterface.Columns; i++)
        {
            tree.RemoveAttribute("keep" + i);
            tree.RemoveAttribute("filter" + i);
        }

        tree.SetInt("jobs", _jobs.Count);
        for (var i = 0; i < _jobs.Count; i++)
        {
            var job = _jobs[i];
            tree.SetInt("jobSlot" + i, job.Slot);
            tree.SetInt("jobWant" + i, job.Wanted);
            tree.SetInt("jobGot" + i, job.Got);
            tree.SetInt("jobFed" + i, job.Fed);
            tree.SetInt("jobAt" + i, job.Index);
            tree.SetInt("jobPut" + i, job.Pushed);
            if (job.Result == null)
                tree.RemoveAttribute("jobOut" + i);
            else
                tree["jobOut" + i] = new ItemstackAttribute(job.Result.Clone());
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        var raw = tree.GetInt("facing", (int)Facing.UpNorth);
        Facing = raw == 0 ? Facing.UpNorth : (Facing)raw;
        Cable = (Facing)tree.GetInt("cable");
        var packedLines = tree.GetInt("cableLines");
        for (var n = 0; n < CableLines.Length; n++)
            CableLines[n] = (packedLines >> (n * 3)) & 7;
        CableCode = tree.GetString("cableCode", "");
        if (Cable != Facing.None && string.IsNullOrEmpty(CableCode))
            CableCode = "estoragecable";
        _ejectBuffer = false;
        for (var i = 0; i < ItemSlotInterface.Columns; i++)
        {
            var keep = tree.GetInt("keep" + i);
            ItemStack? filter = null;
            if (tree["filter" + i] is ItemstackAttribute { value: { } saved })
            {
                filter = saved.Clone();
                filter.ResolveBlockOrItem(worldAccessForResolve);
                if (filter.Collectible == null)
                    filter = null;
            }

            var held = _inventory[i].Itemstack;
            if (filter == null && keep > 0 && held != null)
            {
                filter = held.Clone();
                filter.StackSize = 1;
            }

            if (keep <= 0 && filter == null)
                continue;

            _ejectBuffer = true;
            var config = _inventory[ItemSlotInterface.ConfigAt + i];
            if (!config.Empty || filter?.Collectible == null)
                continue;

            var cap = Math.Max(1, filter.Collectible.MaxStackSize);
            filter.StackSize = Math.Clamp(Math.Max(keep, 1), 1, cap);
            config.Itemstack = filter;
        }

        _jobs.Clear();
        var count = tree.GetInt("jobs");
        for (var i = 0; i < count && i < ItemSlotInterface.Columns; i++)
        {
            if (tree["jobOut" + i] is not ItemstackAttribute { value: { } saved })
                continue;

            var result = saved.Clone();
            result.ResolveBlockOrItem(worldAccessForResolve);
            if (result.Collectible == null)
                continue;

            result.StackSize = 1;
            var slot = tree.GetInt("jobSlot" + i);
            if (slot < ItemSlotInterface.PatternAt || slot >= ItemSlotInterface.ConfigAt)
                continue;

            _jobs.Add(new CraftJob
            {
                Slot = slot,
                Wanted = Math.Clamp(tree.GetInt("jobWant" + i), 1, 100000),
                Got = Math.Max(0, tree.GetInt("jobGot" + i)),
                Fed = Math.Max(0, tree.GetInt("jobFed" + i)),
                Index = Math.Max(0, tree.GetInt("jobAt" + i)),
                Pushed = Math.Max(0, tree.GetInt("jobPut" + i)),
                Result = result
            });
        }
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            toggleInventoryDialogClient(byPlayer, () =>
                new GuiDialogEStorageInterface(
                    Lang.Get("electricalprogressivestorage:estorage-interface-title"),
                    Inventory,
                    Pos,
                    (ICoreClientAPI)Api));
        }

        return true;
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        if (Api?.World == null)
            return;

        dsc.AppendLine(Lang.Get(BlockEStorageTerminal.LinkKey(StorageAccess.Link(Api.World, Pos))));
    }

    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        for (var i = ItemSlotInterface.ConfigAt; i < _inventory.Count; i++)
            _inventory[i].Itemstack = null;

        base.OnBlockBroken(byPlayer);
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        StorageAccess.DirtyTopology();
    }

    public void CollectOutputs(List<ItemStack> into, int limit)
    {
        if (Api?.World == null)
            return;

        for (var i = ItemSlotInterface.PatternAt; i < ItemSlotInterface.ConfigAt; i++)
        {
            if (into.Count >= limit)
                return;

            var stack = _inventory[i].Itemstack;
            if (!ItemEStoragePattern.IsEncoded(stack))
                continue;

            var output = ItemEStoragePattern.Primary(Api.World, stack!);
            if (output?.Collectible == null)
                continue;

            var ghost = output.Clone();
            ghost.StackSize = 1;
            ghost.Attributes.SetBool(StorageAccess.CraftGhostKey, true);
            var seen = false;
            foreach (var have in into)
            {
                if (have.Attributes.GetBool(StorageAccess.CraftGhostKey) && StorageAccess.Same(Api.World, have, ghost))
                {
                    seen = true;
                    break;
                }
            }

            if (!seen)
                into.Add(ghost);
        }
    }

    public bool TryOrder(ItemStack proto, int count)
    {
        if (Api?.World == null || proto.Collectible == null || count <= 0)
            return false;
        if (StorageAccess.Link(Api.World, Pos) != StorageLink.Online)
            return false;

        count = Math.Min(count, 100000);
        for (var i = ItemSlotInterface.PatternAt; i < ItemSlotInterface.ConfigAt; i++)
        {
            var pattern = _inventory[i].Itemstack;
            if (!ItemEStoragePattern.IsEncoded(pattern))
                continue;

            var output = ItemEStoragePattern.Primary(Api.World, pattern!);
            if (output == null || !StorageAccess.Same(Api.World, output, proto))
                continue;

            var job = _jobs.Find(entry => entry.Slot == i);
            if (job == null)
            {
                var result = output.Clone();
                result.StackSize = 1;
                job = new CraftJob { Slot = i, Result = result };
                _jobs.Add(job);
            }

            job.Wanted = Math.Min(100000, job.Wanted + count);
            MarkDirty();
            return true;
        }

        return false;
    }

    private void OnSlot(int index)
    {
        if (_busy || !_ready || Api?.Side != EnumAppSide.Server)
            return;
        if (_inventory[index] is not ItemSlotInterface slot || slot.Row != InterfaceRow.Pattern)
            return;

        var stack = slot.Itemstack;
        if (_jobs.RemoveAll(job => job.Slot == index && !PatternMatches(job, stack)) > 0)
            MarkDirty();
        StorageAccess.TouchContents();
    }

    private bool PatternMatches(CraftJob job, ItemStack? stack)
    {
        if (Api?.World == null || job.Result == null || !ItemEStoragePattern.IsEncoded(stack))
            return false;

        var output = ItemEStoragePattern.Primary(Api.World, stack!);
        return output != null && StorageAccess.Same(Api.World, output, job.Result);
    }

    private bool HasPattern()
    {
        for (var i = ItemSlotInterface.PatternAt; i < ItemSlotInterface.ConfigAt; i++)
        {
            if (ItemEStoragePattern.IsEncoded(_inventory[i].Itemstack))
                return true;
        }

        return false;
    }

    private void EjectLegacyBuffer()
    {
        if (!_ejectBuffer || Api?.World == null)
            return;

        var slot = _inventory[ItemSlotInterface.PatternAt];
        if (slot.Empty || slot.Itemstack == null || ItemEStoragePattern.IsEncoded(slot.Itemstack))
            return;

        Api.World.SpawnItemEntity(slot.Itemstack.Clone(), Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        slot.Itemstack = null;
        slot.MarkDirty();
    }

    private void Maintain()
    {
        if (!_ready || Api?.Side != EnumAppSide.Server || Api.World == null)
            return;
        if (StorageAccess.Link(Api.World, Pos) != StorageLink.Online)
            return;

        if (_jobs.Count > 0)
        {
            ReturnExports();
            RunJobs();
            return;
        }

        ReconcileExports();
        FeedExports();
    }

    private void ReturnExports()
    {
        for (var i = 0; i < ItemSlotInterface.Columns; i++)
        {
            if (_inventory[i] is not ItemSlotInterface slot || slot.Empty || slot.Itemstack == null)
                continue;

            Push(slot, slot.Itemstack.StackSize);
        }
    }

    private void ReconcileExports()
    {
        for (var i = 0; i < ItemSlotInterface.Columns; i++)
        {
            if (_inventory[i] is not ItemSlotInterface slot)
                continue;

            ReconcileExport(slot, _inventory[ItemSlotInterface.ConfigAt + i].Itemstack);
        }
    }

    private void ReconcileExport(ItemSlotInterface slot, ItemStack? goal)
    {
        if (_busy)
            return;

        _busy = true;
        try
        {
            var world = Api?.World;
            if (world == null)
                return;

            if (goal?.Collectible == null || goal.Collectible.IsLiquid())
            {
                Push(slot, slot.Empty || slot.Itemstack == null ? 0 : slot.Itemstack.StackSize);
                return;
            }

            if (!slot.Empty && slot.Itemstack != null && !StorageAccess.Same(world, goal, slot.Itemstack))
            {
                Push(slot, slot.Itemstack.StackSize);
                return;
            }

            var have = slot.Empty || slot.Itemstack == null ? 0 : slot.Itemstack.StackSize;
            var target = Math.Min(Math.Max(goal.StackSize, 0), Math.Max(1, goal.Collectible.MaxStackSize));
            if (have > target)
                Push(slot, have - target);
            else if (have < target)
                Fill(slot, goal, target - have);
        }
        finally
        {
            _busy = false;
        }
    }

    private void FeedExports()
    {
        if (!TryMount(out var target, out var face))
            return;

        var world = Api?.World;
        if (world == null)
            return;

        for (var i = 0; i < ItemSlotInterface.Columns; i++)
        {
            if (_inventory[i] is not ItemSlotInterface slot || slot.Empty || slot.Itemstack == null)
                continue;
            if (slot.Itemstack.Collectible?.IsLiquid() == true)
                continue;

            var goal = _inventory[ItemSlotInterface.ConfigAt + i].Itemstack;
            if (goal == null || !StorageAccess.Same(world, goal, slot.Itemstack))
                continue;

            PushInto(target, face, slot);
        }
    }

    private void RunJobs()
    {
        if (!TryMount(out var target, out var face))
            return;

        var changed = PullOutputs(target, face);
        for (var i = _jobs.Count - 1; i >= 0; i--)
        {
            var job = _jobs[i];
            var pattern = _inventory[job.Slot].Itemstack;
            if (!PatternMatches(job, pattern))
            {
                _jobs.RemoveAt(i);
                changed = true;
                continue;
            }

            if (job.Got >= job.Wanted)
            {
                _jobs.RemoveAt(i);
                changed = true;
                continue;
            }

            if (PushIngredient(job, pattern!, target, face))
                changed = true;
        }

        if (changed)
            MarkDirty();
    }

    /// <summary>
    /// У жерновов автозабор не объявлен: вход — слот, куда кладётся предмет, выход — второй.
    /// Пока заказ идёт, забирается всё из выхода, чтобы слот не встал.
    /// </summary>
    private bool PullOutputs(InventoryBase target, BlockFacing face)
    {
        var probe = NextIngredient();
        ItemSlot? input = probe == null ? null : target.GetAutoPushIntoSlot(face, new DummySlot(probe));
        if (input == null && target.Count == 2)
            input = target[0];

        var pulled = target.GetAutoPullFromSlot(face);
        var changed = false;
        if (pulled != null && pulled != input)
            changed |= TakeOutput(pulled);
        else if (target.Count == 2 && input != null)
        {
            for (var i = 0; i < target.Count; i++)
            {
                if (target[i] != input)
                    changed |= TakeOutput(target[i]);
            }
        }
        else
        {
            for (var i = 0; i < target.Count; i++)
            {
                var slot = target[i];
                if (slot == null || slot == input || slot.Empty || slot.Itemstack == null)
                    continue;
                if (MatchesOutput(slot.Itemstack))
                    changed |= TakeOutput(slot);
            }
        }

        return changed;
    }

    private bool TakeOutput(ItemSlot slot)
    {
        var stack = slot.Itemstack;
        if (stack == null || stack.StackSize <= 0 || Api?.World == null)
            return false;

        var moved = StorageAccess.Insert(Api.World, Pos, stack, stack.StackSize);
        if (moved <= 0)
            return false;

        Credit(stack, moved);
        stack.StackSize -= moved;
        if (stack.StackSize <= 0)
            slot.Itemstack = null;
        slot.MarkDirty();
        return true;
    }

    private void Credit(ItemStack stack, int moved)
    {
        var left = moved;
        foreach (var job in _jobs)
        {
            if (left <= 0)
                break;
            if (job.Fed <= 0 || job.Result == null || Api?.World == null)
                continue;
            if (!StorageAccess.Same(Api.World, job.Result, stack))
                continue;

            var need = job.Wanted - job.Got;
            if (need <= 0)
                continue;

            var give = Math.Min(need, left);
            job.Got += give;
            left -= give;
        }
    }

    private bool MatchesOutput(ItemStack stack)
    {
        if (Api?.World == null)
            return false;

        foreach (var job in _jobs)
        {
            if (job.Result != null && StorageAccess.Same(Api.World, job.Result, stack))
                return true;
        }

        return false;
    }

    private ItemStack? NextIngredient()
    {
        if (Api?.World == null)
            return null;

        foreach (var job in _jobs)
        {
            var pattern = _inventory[job.Slot].Itemstack;
            if (!ItemEStoragePattern.IsEncoded(pattern))
                continue;

            ItemEStoragePattern.Inputs(Api.World, pattern!, _inputs);
            if (_inputs.Count == 0)
                continue;
            if (job.Index >= 0 && job.Index < _inputs.Count)
                return _inputs[job.Index];
            return _inputs[0];
        }

        return null;
    }

    private bool PushIngredient(CraftJob job, ItemStack pattern, InventoryBase target, BlockFacing face)
    {
        var world = Api?.World;
        if (world == null)
            return false;

        ItemEStoragePattern.Inputs(world, pattern, _inputs);
        if (_inputs.Count == 0)
            return false;

        var per = ItemEStoragePattern.PerCraft(world, pattern);
        var crafts = (job.Wanted + per - 1) / per;
        if (job.Fed >= crafts)
            return false;

        if (job.Index == 0 && job.Pushed == 0 && job.Fed > job.Got / per)
            return false;

        while (job.Index < _inputs.Count && job.Pushed >= _inputs[job.Index].StackSize)
        {
            job.Index++;
            job.Pushed = 0;
        }

        if (job.Index >= _inputs.Count)
        {
            job.Fed++;
            job.Index = 0;
            job.Pushed = 0;
            return true;
        }

        var need = _inputs[job.Index];
        var holder = new DummySlot(need.Clone());
        var dest = target.GetAutoPushIntoSlot(face, holder);
        if (dest == null)
            return false;
        if (!dest.Empty && dest.Itemstack != null && !StorageAccess.Same(world, dest.Itemstack, need))
            return false;

        var room = dest.GetRemainingSlotSpace(need);
        if (room <= 0 && dest.Empty)
            room = Math.Max(1, need.Collectible?.MaxStackSize ?? 1);
        if (room <= 0)
            return false;

        var take = Math.Min(need.StackSize - job.Pushed, room);
        if (take <= 0)
            return false;

        var got = StorageAccess.Extract(world, Pos, need, take);
        if (got == null || got.StackSize <= 0)
            return false;

        holder = new DummySlot(got);
        var op = new ItemStackMoveOperation(world, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, got.StackSize);
        var moved = holder.TryPutInto(dest, ref op);
        var remain = holder.Itemstack?.StackSize ?? 0;
        if (remain > 0 && holder.Itemstack != null)
            GiveBack(holder.Itemstack, remain);
        if (moved <= 0)
            return false;

        job.Pushed += moved;
        if (job.Pushed >= need.StackSize)
        {
            job.Index++;
            job.Pushed = 0;
            if (job.Index >= _inputs.Count)
            {
                job.Fed++;
                job.Index = 0;
            }
        }

        return true;
    }

    private void GiveBack(ItemStack stack, int count)
    {
        if (count <= 0 || Api?.World == null)
            return;

        var back = StorageAccess.Insert(Api.World, Pos, stack, count);
        var left = count - back;
        if (left <= 0)
            return;

        var drop = stack.Clone();
        drop.StackSize = left;
        Api.World.SpawnItemEntity(drop, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }

    private bool TryMount(out InventoryBase inventory, out BlockFacing face)
    {
        inventory = null!;
        face = BlockFacing.NORTH;
        if (Api?.World == null || Facing == Facing.None)
            return false;
        if (StorageAccess.Link(Api.World, Pos) != StorageLink.Online)
            return false;

        BlockFacing? toward = null;
        foreach (var side in FacingHelper.Faces(Facing))
        {
            toward = side;
            break;
        }

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

    private void PushInto(InventoryBase target, BlockFacing face, ItemSlot slot)
    {
        if (slot.Empty || slot.Itemstack == null || Api?.World == null)
            return;

        var dest = target.GetAutoPushIntoSlot(face, slot);
        if (dest == null || !dest.CanHold(slot))
            return;

        var op = new ItemStackMoveOperation(Api.World, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, slot.Itemstack.StackSize);
        _busy = true;
        try
        {
            var moved = slot.TryPutInto(dest, ref op);
            if (moved <= 0)
                return;

            if (slot.Itemstack != null && slot.Itemstack.StackSize <= 0)
                slot.Itemstack = null;
            slot.MarkDirty();
            dest.MarkDirty();
        }
        finally
        {
            _busy = false;
        }
    }

    private void Push(ItemSlot slot, int count)
    {
        var stack = slot.Itemstack;
        if (stack == null || count <= 0 || Api?.World == null)
            return;
        if (StorageAccess.Link(Api.World, Pos) != StorageLink.Online)
            return;

        var moved = StorageAccess.Insert(Api.World, Pos, stack, Math.Min(count, stack.StackSize));
        if (moved <= 0)
            return;

        stack.StackSize -= moved;
        if (stack.StackSize <= 0)
            slot.Itemstack = null;
        slot.MarkDirty();
    }

    private void Fill(ItemSlot slot, ItemStack proto, int count)
    {
        if (count <= 0 || Api?.World == null)
            return;
        if (StorageAccess.Link(Api.World, Pos) != StorageLink.Online)
            return;

        var got = StorageAccess.Extract(Api.World, Pos, proto, count);
        if (got == null || got.StackSize <= 0)
            return;

        if (slot.Empty || slot.Itemstack == null)
            slot.Itemstack = got;
        else
            slot.Itemstack.StackSize += got.StackSize;
        slot.MarkDirty();
    }

    private sealed class CraftJob
    {
        public int Slot;
        public int Wanted;
        public int Got;
        public int Fed;
        public int Index;
        public int Pushed;
        public ItemStack? Result;
    }
}
