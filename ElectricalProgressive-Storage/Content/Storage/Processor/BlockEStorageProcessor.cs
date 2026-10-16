using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ElectricalProgressive.Content.Storage.Processor;

/// <summary>
/// Процессор крафта. Один заказ на блок: всё дерево шаблонов сидит здесь, как ЦПУ в AE2.
/// Сплошной прямоугольник из нескольких процессоров — один блок, память складывается.
/// </summary>
public class BlockEStorageProcessor : BlockEStoragePart
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel == null || !world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        if (world.Side != EnumAppSide.Client || world.Api is not ICoreClientAPI capi)
            return true;

        GuiDialogEStorageProcessor.Toggle(capi, blockSel.Position);
        return true;
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        world.BlockAccessor.MarkBlockDirty(pos);
        foreach (var face in BlockFacing.ALLFACES)
        {
            var at = pos.AddCopy(face);
            if (ProcessorCluster.IsProcessor(world.BlockAccessor.GetBlock(at)))
                world.BlockAccessor.MarkBlockDirty(at);
        }

        if (world.Side != EnumAppSide.Server)
            return;
        if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityEStorageProcessor processor)
            processor.Settle();
    }

    public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
    {
        if (api is not ICoreClientAPI capi || chunkExtBlocks == null)
        {
            base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);
            return;
        }

        var mask = ProcessorJoinMesh.Mask(chunkExtBlocks, extIndex3d, capi.World.BlockAccessor, pos);
        var mesh = ProcessorJoinMesh.For(capi, this, mask);
        if (mesh == null)
        {
            base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);
            return;
        }

        sourceMesh = mesh.Clone();
        base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);
    }
}

/// <summary>
/// Буфер ЦПУ и ссылки на шаблоны. У гостевой грани своя ссылка.
/// </summary>
public class BlockEntityEStorageProcessor : BlockEntity
{
    public const int StatusPacket = 19110;
    public const int CancelPacket = 19111;

    private readonly List<ItemStack> _stacks = new();
    private readonly List<BlockPos> _linkPos = new();
    private readonly List<int> _linkSlot = new();
    private readonly List<string> _linkSeat = new();

    public bool HasClaim => _linkPos.Count > 0;

    public IReadOnlyList<ItemStack> Contents => _stacks;

    public int TypeCount
    {
        get
        {
            var count = 0;
            foreach (var stack in _stacks)
            {
                if (stack.StackSize > 0 && stack.Collectible != null)
                    count++;
            }

            return count;
        }
    }

    public int ItemCount
    {
        get
        {
            var count = 0;
            foreach (var stack in _stacks)
            {
                if (stack.StackSize > 0)
                    count += stack.StackSize;
            }

            return count;
        }
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side != EnumAppSide.Server)
            return;

        Settle();
        RegisterGameTickListener(_ => Remind(), 1000);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (_stacks.Count == 0)
            tree.RemoveAttribute("buf");
        else
        {
            var buf = new TreeAttribute();
            for (var i = 0; i < _stacks.Count; i++)
                buf["s" + i.ToString("D3")] = new ItemstackAttribute(_stacks[i].Clone());
            tree["buf"] = buf;
        }

        tree.SetInt("links", _linkPos.Count);
        for (var i = 0; i < _linkPos.Count; i++)
        {
            tree.SetInt("lkS" + i, _linkSlot[i]);
            tree.SetInt("lkX" + i, _linkPos[i].X);
            tree.SetInt("lkY" + i, _linkPos[i].Y);
            tree.SetInt("lkZ" + i, _linkPos[i].Z);
            tree.SetInt("lkD" + i, _linkPos[i].dimension);
            tree.SetString("lkF" + i, i < _linkSeat.Count ? _linkSeat[i] : "");
        }

        if (_linkPos.Count == 0)
        {
            tree.SetInt("claim", -1);
            return;
        }

        tree.SetInt("claim", _linkSlot[0]);
        tree.SetInt("claimX", _linkPos[0].X);
        tree.SetInt("claimY", _linkPos[0].Y);
        tree.SetInt("claimZ", _linkPos[0].Z);
        tree.SetInt("claimD", _linkPos[0].dimension);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        _stacks.Clear();
        if (tree["buf"] is ITreeAttribute buf)
        {
            var names = new List<string>();
            foreach (var kv in buf)
                names.Add(kv.Key);
            names.Sort(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (buf[name] is not ItemstackAttribute { value: { StackSize: > 0 } stack })
                    continue;
                if (stack.Collectible == null && !stack.ResolveBlockOrItem(worldAccessForResolve))
                    continue;
                if (stack.Collectible == null)
                    continue;
                _stacks.Add(stack.Clone());
            }
        }

