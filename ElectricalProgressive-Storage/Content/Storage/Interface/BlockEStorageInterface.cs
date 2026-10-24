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

namespace ElectricalProgressive.Content.Storage.Interface;

/// <summary>
/// Плоский интерфейс на уже проложенном кабеле. Под жилой — сундук, бочка или машина.
/// </summary>
public class BlockEStorageInterface : BlockEStorageTerminal
{
    protected override bool MountsOn(IWorldAccessor world, BlockPos pos, Facing facing)
        => ContainerMount(world, pos, facing);

    protected override bool CanStay(IWorldAccessor world, BlockPos pos, Facing facing)
        => ContainerMount(world, pos, facing) && base.CanStay(world, pos, facing);
}

/// <summary>
/// Ряд слота интерфейса: что забирать, шаблон или что класть.
/// </summary>
public enum InterfaceRow
{
    Export,
    Pattern,
    Config
}

/// <summary>
/// Слот интерфейса: что забирать, шаблон или образец того, что класть.
/// </summary>
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

/// <summary>
/// Слоты одной грани интерфейса.
/// </summary>
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

/// <summary>
/// Автокрафт этой грани: шаблон, заказ и передача в станок под жилой.
/// </summary>
public class BlockEntityEStorageInterface : BlockEntityOpenableContainer, IPanelHost, IPanelStack
{
    private readonly InventoryEStorageInterface _inventory;
    private readonly List<CraftJob> _jobs = [];
    private readonly List<ItemStack> _inputs = [];
    private static long _jobSerial;
    private bool _ready;
    private bool _busy;
    private bool _ejectBuffer;
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

    public BlockEntityEStorageInterface()
    {
        _inventory = new InventoryEStorageInterface();
        _inventory.SlotModified += OnSlot;
    }

    public override InventoryBase Inventory => _inventory;

    public override string InventoryClassName => "estorageinterface";