        Normalize(worldAccessForResolve);
        ClearLinks();
        var links = tree.GetInt("links", -1);
        if (links > 0)
        {
            if (links > 64)
                links = 64;
            for (var i = 0; i < links; i++)
            {
                var slot = tree.GetInt("lkS" + i, -1);
                if (slot < 0)
                    continue;
                var pos = new BlockPos(tree.GetInt("lkX" + i), tree.GetInt("lkY" + i), tree.GetInt("lkZ" + i));
                pos.dimension = tree.GetInt("lkD" + i);
                var seat = tree.HasAttribute("lkF" + i) ? tree.GetString("lkF" + i) ?? "" : "";
                AddLink(pos, slot, seat);
            }

            return;
        }

        var claim = tree.GetInt("claim", -1);
        if (claim < 0)
            return;

        var legacy = new BlockPos(tree.GetInt("claimX"), tree.GetInt("claimY"), tree.GetInt("claimZ"));
        legacy.dimension = tree.GetInt("claimD");
        AddLink(legacy, claim, "");
    }

    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
    {
        var api = Api;
        var world = api?.World;
        if (api == null || world == null || api.Side != EnumAppSide.Server)
            return;
        if (packetid != StatusPacket && packetid != CancelPacket)
        {
            base.OnReceivedClientPacket(fromPlayer, packetid, data);
            return;
        }

        if (!world.Claims.TryAccess(fromPlayer, Pos, EnumBlockAccessFlags.Use) || fromPlayer is not IServerPlayer player)
            return;

        var view = ProcessorCluster.At(world, Pos);
        var holder = ProcessorCluster.Entity(world, view.Anchor) ?? this;
        if (packetid == CancelPacket)
        {
            var index = data is { Length: >= 4 } ? BitConverter.ToInt32(data, 0) : -1;
            holder.CancelClaim(index);
        }

        var tree = new TreeAttribute();
        holder.WriteStatus(tree);
        ((ICoreServerAPI)api).Network.SendBlockEntityPacket(player, Pos, StatusPacket, tree.ToBytes());
    }

    public override void OnReceivedServerPacket(int packetid, byte[] data)
    {
        if (packetid == StatusPacket && data is { Length: > 0 })
        {
            if (Api is ICoreClientAPI capi)
                GuiDialogEStorageProcessor.Deliver(capi, Pos, data);
            return;
        }

        base.OnReceivedServerPacket(packetid, data);
    }

    public void WriteStatus(ITreeAttribute tree)
    {
        tree.SetInt("n", 0);
        var world = Api?.World;
        if (world == null || _linkPos.Count == 0)
            return;

        var n = 0;
        double earliest = 0;
        var haveTime = false;
        for (var i = 0; i < _linkPos.Count && n < 64; i++)
        {
            var panel = LinkedFace(world, i);
            if (panel == null)
                continue;
            if (!panel.TryJob(_linkSlot[i], out var result, out var left, out var since) || result == null)
                continue;

            var shown = result.Clone();
            shown.StackSize = 1;
            tree["s" + n] = new ItemstackAttribute(shown);
            tree.SetInt("c" + n, left);
            if (!haveTime || since < earliest)
            {
                earliest = since;
                haveTime = true;
            }

            n++;
        }

        tree.SetInt("n", n);
        if (haveTime)
            tree.SetDouble("e0", RealSeconds(world, earliest));
    }

    public void CancelClaim(int index)
    {
        if (index < 0 || Api?.World == null || _linkPos.Count == 0)
            return;

        var world = Api.World;
        var panels = _linkPos.ToArray();
        var slots = _linkSlot.ToArray();
        var seats = _linkSeat.ToArray();
        for (var i = 0; i < panels.Length; i++)
        {
            var seat = i < seats.Length ? seats[i] : "";
            PanelStack.Interface(world, panels[i], slots[i], seat)?.CancelJob(slots[i]);
        }
    }

    private static double RealSeconds(IWorldAccessor world, double since)
    {
        var calendar = world.Calendar;
        // SpeedOfTime — игровые секунды за одну реальную, CalendarSpeedMul по умолчанию ещё половинит.
        var delta = calendar.ElapsedSeconds - since;
        if (delta <= 0)
            return 0;

        var rate = calendar.SpeedOfTime * calendar.CalendarSpeedMul;
        if (rate < 1)
            rate = 1;
        return delta / rate;
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        if (Api?.World == null)
            return;

        var view = ProcessorCluster.At(Api.World, Pos);
        var holder = ProcessorCluster.Entity(Api.World, view.Anchor) ?? this;
        var used = StorageAccess.UsedBytes(view.Bytes, holder.TypeCount, holder.ItemCount);
        dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-cpu-bytes", used, view.Bytes));
        dsc.AppendLine(Lang.Get(BlockEStorageTerminal.LinkKey(StorageAccess.Link(Api.World, Pos))));
    }

    public bool Claims(BlockPos panel, int slot, string seat)
    {
        for (var i = 0; i < _linkPos.Count; i++)
        {
            if (SameLink(i, panel, slot, seat))
                return true;
        }

        return false;
    }

    public bool Claim(BlockPos panel, int slot, string seat)
    {
        if (Claims(panel, slot, seat))
            return true;
        if (HasClaim)
            return false;

        return Join(panel, slot, seat);
    }

    /// <summary>
    /// Тот же заказ: шаблон садится на занятый процессор и отдельный ЦПУ не берёт.
    /// </summary>
    public bool Join(BlockPos panel, int slot, string seat)
    {
        if (Claims(panel, slot, seat))
            return true;

        AddLink(panel, slot, seat);
        MarkDirty();
        return true;
    }

    public void Unlink(IWorldAccessor world, BlockPos panel, int slot, string seat)
    {
        if (!RemoveLink(panel, slot, seat))
            return;

        PanelStack.Interface(world, panel, slot, seat)?.DetachCpu(Pos, slot);
        if (_linkPos.Count == 0)
        {
            SpillAll(world);
            MarkDirty();
            Settle();
            return;
        }

        MarkDirty();
    }

    /// <summary>
    /// Тот же буфер, другая панель. Ссылка переписывается на месте, без слива в сеть.
    /// </summary>
    public bool Retarget(BlockPos from, int fromSlot, string fromSeat, BlockPos to, int toSlot, string toSeat)
    {
        var index = -1;
        for (var i = 0; i < _linkPos.Count; i++)
        {
            if (!SameLink(i, from, fromSlot, fromSeat))
                continue;
            index = i;
            break;
        }

        if (index < 0)
            return fromSlot == toSlot && (fromSeat ?? "") == (toSeat ?? "") && ProcessorCluster.SamePos(from, to);

        if (SameLink(index, to, toSlot, toSeat))
            return true;

        for (var i = 0; i < _linkPos.Count; i++)
        {
            if (i == index)
                continue;
            if (!SameLink(i, to, toSlot, toSeat))
                continue;

            DropLink(index);
            MarkDirty();
            return true;
        }

        _linkPos[index] = to.Copy();
        _linkSlot[index] = toSlot;
        if (index < _linkSeat.Count)
            _linkSeat[index] = toSeat ?? "";
        MarkDirty();
        return true;
    }

    public void GiveLinks(IWorldAccessor world, BlockEntityEStorageProcessor heir)
    {
        var panels = _linkPos.ToArray();
        var slots = _linkSlot.ToArray();
        var seats = _linkSeat.ToArray();
        ClearLinks();
        for (var i = 0; i < panels.Length; i++)
        {
            var seat = i < seats.Length ? seats[i] : "";
            if (!heir.Join(panels[i], slots[i], seat))
                continue;
            PanelStack.Interface(world, panels[i], slots[i], seat)?.AttachCpu(heir.Pos, slots[i]);
        }

        MarkDirty();
    }

    /// <summary>
    /// Ссылка этого шага уходит на общий процессор заказа. Буфер переезжает вместе с последней ссылкой.
    /// </summary>
    public void YieldLink(IWorldAccessor world, BlockPos panel, int slot, string seat, BlockEntityEStorageProcessor heir)
    {
        if (ReferenceEquals(this, heir) || ProcessorCluster.SamePos(Pos, heir.Pos))
            return;
        if (!RemoveLink(panel, slot, seat))
            return;

        if (_linkPos.Count == 0)
        {
            foreach (var stack in TakeAll())
                heir.Absorb(world, stack);
        }

        MarkDirty();
    }

    public long OwedByOthers(IWorldAccessor world, ItemStack proto, BlockPos panel, int slot, string seat)
    {
        long sum = 0;
        for (var i = 0; i < _linkPos.Count; i++)
        {
            if (SameLink(i, panel, slot, seat))
                continue;
            var face = LinkedFace(world, i);
            if (face == null)
                continue;
            sum += face.IngredientOwed(_linkSlot[i], proto);
        }

        return sum;
    }

    public void ClearClaim()
    {
        ClearLinks();
        MarkDirty();
    }

    private bool RemoveLink(BlockPos panel, int slot, string seat)
    {
        for (var i = 0; i < _linkPos.Count; i++)
        {
            if (!SameLink(i, panel, slot, seat))
                continue;

            DropLink(i);
            return true;
        }

        return false;
    }

    private void DetachAll(IWorldAccessor world)
    {
        for (var i = 0; i < _linkPos.Count; i++)
        {
            LinkedFace(world, i)?.DetachCpu(Pos, _linkSlot[i]);
        }
    }

    private BlockEntityEStorageInterface? LinkedFace(IWorldAccessor world, int index)
    {
        if (index < 0 || index >= _linkPos.Count)
            return null;
        var seat = index < _linkSeat.Count ? _linkSeat[index] : "";
        return PanelStack.Interface(world, _linkPos[index], _linkSlot[index], seat);
    }

    private bool SameLink(int index, BlockPos panel, int slot, string seat)
    {
        if (index < 0 || index >= _linkPos.Count || _linkSlot[index] != slot || !ProcessorCluster.SamePos(_linkPos[index], panel))
            return false;
        var stored = index < _linkSeat.Count ? _linkSeat[index] : "";
        return stored == (seat ?? "");
    }

    private void AddLink(BlockPos panel, int slot, string seat)
    {
        _linkPos.Add(panel.Copy());
        _linkSlot.Add(slot);
        _linkSeat.Add(seat ?? "");
    }

    private void DropLink(int index)
    {
        _linkPos.RemoveAt(index);
        _linkSlot.RemoveAt(index);
        if (index < _linkSeat.Count)
            _linkSeat.RemoveAt(index);
    }

    private void ClearLinks()
    {
        _linkPos.Clear();
        _linkSlot.Clear();
        _linkSeat.Clear();
    }

    public bool Contains(IWorldAccessor world, ItemStack proto) => IndexOf(world, proto) >= 0;

    public int CountOf(IWorldAccessor world, ItemStack proto)
    {
        var index = IndexOf(world, proto);
        return index < 0 ? 0 : _stacks[index].StackSize;
    }

    public int Insert(IWorldAccessor world, ItemStack proto, int count, int bytes)
    {
        if (count <= 0 || proto.Collectible == null)
            return 0;

        var room = StorageAccess.RoomForAdditional(bytes, TypeCount, ItemCount, !Contains(world, proto));
        var add = Math.Min(count, room);
        if (add <= 0)
            return 0;

        var index = IndexOf(world, proto);
        if (index >= 0)
        {
            var sum = (long)_stacks[index].StackSize + add;
            _stacks[index].StackSize = (int)Math.Min(sum, int.MaxValue);
        }
        else
        {
            var copy = proto.Clone();
            copy.StackSize = add;
            _stacks.Add(copy);
        }

        MarkDirty();
        return add;
    }

    public ItemStack? Take(IWorldAccessor world, ItemStack proto, int count)
    {
        var index = IndexOf(world, proto);
        if (index < 0 || count <= 0)
            return null;

        var have = _stacks[index];
        var taken = Math.Min(count, have.StackSize);
        var copy = have.Clone();
        copy.StackSize = taken;
        have.StackSize -= taken;
        if (have.StackSize <= 0)
            _stacks.RemoveAt(index);
        MarkDirty();
        return copy;
    }

    public void Absorb(IWorldAccessor world, ItemStack stack)
    {
        if (stack.StackSize <= 0 || stack.Collectible == null)
            return;

        var index = IndexOf(world, stack);
        if (index >= 0)
        {
            var sum = (long)_stacks[index].StackSize + stack.StackSize;
            _stacks[index].StackSize = (int)Math.Min(sum, int.MaxValue);
        }
        else
            _stacks.Add(stack.Clone());
    }

    public List<ItemStack> TakeAll()
    {
        var copy = new List<ItemStack>();
        foreach (var stack in _stacks)
        {
            if (stack.StackSize > 0 && stack.Collectible != null)
                copy.Add(stack.Clone());
        }

        _stacks.Clear();
        return copy;
    }

    /// <summary>
    /// Сосед появился или пропал: сплошной прямоугольник собирает предметы на якорь.
    /// Лишнее уходит в сеть. Два живых заказа не сливаются, пока один не закончится.
    /// </summary>
    public void Settle()
    {
        var api = Api;
        var world = api?.World;
        if (api == null || world == null || api.Side != EnumAppSide.Server || ProcessorCluster.Busy)
            return;

        ProcessorCluster.Busy = true;
        try
        {
            var view = ProcessorCluster.At(world, Pos);
            if (view.Incomplete)
                return;

            var anchor = ProcessorCluster.Entity(world, view.Anchor) ?? this;
            if (view.Merged)
            {
                foreach (var member in view.Members)
                {
                    if (ProcessorCluster.SamePos(member, anchor.Pos))
                        continue;
                    if (world.BlockAccessor.GetBlockEntity(member) is not BlockEntityEStorageProcessor other)
                        continue;

                    foreach (var stack in other.TakeAll())
                        anchor.Absorb(world, stack);
                    if (other.HasClaim)
                        other.GiveLinks(world, anchor);

                    other.MarkDirty();
                }
            }

            if (!anchor.HasClaim && anchor._stacks.Count > 0)
                anchor.SpillAll(world);
            else
                anchor.EjectOverflow(world, view.Merged && ProcessorCluster.SamePos(anchor.Pos, view.Anchor) ? view.Bytes : ProcessorCluster.BytesOf(world.BlockAccessor.GetBlock(anchor.Pos)));

            anchor.MarkDirty();
            StorageAccess.DirtyTopology();
        }
        finally
        {
            ProcessorCluster.Busy = false;
        }
    }

    public void EjectOverflow(IWorldAccessor world, int bytes)
    {
        var guard = 0;
        while (_stacks.Count > 0 && guard++ < 80 && !Fits(bytes))
        {
            var last = _stacks[^1];
            var removed = last.Clone();
            _stacks.RemoveAt(_stacks.Count - 1);
            var kept = Insert(world, removed, removed.StackSize, bytes);
            var left = removed.StackSize - kept;
            if (left > 0)
                Spill(world, removed, left);
        }
    }

    public void Release(IWorldAccessor world)
    {
        SpillAll(world);
        DetachAll(world);
        ClearLinks();
        MarkDirty();
        Settle();
    }

    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        var api = Api;
        var world = api?.World;
        if (api != null && world != null && api.Side == EnumAppSide.Server)
        {
            var creative = byPlayer?.WorldData.CurrentGameMode == EnumGameMode.Creative;
            var view = ProcessorCluster.At(world, Pos);
            BlockEntityEStorageProcessor? heir = null;
            if (view.Merged)
            {
                foreach (var member in view.Members)
                {
                    if (ProcessorCluster.SamePos(member, Pos))
                        continue;
                    if (world.BlockAccessor.GetBlockEntity(member) is not BlockEntityEStorageProcessor other)
                        continue;
                    if (heir == null || StorageScan.Earlier(other.Pos, heir.Pos))
                        heir = other;
                }
            }

            if (heir != null)
                HandTo(world, heir);
            else if (creative)
                Void(world);
            else
                Release(world);
        }

        base.OnBlockBroken(byPlayer);
    }

    private void HandTo(IWorldAccessor world, BlockEntityEStorageProcessor heir)
    {
        foreach (var stack in TakeAll())
            heir.Absorb(world, stack);
        GiveLinks(world, heir);
        MarkDirty();
        heir.MarkDirty();
    }

    private void Void(IWorldAccessor world)
    {
        _stacks.Clear();
        DetachAll(world);
        ClearLinks();
    }

    private void SpillAll(IWorldAccessor world)
    {
        for (var i = _stacks.Count - 1; i >= 0; i--)
        {
            var stack = _stacks[i];
            Spill(world, stack, stack.StackSize);
            _stacks.RemoveAt(i);
        }
    }

    private void Spill(IWorldAccessor world, ItemStack proto, int count)
    {
        if (count <= 0)
            return;

        var moved = 0;
        if (StorageAccess.Link(world, Pos) == StorageLink.Online)
        {
            moved = StorageAccess.LiquidProps(proto) != null
                ? StorageAccess.InsertLiquid(world, Pos, proto, count)
                : StorageAccess.Insert(world, Pos, proto, count);
        }
        var left = count - moved;
        if (left <= 0)
            return;

        var drop = proto.Clone();
        drop.StackSize = left;
        world.SpawnItemEntity(drop, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }

    private void Remind()
    {
        var api = Api;
        var world = api?.World;
        if (api == null || world == null || api.Side != EnumAppSide.Server || _linkPos.Count == 0)
            return;

        for (var i = _linkPos.Count - 1; i >= 0; i--)
        {
            var panelPos = _linkPos[i];
            var slot = _linkSlot[i];
            var seat = i < _linkSeat.Count ? _linkSeat[i] : "";
            var panel = PanelStack.Interface(world, panelPos, slot, seat);
            if (panel == null)
            {
                DropLink(i);
                MarkDirty();
                continue;
            }

            var owner = panel.CpuOf(slot);
            if (owner != null && !ProcessorCluster.SamePos(owner, Pos)
                && world.BlockAccessor.GetBlockEntity(owner) is BlockEntityEStorageProcessor other
                && other.Claims(panelPos, slot, seat))
            {
                if (_linkPos.Count == 1)
                {
                    foreach (var stack in TakeAll())
                        other.Absorb(world, stack);
                    ClearLinks();
                    MarkDirty();
                    other.MarkDirty();
                    return;
                }

                DropLink(i);
                MarkDirty();
                continue;
            }

            if (!panel.OwnsJob(slot))
            {
                Unlink(world, panelPos, slot, seat);
                continue;
            }

            panel.AttachCpu(Pos, slot);
        }

        if (_linkPos.Count == 0 && _stacks.Count > 0)
            SpillAll(world);
    }

    private bool Fits(int bytes)
    {
        var types = TypeCount;
        if (types == 0)
            return true;
        var items = ItemCount;
        if (items < 0)
            return false;
        return StorageAccess.Fits(bytes, types, items);
    }

    private int IndexOf(IWorldAccessor world, ItemStack proto)
    {
        if (proto.Collectible == null)
            return -1;

        for (var i = 0; i < _stacks.Count; i++)
        {
            if (_stacks[i].StackSize > 0 && StorageAccess.Same(world, _stacks[i], proto))
                return i;
        }

        return -1;
    }

    private void Normalize(IWorldAccessor world)
    {
        for (var i = 0; i < _stacks.Count; i++)
        {
            if (_stacks[i].StackSize <= 0 || _stacks[i].Collectible == null)
            {
                _stacks.RemoveAt(i);
                i--;
                continue;
            }

            for (var j = 0; j < i; j++)
            {
                if (!StorageAccess.Same(world, _stacks[j], _stacks[i]))
                    continue;

                var sum = (long)_stacks[j].StackSize + _stacks[i].StackSize;
                _stacks[j].StackSize = (int)Math.Min(sum, int.MaxValue);
                _stacks.RemoveAt(i);
                i--;
                break;
            }
        }
    }
}

/// <summary>
/// Прямой ряд процессоров — один ЦПУ. Угол и незаконченная фигура остаются отдельными.
/// </summary>
public static class ProcessorCluster
{
    public static bool Busy;

    public static bool SamePos(BlockPos a, BlockPos b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.dimension == b.dimension;

    public static bool IsProcessor(Vintagestory.API.Common.Block? block)
        => block?.FirstCodePart() == "estorageprocessor";

    public static int BytesOf(Vintagestory.API.Common.Block? block)
    {
        var attrs = block?.Attributes;
        if (attrs == null)
            return 1024;

        var bytes = attrs["bytes"].AsInt(1024);
        return bytes > 0 ? bytes : 1024;
    }

    public static bool IsAnchor(IWorldAccessor world, BlockPos pos)
    {
        var view = At(world, pos);
        return !view.Merged || SamePos(view.Anchor, pos);
    }

    public static BlockEntityEStorageProcessor? Entity(IWorldAccessor world, BlockPos pos)
        => world.BlockAccessor.GetBlockEntity(pos) as BlockEntityEStorageProcessor;

    public static ClusterView At(IWorldAccessor world, BlockPos start)
    {
        var (members, incomplete) = Flood(world, start);
        var claims = 0;
        long sum = 0;
        foreach (var member in members)
        {
            var block = world.BlockAccessor.GetBlock(member);
            sum += BytesOf(block);
            if (world.BlockAccessor.GetBlockEntity(member) is BlockEntityEStorageProcessor { HasClaim: true })
                claims++;
        }

        var cap = (int)Math.Min(Math.Max(sum, 1024), int.MaxValue / StorageAccess.ItemsPerByte);
        var merged = !incomplete && claims <= 1 && Filled(members);
        if (!merged)
        {
            return new ClusterView
            {
                Anchor = start.Copy(),
                Members = new List<BlockPos> { start.Copy() },
                Bytes = BytesOf(world.BlockAccessor.GetBlock(start)),
                Merged = false,
                Incomplete = incomplete
            };
        }

        BlockPos? anchor = null;
        foreach (var member in members)
        {
            if (anchor == null || StorageScan.Earlier(member, anchor))
                anchor = member;
        }

        return new ClusterView
        {
            Anchor = anchor!.Copy(),
            Members = members,
            Bytes = cap,
            Merged = true,
            Incomplete = false
        };
    }

    public static bool CanHold(IWorldAccessor world, int bytes, IReadOnlyList<ItemStack> have, List<(ItemStack Proto, long Extra)> extra)
    {
        long items = 0;
        var types = 0;
        foreach (var stack in have)
        {
            if (stack.StackSize <= 0 || stack.Collectible == null)
                continue;
            items += stack.StackSize;
            types++;
        }

        foreach (var (proto, add) in extra)
        {
            if (add <= 0 || proto.Collectible == null)
                continue;

            var known = false;
            foreach (var stack in have)
            {
                if (stack.StackSize > 0 && StorageAccess.Same(world, stack, proto))
                {
                    known = true;
                    break;
                }
            }

            if (!known)
                types++;
            items += add;
        }

        if (types > StorageAccess.MaxTypes || items > int.MaxValue)
            return false;
        if (types == 0)
            return true;
        return StorageAccess.Fits(bytes, types, (int)items);
    }

    private static (List<BlockPos> Members, bool Incomplete) Flood(IWorldAccessor world, BlockPos start)
    {
        var members = new List<BlockPos>();
        var seen = new HashSet<string>();
        var queue = new Queue<BlockPos>();
        queue.Enqueue(start.Copy());
        var incomplete = false;

        while (queue.Count > 0)
        {
            var pos = queue.Dequeue();
            var key = pos.X + "," + pos.Y + "," + pos.Z + "," + pos.dimension;
            if (!seen.Add(key))
                continue;
            if (world.BlockAccessor.GetChunkAtBlockPos(pos) == null)
            {
                incomplete = true;
                continue;
            }

            if (!IsProcessor(world.BlockAccessor.GetBlock(pos)))
                continue;

            if (members.Count >= 256)
            {
                incomplete = true;
                break;
            }

            members.Add(pos.Copy());
            foreach (var face in BlockFacing.ALLFACES)
            {
                var next = pos.AddCopy(face);
                if (world.BlockAccessor.GetChunkAtBlockPos(next) == null)
                    continue;

                if (IsProcessor(world.BlockAccessor.GetBlock(next)))
                    queue.Enqueue(next);
            }
        }

        if (members.Count == 0)
            members.Add(start.Copy());
        return (members, incomplete);
    }

    private static bool Filled(List<BlockPos> members)
    {
        if (members.Count == 0)
            return false;

        var minX = members[0].X;
        var maxX = minX;
        var minY = members[0].Y;
        var maxY = minY;
        var minZ = members[0].Z;
        var maxZ = minZ;
        var dimension = members[0].dimension;
        foreach (var pos in members)
        {
            if (pos.dimension != dimension)
                return false;
            if (pos.X < minX) minX = pos.X;
            if (pos.X > maxX) maxX = pos.X;
            if (pos.Y < minY) minY = pos.Y;
            if (pos.Y > maxY) maxY = pos.Y;
            if (pos.Z < minZ) minZ = pos.Z;
            if (pos.Z > maxZ) maxZ = pos.Z;
        }

        var volume = (long)(maxX - minX + 1) * (maxY - minY + 1) * (maxZ - minZ + 1);
        return volume == members.Count;
    }
}

/// <summary>
/// Якорь ряда, его блоки, байты и собран ли ряд в один ЦПУ.
/// </summary>
public sealed class ClusterView
{
    public BlockPos Anchor = null!;
    public List<BlockPos> Members = new();
    public int Bytes;
    public bool Merged;
    public bool Incomplete;
}