    public bool Holds(string inventoryId) => inventoryId == _inventory.InventoryID;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        PanelStack.ClaimPromote(this);
        BlockEStorageTerminal.ClaimStick(this);
        _inventory.LateInitialize("estorageinterface-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z + Seat, api);
        Stack.SpawnPending(api.World, Pos);
        _ready = true;
        if (api.Side == EnumAppSide.Server)
        {
            BindLoaded();
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
        Stack.Write(tree);
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
            tree.SetInt("jobPaid" + i, job.Prepaid);
            tree.SetInt("jobAt" + i, job.Index);
            tree.SetInt("jobPut" + i, job.Pushed);
            tree.SetDouble("jobSince" + i, job.Since);
            tree.SetLong("jobId" + i, job.Id);
            tree.SetLong("jobBatch" + i, job.Batch);
            if (job.Cpu == null)
                tree.SetInt("jobCpu" + i, 0);
            else
            {
                tree.SetInt("jobCpu" + i, 1);
                tree.SetInt("jobCpuX" + i, job.Cpu.X);
                tree.SetInt("jobCpuY" + i, job.Cpu.Y);
                tree.SetInt("jobCpuZ" + i, job.Cpu.Z);
                tree.SetInt("jobCpuD" + i, job.Cpu.dimension);
            }

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
        Stack.Read(tree, worldAccessForResolve, this);
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
                Prepaid = Math.Max(0, tree.GetInt("jobPaid" + i)),
                Index = Math.Max(0, tree.GetInt("jobAt" + i)),
                Pushed = Math.Max(0, tree.GetInt("jobPut" + i)),
                Since = tree.HasAttribute("jobSince" + i)
                    ? tree.GetDouble("jobSince" + i)
                    : worldAccessForResolve.Calendar.ElapsedSeconds,
                Result = result,
                Cpu = ReadCpu(tree, i),
                Id = tree.GetLong("jobId" + i),
                Batch = tree.GetLong("jobBatch" + i)
            });
            if (_jobs[^1].Id == 0 && worldAccessForResolve.Side == EnumAppSide.Server)
                _jobs[^1].Id = NewJobId();
        }
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

        var dialog = new GuiDialogEStorageInterface(
            Lang.Get("electricalprogressivestorage:estorage-interface-title"),
            Inventory,
            Pos,
            capi,
            Facing);
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
        if (Api?.Side == EnumAppSide.Server)
        {
            for (var i = _jobs.Count - 1; i >= 0; i--)
                ReleaseJob(_jobs[i]);
            _jobs.Clear();
        }

        for (var i = ItemSlotInterface.ConfigAt; i < _inventory.Count; i++)
            _inventory[i].Itemstack = null;

        if (Api?.Side == EnumAppSide.Server && Pos != null)
            Inventory.DropAll(Pos.ToVec3d().Add(0.5, 0.5, 0.5));

        base.OnBlockBroken(byPlayer);
    }

    public override void OnBlockRemoved()
    {
        if (!Stack.Keep)
            Stack.Discard();
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

            var outputs = new List<ItemStack>(3);
            ItemEStoragePattern.CopyOutputs(Api.World, stack!, outputs);
            foreach (var output in outputs)
            {
                if (output.Collectible == null)
                    continue;
                if (into.Count >= limit)
                    return;

                var ghost = output.Clone();
                ghost.StackSize = 1;
                ghost.Attributes.RemoveAttribute(StorageAccess.OrderKey);
                ghost.Attributes.RemoveAttribute(StorageAccess.CraftGhostKey);
                var seen = false;
                foreach (var have in into)
                {
                    if (!SameOutput(have, ghost))
                        continue;
                    if (have.Attributes.GetBool(StorageAccess.CraftGhostKey))
                    {
                        seen = true;
                        continue;
                    }

                    have.Attributes.SetBool(StorageAccess.OrderKey, true);
                }

                if (!seen)
                {
                    ghost.Attributes.SetBool(StorageAccess.CraftGhostKey, true);
                    into.Add(ghost);
                }
            }
        }
    }

    /// <summary>
    /// Сравнение без флагов списка: иначе лежащий предмет не совпадёт с призраком.
    /// </summary>
    private bool SameOutput(ItemStack have, ItemStack proto)
    {
        var craft = have.Attributes.GetBool(StorageAccess.CraftGhostKey);
        var order = have.Attributes.GetBool(StorageAccess.OrderKey);
        if (craft)
            have.Attributes.RemoveAttribute(StorageAccess.CraftGhostKey);
        if (order)
            have.Attributes.RemoveAttribute(StorageAccess.OrderKey);
        var match = StorageAccess.Same(Api!.World, have, proto);
        if (craft)
            have.Attributes.SetBool(StorageAccess.CraftGhostKey, true);
        if (order)
            have.Attributes.SetBool(StorageAccess.OrderKey, true);
        return match;
    }

    public bool TryOrder(ItemStack proto, int count)
    {
        var world = Api?.World;
        if (world == null || proto.Collectible == null || count <= 0)
            return false;
        if (StorageAccess.Link(world, Pos) != StorageLink.Online)
            return false;

        count = Math.Min(count, 100000);
        for (var i = ItemSlotInterface.PatternAt; i < ItemSlotInterface.ConfigAt; i++)
        {
            if (TryOrderSlot(i, proto, count) >= 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Сколько результатов реально добавлено в заказ этого слота. −1, если слот не подошёл или нет свободного процессора.
    /// </summary>
    public int TryOrderSlot(int slot, ItemStack proto, int count, BlockPos? share = null, long batch = 0)
    {
        var world = Api?.World;
        if (world == null || proto.Collectible == null || count <= 0)
            return -1;
        if (StorageAccess.Link(world, Pos) != StorageLink.Online)
            return -1;
        if (slot < ItemSlotInterface.PatternAt || slot >= ItemSlotInterface.ConfigAt)
            return -1;

        count = Math.Min(count, 100000);
        var pattern = _inventory[slot].Itemstack;
        if (!ItemEStoragePattern.IsEncoded(pattern))
            return -1;

        var output = ItemEStoragePattern.Matching(world, pattern!, proto);
        if (output == null)
            return -1;

        var job = _jobs.Find(entry => entry.Slot == slot);
        if (job != null && ItemEStoragePattern.JobId(pattern) == 0 && OutputMatches(job, pattern))
            Bind(job, pattern!, true);
        // Чужой шаблон не продолжает и не увеличивает уже стоящий заказ.
        if (job != null && !PatternMatches(job, pattern))
            return -1;

        var created = false;
        if (job == null)
        {
            var stamped = ItemEStoragePattern.JobId(pattern);
            if (stamped != 0 && NetworkHasJob(stamped))
                return -1;
            if (stamped != 0)
                ItemEStoragePattern.SetJobId(pattern!, 0);

            var result = output.Clone();
            result.StackSize = 1;
            job = new CraftJob
            {
                Slot = slot,
                Result = result,
                Since = world.Calendar.ElapsedSeconds,
                Id = NewJobId()
            };
            _jobs.Add(job);
            created = true;
        }

        if (batch != 0)
            job.Batch = batch;

        if (!AttachCpu(job, pattern!, count, share))
        {
            if (created)
                _jobs.Remove(job);
            return -1;
        }

        Bind(job, pattern!, true);

        var before = job.Wanted;
        job.Wanted = Math.Min(100000, job.Wanted + count);
        if (job.Cpu != null)
            Stock(job, pattern!);
        MarkDirty();
        return job.Wanted - before;
    }

    public void UndoOrder(int slot, int count)
    {
        if (count <= 0)
            return;

        var job = _jobs.Find(entry => entry.Slot == slot);
        if (job == null)
            return;

        job.Wanted -= count;
        if (job.Wanted < job.Got)
            job.Wanted = job.Got;
        if (job.Wanted <= job.Got)
        {
            ReleaseJob(job);
            _jobs.Remove(job);
        }

        MarkDirty();
    }

    public bool OwnsJob(int slot)
    {
        foreach (var job in _jobs)
        {
            if (job.Slot == slot && job.Got < job.Wanted)
                return true;
        }

        return false;
    }

    public bool OwnsCraft(long id)
    {
        if (id == 0)
            return false;

        foreach (var job in _jobs)
        {
            if (job.Id == id && job.Got < job.Wanted)
                return true;
        }

        return false;
    }

    public long IngredientOwed(int slot, ItemStack proto)
    {
        var world = Api?.World;
        if (world == null || proto.Collectible == null)
            return 0;

        CraftJob? job = null;
        foreach (var entry in _jobs)
        {
            if (entry.Slot != slot || entry.Got >= entry.Wanted)
                continue;
            job = entry;
            break;
        }

        if (job == null)
            return 0;

        var pattern = _inventory[slot].Itemstack;
        if (!ItemEStoragePattern.IsEncoded(pattern))
            return 0;

        long sum = 0;
        foreach (var (item, extra) in IngredientPlan(world, job, pattern!, job.Wanted))
        {
            if (StorageAccess.Same(world, item, proto))
                sum += extra;
        }

        return sum;
    }

    public BlockPos? CpuOf(int slot)
    {
        foreach (var job in _jobs)
        {
            if (job.Slot == slot && job.Got < job.Wanted)
                return job.Cpu?.Copy();
        }

        return null;
    }

    public long BatchOf(int slot)
    {
        foreach (var job in _jobs)
        {
            if (job.Slot == slot && job.Got < job.Wanted)
                return job.Batch;
        }

        return 0;
    }

    public void CollectCpu(long batch, double since, ref BlockPos? best)
    {
        var world = Api?.World;
        if (world == null)
            return;

        foreach (var job in _jobs)
        {
            if (job.Got >= job.Wanted || job.Cpu == null)
                continue;

            var sameBatch = batch != 0 && job.Batch == batch;
            var sameWave = batch == 0 && job.Batch == 0 && since > 0 && job.Since == since;
            if (!sameBatch && !sameWave)
                continue;
            if (world.BlockAccessor.GetBlockEntity(job.Cpu) is not BlockEntityEStorageProcessor)
                continue;
            if (best != null && !StorageScan.Earlier(job.Cpu, best))
                continue;

            best = job.Cpu.Copy();
        }
    }

    public void AttachCpu(BlockPos cpu, int slot)
    {
        var world = Api?.World;
        if (world == null)
            return;

        foreach (var job in _jobs)
        {
            if (job.Slot != slot || job.Got >= job.Wanted)
                continue;
            if (job.Cpu != null && !ProcessorCluster.SamePos(job.Cpu, cpu))
            {
                if (world.BlockAccessor.GetBlockEntity(job.Cpu) is BlockEntityEStorageProcessor other && other.Claims(Pos, slot, Seat))
                    return;
            }

            if (job.Cpu != null && ProcessorCluster.SamePos(job.Cpu, cpu))
                return;

            job.Cpu = cpu.Copy();
            MarkDirty();
        }
    }

    /// <summary>
    /// Сборщик закончил один крафт. Заказ уменьшается сразу, не дожидаясь забора из выходного слота.
    /// </summary>
    public bool NoteAssemblerCraft(ItemStack? product, ItemStack? pattern = null)
    {
        var world = Api?.World;
        if (world == null || product?.Collectible == null)
            return false;

        ItemStack? crafted = null;
        if (ItemEStoragePattern.IsEncoded(pattern))
            crafted = ItemEStoragePattern.Primary(world, pattern!);

        foreach (var job in _jobs)
        {
            if (job.Got >= job.Wanted || job.Result == null)
                continue;

            var byProduct = StorageAccess.Same(world, job.Result, product);
            var byPattern = crafted != null && StorageAccess.Same(world, job.Result, crafted);
            if (!byProduct && !byPattern)
                continue;

            var craftId = ItemEStoragePattern.JobId(pattern);
            if (craftId != 0 && job.Id != 0 && job.Id != craftId)
                continue;

            var live = _inventory[job.Slot].Itemstack;
            var counted = ItemEStoragePattern.IsEncoded(pattern) ? pattern : live;
            if (!ItemEStoragePattern.IsEncoded(counted))
                continue;

            var per = ItemEStoragePattern.PerCraft(world, counted!);
            var crafts = (job.Wanted + per - 1) / per;
            if (job.Fed >= crafts)
                continue;

            job.Fed++;
            job.Index = 0;
            job.Pushed = 0;
            var gain = Math.Min(per, job.Wanted - job.Got);
            if (gain > 0)
            {
                job.Got += gain;
                if (byProduct)
                    job.Prepaid += gain;
            }

            MarkDirty();
            return true;
        }

        return false;
    }

    public bool TryJob(int slot, out ItemStack? result, out int left, out double since)
    {
        result = null;
        left = 0;
        since = 0;
        foreach (var job in _jobs)
        {
            if (job.Slot != slot || job.Got >= job.Wanted || job.Result?.Collectible == null)
                continue;

            var copy = job.Result.Clone();
            copy.StackSize = 1;
            result = copy;
            left = Math.Max(1, job.Wanted - job.Got);
            since = job.Since;
            return true;
        }

        return false;
    }

    public void CancelJob(int slot)
    {
        if (Api?.Side != EnumAppSide.Server)
            return;

        for (var i = _jobs.Count - 1; i >= 0; i--)
        {
            if (_jobs[i].Slot != slot)
                continue;

            ReleaseJob(_jobs[i]);
            _jobs.RemoveAt(i);
            MarkDirty();
            return;
        }
    }

    public void DetachCpu(BlockPos cpu, int slot)
    {
        foreach (var job in _jobs)
        {
            if (job.Slot != slot || job.Cpu == null || !ProcessorCluster.SamePos(job.Cpu, cpu))
                continue;
            job.Cpu = null;
            MarkDirty();
        }
    }

    private void OnSlot(int index)
    {
        if (_busy || !_ready || Api?.Side != EnumAppSide.Server)
            return;
        if (_inventory[index] is not ItemSlotInterface slot || slot.Row != InterfaceRow.Pattern)
            return;

        // Пустой слот заказ не снимает. Шаблон с номером заказа переносит его на эту панель.
        if (ItemEStoragePattern.IsEncoded(slot.Itemstack))
            Follow(index, slot.Itemstack!);

        StorageAccess.TouchContents();
    }

    private void Follow(int index, ItemStack stack)
    {
        var id = ItemEStoragePattern.JobId(stack);
        if (id == 0)
        {
            var local = _jobs.Find(job => job.Slot == index);
            if (local != null && OutputMatches(local, stack))
                Bind(local, stack, true);
            return;
        }

        var mine = _jobs.Find(job => job.Id == id);
        if (mine != null)
        {
            if (mine.Slot != index && !_jobs.Exists(job => job.Slot == index && job.Id != id) && !SlotTaken(index, null))
                RetargetOwnSlot(mine, index);
            return;
        }

        if (_jobs.Exists(job => job.Slot == index) || Api?.World == null)
            return;

        var scan = StorageAccess.GetScan(Api.World, Pos);
        foreach (var cell in scan.Interfaces)
        {
            foreach (var face in PanelStack.Interfaces(Api.World, cell))
            {
                if (ReferenceEquals(face, this))
                    continue;
                if (face.TryGiveJob(id, this, index, stack, out var present))
                    return;
                if (present)
                    return;
            }
        }

        if (scan.Pending.Count == 0)
        {
            ItemEStoragePattern.SetJobId(stack, 0);
            _inventory[index].MarkDirty();
        }
    }

    /// <summary>
    /// Заказ уходит на другую панель той же сети. Буфер процессора остаётся.
    /// </summary>
    public bool TryGiveJob(long id, BlockEntityEStorageInterface dest, int destSlot, ItemStack stack, out bool present)
    {
        present = false;
        var job = _jobs.Find(entry => entry.Id == id);
        if (job == null || dest.Pos == null || Pos == null || Api?.World == null)
            return false;

        present = true;
        var sitting = _inventory[job.Slot].Itemstack;
        if (sitting != null && ItemEStoragePattern.JobId(sitting) == id)
            return false;

        var oldSlot = job.Slot;
        var sameCell = ProcessorCluster.SamePos(Pos, dest.Pos);
        var sameSlot = oldSlot == destSlot;
        if (dest.SlotTaken(destSlot, sameCell && sameSlot ? this : null))
            return false;

        var cpu = FindCpu(job);
        var world = Api.World;
        if (sameCell && sameSlot && Seat == dest.Seat)
        {
            _jobs.Remove(job);
            job.Slot = destSlot;
            dest.ReceiveJob(job);
        }
        else
        {
            var saved = job.Slot;
            job.Slot = destSlot;
            dest.ReceiveJob(job);
            if (cpu != null && !cpu.Retarget(Pos, oldSlot, Seat, dest.Pos, destSlot, dest.Seat))
            {
                dest.ReleaseReceived(job);
                job.Slot = saved;
                return false;
            }

            _jobs.Remove(job);
        }

        var home = AssemblerFor(world, stack);
        if (home != null && !GridClaimedByOther(world, job, home))
            dest.RehomeFrom(world, home, job);
        MarkDirty();
        dest.MarkDirty();
        return true;
    }

    public bool SlotTaken(int slot, BlockEntityEStorageInterface? ignore)
    {
        var world = Api?.World;
        if (world == null || Pos == null)
            return false;

        foreach (var face in PanelStack.Interfaces(world, Pos))
        {
            if (ignore != null && ReferenceEquals(face, ignore))
                continue;
            if (face.OwnsJob(slot))
                return true;
        }

        return false;
    }

    private BlockEntityEStorageAssembler? AssemblerFor(IWorldAccessor world, ItemStack pattern)
    {
        if (Pos == null)
            return null;

        BlockEntityEStorageAssembler? partial = null;
        foreach (var face in BlockFacing.ALLFACES)
        {
            var neighbor = Pos.AddCopy(face);
            if (world.BlockAccessor.GetBlockEntity(neighbor) is not BlockEntityEStorageAssembler assembler)
                continue;
            if (!Feeds(world, assembler.Pos) || !assembler.ForPanel(world, pattern, Pos))
                continue;
            if (assembler.Serves(Pos))
                return assembler;
            partial ??= assembler;
        }

        return partial;
    }

    private void ReceiveJob(CraftJob job) => _jobs.Add(job);

    private void ReleaseReceived(CraftJob job) => _jobs.Remove(job);

    private bool RetargetOwnSlot(CraftJob job, int destSlot)
    {
        var old = job.Slot;
        var cpu = FindCpu(job);
        job.Slot = destSlot;
        if (cpu != null && Pos != null && !cpu.Retarget(Pos, old, Seat, Pos, destSlot, Seat))
        {
            job.Slot = old;
            return false;
        }

        MarkDirty();
        return true;
    }

    private void RehomeFrom(IWorldAccessor world, BlockEntityEStorageAssembler assembler, CraftJob job)
    {
        var output = assembler.Inventory[InventoryEStorageAssembler.Output];
        if (output != null && !output.Empty && output.Itemstack != null && job.Result != null
            && StorageAccess.Same(world, job.Result, output.Itemstack))
            TakeAssemblerOutput(output);

        if (Feeds(world, assembler.Pos))
        {
            assembler.PointOwner(Pos);
            return;
        }

        assembler.RefundGrid(world, FindCpu(job));
        assembler.ReturnCraftSlots(world, Pos);
        job.Index = 0;
        job.Pushed = 0;
    }

    private bool GridClaimedByOther(IWorldAccessor world, CraftJob job, BlockEntityEStorageAssembler assembler)
    {

        foreach (var other in _jobs)
        {
            if (other.Id == job.Id || other.Got >= other.Wanted)
                continue;
            var stack = _inventory[other.Slot].Itemstack;
            if (ItemEStoragePattern.IsEncoded(stack) && assembler.Holding(world, stack!))
                return true;
        }

        return false;
    }

    private bool NetworkHasJob(long id)
    {
        var world = Api?.World;
        if (world == null || Pos == null || id == 0)
            return false;

        foreach (var cell in StorageAccess.GetScan(world, Pos).Interfaces)
        {
            foreach (var face in PanelStack.Interfaces(world, cell))
            {
                if (face.OwnsCraft(id))
                    return true;
            }
        }

        return false;
    }

    private void BindLoaded()
    {
        foreach (var job in _jobs)
        {
            if (job.Id == 0)
                job.Id = NewJobId();
            var stack = job.Slot >= 0 && job.Slot < _inventory.Count ? _inventory[job.Slot].Itemstack : null;
            if (OutputMatches(job, stack))
                Bind(job, stack!, false);
        }
    }

    private void ClaimArrivals()
    {
        if (Api?.World == null)
            return;

        for (var i = ItemSlotInterface.PatternAt; i < ItemSlotInterface.ConfigAt; i++)
        {
            var stack = _inventory[i].Itemstack;
            if (!ItemEStoragePattern.IsEncoded(stack))
                continue;
            var id = ItemEStoragePattern.JobId(stack);
            if (id == 0 || _jobs.Exists(job => job.Id == id))
                continue;
            Follow(i, stack!);
        }
    }

    private void Bind(CraftJob job, ItemStack stack, bool sync)
    {
        if (job.Id == 0)
            job.Id = NewJobId();
        if (ItemEStoragePattern.JobId(stack) != 0)
            return;

        ItemEStoragePattern.SetJobId(stack, job.Id);
        if (sync && job.Slot >= 0 && job.Slot < _inventory.Count)
            _inventory[job.Slot].MarkDirty();
    }

    private long NewJobId()
    {
        _jobSerial++;
        long id = _jobSerial;
        if (Pos != null)
        {
            id ^= ((long)Pos.X & 0xFFFFF) << 20;
            id ^= ((long)Pos.Y & 0xFFF) << 8;
            id ^= Pos.Z & 0xFFFFF;
            id ^= (long)Pos.dimension << 40;
        }

        var elapsed = Api?.World?.ElapsedMilliseconds ?? 0;
        id ^= elapsed << 1;
        if (id == 0)
            id = _jobSerial;
        return id;
    }

    private bool OutputMatches(CraftJob job, ItemStack? stack)
    {
        var world = Api?.World;
        if (world == null || job.Result == null || !ItemEStoragePattern.IsEncoded(stack))
            return false;

        return ItemEStoragePattern.Matching(world, stack!, job.Result) != null;
    }

    private bool PatternMatches(CraftJob job, ItemStack? stack)
    {
        if (!OutputMatches(job, stack) || stack == null)
            return false;

        var id = ItemEStoragePattern.JobId(stack);
        if (job.Id == 0 || id == 0)
            return true;
        return id == job.Id;
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

        ClaimArrivals();
        if (_jobs.Count > 0)
        {
            ReturnExports();
            RunJobs();
            return;
        }

        ReconcileExports();
        FeedExports();
        PullAssemblerOutput();
    }

    /// <summary>
    /// Ручной крафт не создаёт заказ, но готовый предмет всё равно уходит в сеть.
    /// Сетка при этом остаётся в сборщике.
    /// </summary>
    private void PullAssemblerOutput()
    {
        var assembler = FindAssembler();
        if (assembler != null && PullCraftOutput(assembler))
            MarkDirty();
    }

    private bool PullCraftOutput(BlockEntityEStorageAssembler assembler)
    {
        var slot = assembler.Inventory[InventoryEStorageAssembler.Output];
        if (slot == null || slot.Empty)
            return false;
        return TakeAssemblerOutput(slot);
    }

    /// <summary>
    /// Готовый предмет из сборщика уходит в сеть. Ручной крафт заказа не имеет, но выход забирается так же.
    /// </summary>
    public bool TakeAssemblerOutput(ItemSlot slot)
    {
        var world = Api?.World;
        var stack = slot.Itemstack;
        if (world == null || stack == null || stack.StackSize <= 0)
            return false;

        var origin = StorageOrigin(world);
        if (origin == null)
            return false;

        var moved = StorageAccess.Insert(world, origin, stack, stack.StackSize);
        if (moved <= 0)
            return false;

        Credit(stack, moved);
        stack.StackSize -= moved;
        if (stack.StackSize <= 0)
            slot.Itemstack = null;
        slot.MarkDirty();
        return true;
    }

    /// <summary>
    /// Своя клетка, если интерфейс в сети. Иначе контроллер той же сети: сборщик уже питается от неё.
    /// </summary>
    private BlockPos? StorageOrigin(IWorldAccessor world)
    {
        if (Pos == null)
            return null;
        if (StorageAccess.Link(world, Pos) == StorageLink.Online)
            return Pos;

        var scan = StorageAccess.GetScan(world, Pos);
        if (scan.Conflict || !StorageAccess.HasPower(world, scan))
            return null;
        if (scan.Controller != null && scan.Serves(scan.Controller))
            return scan.Controller;

        foreach (var drive in scan.Drives)
        {
            if (scan.Serves(drive))
                return drive;
        }

        return null;
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
            var cap = Math.Max(1, goal.Collectible.MaxStackSize);
            var target = Math.Clamp(WantInBuffer(goal), 0, cap);
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
        var mounted = TryMount(out var target, out var face);
        var changed = mounted && PullOutputs(target, face);
        var assembler = FindAssembler();
        if (assembler != null && PullCraftOutput(assembler))
            changed = true;
        for (var i = _jobs.Count - 1; i >= 0; i--)
        {
            var job = _jobs[i];
            if (job.Got >= job.Wanted)
            {
                if (OutputPending(job))
                    continue;
                ReleaseJob(job);
                _jobs.RemoveAt(i);
                changed = true;
                continue;
            }

            var pattern = _inventory[job.Slot].Itemstack;
            if (OutputMatches(job, pattern) && pattern != null && ItemEStoragePattern.JobId(pattern) == 0)
                Bind(job, pattern, true);
            if (!PatternMatches(job, pattern))
                continue;

            if (!AttachCpu(job, pattern!, 0))
                continue;

            if (Stock(job, pattern!))
                changed = true;
            if (ItemEStoragePattern.IsProcessing(pattern))
            {
                if (mounted && PushIngredient(job, pattern!, target, face))
                    changed = true;
            }
            else if (assembler != null)
            {
                if (PushCraft(job, pattern!, assembler))
                    changed = true;
            }
            else if (mounted && PushIngredient(job, pattern!, target, face))
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

    /// <summary>
    /// Готовый предмет ещё лежит в сборщике. Заказ уже уменьшен, процессор отпускаем после забора в сеть.
    /// </summary>
    private bool OutputPending(CraftJob job)
    {
        var world = Api?.World;
        var assembler = FindAssembler();
        if (world == null || assembler == null || job.Result == null)
            return false;

        var stack = assembler.Inventory[InventoryEStorageAssembler.Output].Itemstack;
        return stack != null && StorageAccess.Same(world, job.Result, stack);
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
        var world = Api?.World;
        if (world == null)
            return;

        var left = moved;
        foreach (var job in _jobs)
        {
            if (left <= 0)
                break;
            if (job.Prepaid <= 0 || job.Result == null || !StorageAccess.Same(world, job.Result, stack))
                continue;

            var take = Math.Min(job.Prepaid, left);
            job.Prepaid -= take;
            left -= take;
        }

        foreach (var job in _jobs)
        {
            if (left <= 0)
                break;
            if (job.Fed <= 0 || job.Result == null || !StorageAccess.Same(world, job.Result, stack))
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

    private BlockEntityEStorageAssembler? FindAssembler()
    {
        var world = Api?.World;
        if (world == null || Pos == null)
            return null;

        BlockEntityEStorageAssembler? first = null;
        foreach (var face in BlockFacing.ALLFACES)
        {
            var neighbor = Pos.AddCopy(face);
            if (world.BlockAccessor.GetBlockEntity(neighbor) is not BlockEntityEStorageAssembler assembler)
                continue;
            if (!Feeds(world, assembler.Pos))
                continue;
            if (assembler.Serves(Pos))
                return assembler;
            first ??= assembler;
        }

        return first;
    }

    public BlockPos? MountedOn()
    {
        if (Facing == Facing.None || Pos == null)
            return null;

        BlockFacing? toward = null;
        foreach (var side in FacingHelper.Faces(Facing))
        {
            toward = side;
            break;
        }

        return toward == null ? null : Pos.AddCopy(toward);
    }

    /// <summary>
    /// Полный блок кормит соседний сборщик с любой грани, если лицевая сторона не смотрит в другую машину.
    /// Жернова и сундук по-прежнему только с грани, куда поставлен интерфейс.
    /// </summary>
    public bool Feeds(IWorldAccessor world, BlockPos machine)
    {
        if (Pos == null)
            return false;

        var mount = MountedOn();
        if (mount != null && ProcessorCluster.SamePos(mount, machine))
            return true;
        if (world.BlockAccessor.GetBlock(machine).FirstCodePart() != "estorageassembler")
            return false;
        if (!NextTo(Pos, machine))
            return false;
        if (mount != null && MachineAt(world, mount))
            return false;

        return true;
    }

    /// <summary>
    /// Сетка уже собрана под живой заказ с процессором. Ручной крафт её не перехватывает.
    /// </summary>
    public bool OwnsReadyCraft(IWorldAccessor world, BlockEntityEStorageAssembler assembler)
    {
        if (StorageAccess.Link(world, Pos) != StorageLink.Online)
            return false;

        foreach (var job in _jobs)
        {
            if (job.Got >= job.Wanted)
                continue;
            var pattern = _inventory[job.Slot].Itemstack;
            if (!ItemEStoragePattern.IsEncoded(pattern) || ItemEStoragePattern.IsProcessing(pattern))
                continue;
            if (FindCpu(job) == null)
                continue;
            if (assembler.CraftReady(world, pattern!))
                return true;
        }

        return false;
    }

    private static bool NextTo(BlockPos a, BlockPos b)
    {
        if (a.dimension != b.dimension)
            return false;

        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        if (dx < 0)
            dx = -dx;
        if (dy < 0)
            dy = -dy;
        if (dz < 0)
            dz = -dz;
        return dx + dy + dz == 1;
    }

    private static bool MachineAt(IWorldAccessor world, BlockPos pos)
    {
        var entity = world.BlockAccessor.GetBlockEntity(pos);
        if (entity is BlockEntityContainer or IBlockEntityContainer)
            return true;
        return world.BlockAccessor.GetBlock(pos).FirstCodePart() == "estorageassembler";
    }

    private bool PushCraft(CraftJob job, ItemStack pattern, BlockEntityEStorageAssembler assembler)
    {
        var world = Api?.World;
        if (world == null || assembler.Working)
            return false;

        var served = LiquidCraft.Mask(world, pattern, LiquidCraft.Player(world, assembler.Pos));
        var changed = false;
        for (var i = 0; i < 9; i++)
        {
            if (!served[i])
                continue;
            var occupied = assembler.CraftSlot(i);
            if (occupied.Empty)
                continue;
            if (ReturnToNetwork(occupied))
                changed = true;
        }

        if (!assembler.ReadyFor(world, pattern))
            return changed;

        var per = ItemEStoragePattern.PerCraft(world, pattern);
        var crafts = (job.Wanted + per - 1) / per;
        if (job.Fed >= crafts)
            return changed;
        if (FindCpu(job) is not { } cpu)
            return changed;

        // В сетку уходит сразу набор на один крафт. Один шпагат из четырёх сборщик не занимает.
        if (!assembler.CraftReady(world, pattern) && !SetAvailable(world, cpu, assembler, pattern, served))
        {
            if (ReclaimPartial(world, cpu, assembler))
                changed = true;
            return changed;
        }

        if (CraftGridEmpty(assembler))
        {
            job.Index = 0;
            job.Pushed = 0;
        }

        for (var guard = 0; guard < 12 && job.Fed < crafts; guard++)
        {
            if (assembler.CraftReady(world, pattern))
            {
                var made = ItemEStoragePattern.Primary(world, pattern);
                if (!assembler.Begin(world, made, pattern, cpu, Pos))
                    return changed;
                return true;
            }

            if (job.Index >= 9)
                job.Index = 0;

            while (job.Index < 9)
            {
                var cell = ItemEStoragePattern.Cell(world, pattern, job.Index);
                if (cell == null || served[job.Index])
                {
                    var slot = assembler.CraftSlot(job.Index);
                    if (served[job.Index] && !slot.Empty && ReturnToNetwork(slot))
                        changed = true;
                    job.Index++;
                    job.Pushed = 0;
                    continue;
                }

                var size = Math.Max(1, cell.StackSize);
                var dest = assembler.CraftSlot(job.Index);
                var have = dest.Empty || dest.Itemstack == null || !StorageAccess.Same(world, dest.Itemstack, cell)
                    ? 0
                    : dest.Itemstack.StackSize;
                if (have == 0 && !dest.Empty)
                    return changed;
                if (have >= size)
                {
                    job.Index++;
                    job.Pushed = 0;
                    continue;
                }

                var room = FreeInSlot(dest, cell);
                var take = Math.Min(size - have, room);
                if (take <= 0)
                    return changed;

                var got = cpu.Take(world, cell, take);
                if (got == null || got.StackSize <= 0)
                    return changed;

                var holder = new DummySlot(got);
                var op = new ItemStackMoveOperation(world, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, got.StackSize);
                var moved = holder.TryPutInto(dest, ref op);
                var remain = holder.Itemstack?.StackSize ?? 0;
                if (remain > 0 && holder.Itemstack != null)
                {
                    var kept = cpu.Insert(world, holder.Itemstack, remain, ProcessorCluster.At(world, cpu.Pos).Bytes);
                    remain -= kept;
                    if (remain > 0)
                        GiveBack(holder.Itemstack, remain);
                }

                if (moved <= 0)
                    return changed;

                changed = true;
                if (have + moved < size)
                    return changed;

                job.Index++;
                job.Pushed = 0;
            }

            if (job.Index < 9)
                return changed;
        }

        return changed;
    }

    private static bool CraftGridEmpty(BlockEntityEStorageAssembler assembler)
    {
        for (var i = 0; i < 9; i++)
        {
            if (!assembler.CraftSlot(i).Empty)
                return false;
        }

        return true;
    }

    private static bool SetAvailable(IWorldAccessor world, BlockEntityEStorageProcessor cpu, BlockEntityEStorageAssembler assembler, ItemStack pattern, bool[] served)
    {
        for (var i = 0; i < 9; i++)
        {
            if (served[i])
                continue;

            var cell = ItemEStoragePattern.Cell(world, pattern, i);
            if (cell == null)
                continue;

            var size = Math.Max(1, cell.StackSize);
            long have = cpu.CountOf(world, cell);
            for (var slot = 0; slot < 9; slot++)
            {
                var stack = assembler.CraftSlot(slot).Itemstack;
                if (stack != null && StorageAccess.Same(world, stack, cell))
                    have += stack.StackSize;
            }

            if (have < size)
                return false;

            for (var later = i + 1; later < 9; later++)
            {
                if (served[later])
                    continue;
                var other = ItemEStoragePattern.Cell(world, pattern, later);
                if (other != null && StorageAccess.Same(world, other, cell))
                    size += Math.Max(1, other.StackSize);
            }

            if (have < size)
                return false;
        }

        return true;
    }

    private bool ReturnToNetwork(ItemSlot slot)
    {
        var world = Api?.World;
        var stack = slot.Itemstack;
        if (world == null || stack == null || stack.StackSize <= 0)
            return false;

        var moved = StorageAccess.Insert(world, Pos, stack, stack.StackSize);
        stack.StackSize -= moved;
        if (stack.StackSize > 0)
            world.SpawnItemEntity(stack.Clone(), Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        slot.Itemstack = null;
        slot.MarkDirty();
        return true;
    }

    private static bool ReclaimPartial(IWorldAccessor world, BlockEntityEStorageProcessor cpu, BlockEntityEStorageAssembler assembler)
    {
        var bytes = ProcessorCluster.At(world, cpu.Pos).Bytes;
        var moved = false;
        for (var i = 0; i < 9; i++)
        {
            var slot = assembler.CraftSlot(i);
            if (slot.Empty || slot.Itemstack == null)
                continue;

            var stack = slot.Itemstack;
            var kept = cpu.Insert(world, stack, stack.StackSize, bytes);
            if (kept <= 0)
                continue;

            stack.StackSize -= kept;
            if (stack.StackSize <= 0)
                slot.Itemstack = null;
            slot.MarkDirty();
            moved = true;
        }

        return moved;
    }

    private bool PushIngredient(CraftJob job, ItemStack pattern, InventoryBase target, BlockFacing face)
    {
        var world = Api?.World;
        if (world == null)
            return false;

        ItemEStoragePattern.Inputs(world, pattern, _inputs);
        if (_inputs.Count == 0)
            return false;

        var per = ItemEStoragePattern.Yield(world, pattern, job.Result);
        var crafts = (job.Wanted + per - 1) / per;
        if (job.Fed >= crafts)
            return false;

        if (FindCpu(job) is not { } cpu)
            return false;

        var changed = false;
        for (var step = 0; step < 32 && job.Fed < crafts; step++)
        {
            while (job.Index < _inputs.Count && job.Pushed >= Math.Max(1, _inputs[job.Index].StackSize))
            {
                job.Index++;
                job.Pushed = 0;
            }

            if (job.Index >= _inputs.Count)
            {
                job.Fed++;
                job.Index = 0;
                job.Pushed = 0;
                changed = true;
                continue;
            }

            var need = _inputs[job.Index];
            var size = Math.Max(1, need.StackSize);
            var holder = new DummySlot(need.Clone());
            var dest = target.GetAutoPushIntoSlot(face, holder);
            if (dest == null)
                break;
            if (!dest.Empty && dest.Itemstack != null && !StorageAccess.Same(world, dest.Itemstack, need))
                break;

            var room = FreeInSlot(dest, need);
            if (room <= 0)
                break;

            var owed = (crafts - job.Fed) * size - job.Pushed;
            if (_inputs.Count > 1)
                owed = Math.Min(owed, size - job.Pushed);
            var take = Math.Min(Math.Max(owed, 0), room);
            if (take <= 0)
                break;

            var got = cpu.Take(world, need, take);
            if (got == null || got.StackSize <= 0)
                break;

            holder = new DummySlot(got);
            var op = new ItemStackMoveOperation(world, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, got.StackSize);
            var moved = holder.TryPutInto(dest, ref op);
            var remain = holder.Itemstack?.StackSize ?? 0;
            if (remain > 0 && holder.Itemstack != null)
            {
                var kept = cpu.Insert(world, holder.Itemstack, remain, ProcessorCluster.At(world, cpu.Pos).Bytes);
                remain -= kept;
                if (remain > 0)
                    GiveBack(holder.Itemstack, remain);
            }
            if (moved <= 0)
                break;

            changed = true;
            job.Pushed += moved;
            while (job.Pushed >= size)
            {
                job.Pushed -= size;
                job.Index++;
                if (job.Index >= _inputs.Count)
                {
                    job.Fed++;
                    job.Index = 0;
                }

                if (_inputs.Count > 1)
                    break;
            }

            if (moved < take || room - moved <= 0)
                break;
        }

        return changed;
    }

    /// <summary>
    /// Сколько ещё влезет во вход машины. Без машины остаётся заданное в образце число.
    /// </summary>
    private int WantInBuffer(ItemStack goal)
    {
        if (!TryMount(out var inventory, out var face))
            return Math.Max(goal.StackSize, 0);

        var dest = inventory.GetAutoPushIntoSlot(face, new DummySlot(goal));
        if (dest == null || Api?.World == null)
            return 0;
        if (!dest.Empty && dest.Itemstack != null && !StorageAccess.Same(Api.World, dest.Itemstack, goal))
            return 0;

        return FreeInSlot(dest, goal);
    }

    /// <summary>
    /// Слот машины по умолчанию вмещает 999999. Реальный предел — размер стака предмета.
    /// Лишнее возвращается в сеть.
    /// </summary>
    private int FreeInSlot(ItemSlot dest, ItemStack sample)
    {
        var max = StackCap(dest, sample);
        if (Api?.World != null && !dest.Empty && dest.Itemstack != null && StorageAccess.Same(Api.World, dest.Itemstack, sample))
        {
            var extra = dest.Itemstack.StackSize - max;
            if (extra > 0)
            {
                var back = StorageAccess.Insert(Api.World, Pos, dest.Itemstack, extra);
                dest.Itemstack.StackSize -= back;
                if (dest.Itemstack.StackSize <= 0)
                    dest.Itemstack = null;
                dest.MarkDirty();
            }
        }

        var have = dest.Empty || dest.Itemstack == null ? 0 : dest.Itemstack.StackSize;
        return Math.Max(0, max - have);
    }

    private static int StackCap(ItemSlot dest, ItemStack sample)
    {
        var max = Math.Max(1, sample.Collectible?.MaxStackSize ?? 1);
        var slotMax = dest.MaxSlotStackSize;
        if (slotMax > 0 && slotMax < max)
            max = slotMax;
        return max;
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
        if (!dest.Empty && dest.Itemstack != null && !StorageAccess.Same(Api.World, dest.Itemstack, slot.Itemstack))
            return;

        var room = FreeInSlot(dest, slot.Itemstack);
        if (room <= 0)
            return;

        var op = new ItemStackMoveOperation(Api.World, EnumMouseButton.Left, 0, EnumMergePriority.DirectMerge, Math.Min(slot.Itemstack.StackSize, room));
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

    private static BlockPos? ReadCpu(ITreeAttribute tree, int index)
    {
        if (tree.GetInt("jobCpu" + index) == 0)
            return null;

        var pos = new BlockPos(tree.GetInt("jobCpuX" + index), tree.GetInt("jobCpuY" + index), tree.GetInt("jobCpuZ" + index));
        pos.dimension = tree.GetInt("jobCpuD" + index);
        return pos;
    }

    private bool AttachCpu(CraftJob job, ItemStack pattern, int extra, BlockPos? share = null)
    {
        var world = Api?.World;
        if (world == null)
            return false;

        if (share == null)
            share = WaveCpu(job);
        if (share == null && job.Cpu != null)
            share = job.Cpu;

        if (share != null)
        {
            var view = ProcessorCluster.At(world, share);
            if (ProcessorCluster.Entity(world, view.Anchor) is BlockEntityEStorageProcessor holder)
            {
                var current = FindCpu(job);
                if (current != null && ProcessorCluster.SamePos(current.Pos, holder.Pos))
                {
                    job.Cpu = holder.Pos.Copy();
                    return true;
                }

                if (current != null)
                    current.YieldLink(world, Pos, job.Slot, Seat, holder);
                if (!holder.Join(Pos, job.Slot, Seat))
                    return false;

                job.Cpu = holder.Pos.Copy();
                if (current != null)
                    holder.Settle();
                return true;
            }

            if (job.Cpu != null && ProcessorCluster.SamePos(job.Cpu, share))
                job.Cpu = null;
        }

        if (FindCpu(job) != null)
            return true;

        if (!Reserve(job, pattern, extra, out var cpuPos) || cpuPos == null)
            return false;

        if (world.BlockAccessor.GetBlockEntity(cpuPos) is not BlockEntityEStorageProcessor processor)
            return false;
        if (!processor.Claims(Pos, job.Slot, Seat) && !processor.Claim(Pos, job.Slot, Seat))
            return false;

        job.Cpu = cpuPos.Copy();
        return true;
    }

    private BlockPos? WaveCpu(CraftJob job)
    {
        var world = Api?.World;
        if (world == null || Pos == null)
            return null;
        if (job.Batch == 0 && job.Since <= 0)
            return null;

        BlockPos? best = null;
        foreach (var cell in StorageAccess.GetScan(world, Pos).Interfaces)
        {
            foreach (var face in PanelStack.Interfaces(world, cell))
                face.CollectCpu(job.Batch, job.Since, ref best);
        }

        return best;
    }

    private bool Reserve(CraftJob? job, ItemStack pattern, int count, out BlockPos? cpu)
    {
        cpu = null;
        var world = Api?.World;
        if (world == null)
            return false;

        if (job != null && FindCpu(job) is { } bound)
        {
            cpu = bound.Pos.Copy();
            return true;
        }

        var scan = StorageAccess.GetScan(world, Pos);
        if (scan.Processors.Count == 0)
            return false;

        var wanted = Math.Min(100000, (job?.Wanted ?? 0) + Math.Max(count, 0));
        var plan = IngredientPlan(world, job, pattern, Math.Max(wanted, 1));
        BlockEntityEStorageProcessor? fit = null;
        var fitBytes = 0;
        BlockEntityEStorageProcessor? roomy = null;
        var roomyBytes = -1;
        foreach (var pos in scan.Processors)
        {
            var view = ProcessorCluster.At(world, pos);
            if (view.Merged && !ProcessorCluster.SamePos(view.Anchor, pos))
                continue;
            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageProcessor processor || processor.HasClaim)
                continue;

            if (roomy == null || view.Bytes > roomyBytes || (view.Bytes == roomyBytes && StorageScan.Earlier(pos, roomy.Pos)))
            {
                roomy = processor;
                roomyBytes = view.Bytes;
            }

            var local = new List<(ItemStack Proto, long Extra)>(plan);
            ApplyStored(world, local, processor);
            if (!ProcessorCluster.CanHold(world, view.Bytes, processor.Contents, local))
                continue;
            if (fit != null && (view.Bytes > fitBytes || (view.Bytes == fitBytes && !StorageScan.Earlier(pos, fit.Pos))))
                continue;

            fit = processor;
            fitBytes = view.Bytes;
        }

        var pick = fit ?? roomy;
        if (pick == null)
            return false;

        cpu = pick.Pos.Copy();
        return true;
    }

    private BlockEntityEStorageProcessor? FindCpu(CraftJob job)
    {
        var world = Api?.World;
        if (world == null)
            return null;

        if (job.Cpu != null && world.BlockAccessor.GetBlockEntity(job.Cpu) is BlockEntityEStorageProcessor direct && direct.Claims(Pos, job.Slot, Seat))
            return direct;

        foreach (var pos in StorageAccess.GetScan(world, Pos).Processors)
        {
            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageProcessor processor || !processor.Claims(Pos, job.Slot, Seat))
                continue;
            job.Cpu = processor.Pos.Copy();
            return processor;
        }

        return null;
    }

    private void ReturnAssembler(CraftJob job)
    {
        var world = Api?.World;
        var assembler = FindAssembler();
        if (world == null || assembler == null)
            return;

        var current = job.Slot >= 0 && job.Slot < _inventory.Count ? _inventory[job.Slot].Itemstack : null;
        if (current != null && assembler.Holding(world, current))
        {
            assembler.GiveAll(world, Pos);
            return;
        }

        foreach (var other in _jobs)
        {
            if (other == job || other.Got >= other.Wanted)
                continue;
            var pattern = _inventory[other.Slot].Itemstack;
            if (pattern != null && assembler.Holding(world, pattern))
                return;
        }

        if (assembler.BusyGrid())
            assembler.GiveAll(world, Pos);
    }

    private void ReleaseJob(CraftJob job)
    {
        ReturnAssembler(job);
        ClearStamp(job);
        var cpu = FindCpu(job);
        if (cpu != null && Api?.World != null)
            cpu.Unlink(Api.World, Pos, job.Slot, Seat);
        job.Cpu = null;
    }

    private void ClearStamp(CraftJob job)
    {
        if (job.Slot < 0 || job.Slot >= _inventory.Count)
            return;
        var stack = _inventory[job.Slot].Itemstack;
        if (stack != null && ItemEStoragePattern.JobId(stack) == job.Id)
            ItemEStoragePattern.SetJobId(stack, 0);
    }

    private bool Stock(CraftJob job, ItemStack pattern)
    {
        var world = Api?.World;
        if (world == null)
            return false;

        var cpu = FindCpu(job);
        if (cpu == null)
            return false;

        var view = ProcessorCluster.At(world, cpu.Pos);
        var plan = IngredientPlan(world, job, pattern, job.Wanted);
        var changed = false;
        foreach (var (proto, extra) in plan)
        {
            if (extra <= 0)
                continue;

            var others = cpu.OwedByOthers(world, proto, Pos, job.Slot, Seat);
            var pool = (long)cpu.CountOf(world, proto);
            var shortfall = extra + others - pool;
            if (shortfall < 0)
                shortfall = 0;
            var want = Math.Min(extra, shortfall);
            var room = StorageAccess.RoomForAdditional(view.Bytes, cpu.TypeCount, cpu.ItemCount, !cpu.Contains(world, proto));
            var pull = (int)Math.Min(want, (long)room);
            if (pull <= 0)
                continue;

            var liquid = StorageAccess.LiquidProps(proto) != null;
            var got = liquid
                ? StorageAccess.ExtractLiquid(world, Pos, proto, pull)
                : StorageAccess.Extract(world, Pos, proto, pull);
            if (got == null || got.StackSize <= 0)
                continue;

            var kept = cpu.Insert(world, got, got.StackSize, view.Bytes);
            var left = got.StackSize - kept;
            if (left > 0)
            {
                if (liquid)
                {
                    left -= StorageAccess.InsertLiquid(world, Pos, got, left);
                    if (left > 0)
                    {
                        var drop = got.Clone();
                        drop.StackSize = left;
                        world.SpawnItemEntity(drop, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                    }
                }
                else
                    GiveBack(got, left);
            }
            if (kept > 0)
                changed = true;
        }

        return changed;
    }

    private List<(ItemStack Proto, long Extra)> IngredientPlan(IWorldAccessor world, CraftJob? job, ItemStack pattern, int wanted)
    {
        var plan = new List<(ItemStack Proto, long Extra)>();
        if (wanted <= 0)
            return plan;

        var per = job?.Result != null
            ? ItemEStoragePattern.Yield(world, pattern, job.Result)
            : ItemEStoragePattern.PerCraft(world, pattern);
        var crafts = (wanted + per - 1) / per;
        var liquids = FindAssembler() != null
            ? LiquidCraft.Collect(world, pattern, LiquidCraft.Player(world, Pos))
            : new List<LiquidNeed>();
        var served = new bool[9];
        foreach (var need in liquids)
        {
            served[need.Cell] = true;
            var size = need.Portions;
            long delivered = job == null ? 0 : (long)job.Fed * size;
            var owed = (long)crafts * size - delivered;
            if (owed < 0)
                owed = 0;
            Merge(plan, need.Liquid, owed);
        }

        var i = 0;
        for (var cell = 0; cell < 9; cell++)
        {
            var input = ItemEStoragePattern.Cell(world, pattern, cell);
            if (input == null)
                continue;
            if (served[cell])
                continue;

            var size = Math.Max(1, input.StackSize);
            long delivered = 0;
            if (job != null)
            {
                long sets = job.Fed;
                if (i < job.Index)
                    sets++;
                var partial = i == job.Index ? job.Pushed : 0;
                delivered = sets * size + partial;
            }

            var owed = (long)crafts * size - delivered;
            if (owed < 0)
                owed = 0;

            Merge(plan, input, owed);
            i++;
        }

        return plan;

        void Merge(List<(ItemStack Proto, long Extra)> into, ItemStack proto, long extra)
        {
            for (var k = 0; k < into.Count; k++)
            {
                if (!StorageAccess.Same(world, into[k].Proto, proto))
                    continue;
                into[k] = (into[k].Proto, into[k].Extra + extra);
                return;
            }

            into.Add((proto, extra));
        }
    }

    private static void ApplyStored(IWorldAccessor world, List<(ItemStack Proto, long Extra)> plan, BlockEntityEStorageProcessor cpu)
    {
        for (var i = 0; i < plan.Count; i++)
        {
            var left = plan[i].Extra - cpu.CountOf(world, plan[i].Proto);
            if (left < 0)
                left = 0;
            plan[i] = (plan[i].Proto, left);
        }
    }

    private sealed class CraftJob
    {
        public int Slot;
        public int Wanted;
        public int Got;
        public int Fed;
        public int Prepaid;
        public int Index;
        public int Pushed;
        public ItemStack? Result;
        public BlockPos? Cpu;
        public double Since;
        public long Id;
        public long Batch;
    }
}
