using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Storage;

public sealed class CraftOrder
{
    public BlockPos Panel = null!;
    public int Slot;
    public ItemStack Output = null!;
    public int Amount;
    public bool Bound;
}

public sealed class CraftPlanResult
{
    public bool NoCpu;
    public bool Failed;
    public string Text = "";
    public int Bytes;
    public byte[] Cells = [];
    public BlockPos? Share;
    public List<CraftOrder> Orders = new();
}

/// <summary>
/// Дерево заказа: что скрафтить, что взять из сети и каких процессоров не хватает.
/// </summary>
public static class CraftPlan
{
    private const int MaxDepth = 8;
    private const int MaxLines = 40;

    public static CraftPlanResult Build(IWorldAccessor world, BlockPos from, ItemStack proto, int count, IPlayer? player = null)
    {
        return new Builder(world, from, player).Run(proto, count);
    }

    public static bool Place(IWorldAccessor world, CraftPlanResult plan)
    {
        var done = new List<(CraftOrder Order, int Added)>();
        var share = plan.Share?.Copy();
        if (share == null)
        {
            foreach (var order in plan.Orders)
            {
                if (!order.Bound)
                    continue;
                if (world.BlockAccessor.GetBlockEntity(order.Panel) is not BlockEntityEStorageInterface bound)
                    continue;
                share = bound.CpuOf(order.Slot);
                if (share != null)
                    break;
            }
        }

        foreach (var order in plan.Orders)
        {
            if (world.BlockAccessor.GetBlockEntity(order.Panel) is not BlockEntityEStorageInterface panel)
            {
                Undo(world, done);
                return false;
            }

            var added = panel.TryOrderSlot(order.Slot, order.Output, order.Amount, share);
            if (added < 0)
            {
                Undo(world, done);
                return false;
            }

            done.Add((order, added));
            if (share == null)
                share = panel.CpuOf(order.Slot);
        }

        return true;
    }

    private static void Undo(IWorldAccessor world, List<(CraftOrder Order, int Added)> done)
    {
        for (var i = done.Count - 1; i >= 0; i--)
        {
            if (world.BlockAccessor.GetBlockEntity(done[i].Order.Panel) is BlockEntityEStorageInterface panel)
                panel.UndoOrder(done[i].Order.Slot, done[i].Added);
        }
    }

    private sealed class Builder
    {
        private readonly IWorldAccessor _world;
        private readonly BlockPos _from;
        private readonly IPlayer? _player;
        private readonly StorageScan _scan;
        private readonly List<string> _lines = new();
        private readonly List<CraftOrder> _orders = new();
        private readonly List<(ItemStack Proto, long Amount)> _held = new();
        private readonly List<(ItemStack Proto, long Amount)> _liquidItems = new();
        private readonly List<(ItemStack Proto, long Amount)> _spent = new();
        private readonly List<(ItemStack Proto, long Amount)> _missing = new();
        private readonly List<(ItemStack Stack, int Count, int Kind)> _cells = new();
        private readonly HashSet<string> _stack = new();
        private bool _trimmed;

        public Builder(IWorldAccessor world, BlockPos from, IPlayer? player)
        {
            _world = world;
            _from = from;
            _player = player;
            _scan = StorageAccess.GetScan(world, from);
        }

        public CraftPlanResult Run(ItemStack proto, int count)
        {
            var result = new CraftPlanResult();
            if (proto.Collectible == null && !proto.ResolveBlockOrItem(_world))
            {
                result.Failed = true;
                result.Text = Lang.Get("electricalprogressivestorage:estorage-order-none");
                return result;
            }

            count = Math.Clamp(count, 1, 100000);
            if (!Find(proto, out var panel, out var slot, out var pattern))
            {
                result.Failed = true;
                result.Text = Lang.Get("electricalprogressivestorage:estorage-order-none");
                return result;
            }

            Craft(panel, slot, pattern, proto, count, 0);

            var fresh = false;
            var bound = false;
            foreach (var order in _orders)
            {
                if (order.Bound)
                    bound = true;
                else
                    fresh = true;
            }

            result.Orders = _orders;
            result.Share = fresh && !bound ? PickFree() : null;
            result.NoCpu = fresh && !bound && result.Share == null;
            result.Bytes = PlanBytes();
            result.Cells = PackCells();
            result.Text = Finish();
            return result;
        }

        private void Craft(BlockEntityEStorageInterface panel, int slot, ItemStack pattern, ItemStack proto, int amount, int depth)
        {
            if (amount <= 0)
                return;

            var key = Key(panel.Pos, slot);
            if (_stack.Contains(key) || depth > MaxDepth)
            {
                Short(proto, amount, depth);
                return;
            }

            _stack.Add(key);
            Remember(proto, amount, 1);

            Line(depth, Lang.Get("electricalprogressivestorage:estorage-order-item", Name(proto), Qty(amount)));

            var per = ItemEStoragePattern.PerCraft(_world, pattern);
            var crafts = (amount + (long)per - 1) / per;
            if (crafts < 1)
                crafts = 1;

            var liquids = LiquidCraft.Collect(_world, pattern, _player);
            for (var i = 0; i < 9; i++)
            {
                LiquidNeed? liquid = null;
                foreach (var entry in liquids)
                {
                    if (entry.Cell != i)
                        continue;
                    liquid = entry;
                    break;
                }

                if (liquid != null)
                {
                    PlanLiquid(liquid, crafts, depth + 1);
                    continue;
                }

                var input = ItemEStoragePattern.Cell(_world, pattern, i);
                if (input == null)
                    continue;

                var need = crafts * Math.Max(1, input.StackSize);
                if (need > int.MaxValue)
                    need = int.MaxValue;
                PlanInput(input, (int)need, depth + 1);
            }

            var order = _orders.Find(entry => ProcessorCluster.SamePos(entry.Panel, panel.Pos) && entry.Slot == slot);
            if (order == null)
            {
                var output = proto.Clone();
                output.StackSize = 1;
                _orders.Add(new CraftOrder
                {
                    Panel = panel.Pos.Copy(),
                    Slot = slot,
                    Output = output,
                    Amount = amount,
                    Bound = panel.OwnsJob(slot) && panel.CpuOf(slot) != null
                });
            }
            else
            {
                var sum = (long)order.Amount + amount;
                order.Amount = sum > 100000 ? 100000 : (int)sum;
                _orders.Remove(order);
                _orders.Add(order);
            }

            _stack.Remove(key);
        }

        private void PlanInput(ItemStack proto, int need, int depth)
        {
            if (need <= 0)
                return;

            Line(depth, Lang.Get("electricalprogressivestorage:estorage-order-item", Name(proto), Qty(need)));

            var have = StorageAccess.Count(_world, _from, proto) - Held(proto);
            if (have < 0)
                have = 0;
            var take = (int)Math.Min(have, need);
            if (take > 0)
            {
                Hold(proto, take);
                Add(_spent, proto, take);
                Remember(proto, take, 0);
                Line(depth + 1, Lang.Get("electricalprogressivestorage:estorage-order-stored", Qty(take)));
            }

            var left = need - take;
            if (left <= 0)
                return;

            if (left > 100000)
            {
                Short(proto, left - 100000, depth + 1);
                left = 100000;
            }

            if (Find(proto, out var panel, out var slot, out var pattern))
                Craft(panel, slot, pattern, proto, left, depth + 1);
            else
                Short(proto, left, depth + 1);
        }

        private void PlanLiquid(LiquidNeed need, long crafts, int depth)
        {
            if (crafts < 1)
                crafts = 1;

            var portions = (long)need.Portions * crafts;
            if (portions > int.MaxValue)
                portions = int.MaxValue;

            var have = StorageAccess.CountLiquid(_world, _from, need.Liquid) - Held(need.Liquid);
            if (have < 0)
                have = 0;
            var take = Math.Min(have, portions);
            if (take > 0)
            {
                Hold(need.Liquid, take);
                Add(_liquidItems, need.Liquid, take);
            }

            var totalMilli = (long)need.Milli * crafts;
            if (totalMilli > int.MaxValue)
                totalMilli = int.MaxValue;
            var haveMilli = portions <= 0 ? 0 : totalMilli * take / portions;
            var missMilli = totalMilli - haveMilli;
            if (missMilli < 0)
                missMilli = 0;

            if (haveMilli > 0)
            {
                Remember(need.Liquid, (int)haveMilli, 3);
                Line(depth, Lang.Get("electricalprogressivestorage:estorage-order-liquid-have", LiquidCraft.LitresText((int)haveMilli)));
            }

            if (missMilli > 0)
            {
                Remember(need.Liquid, (int)missMilli, 4);
                Line(depth, Lang.Get("electricalprogressivestorage:estorage-order-liquid-miss", LiquidCraft.LitresText((int)missMilli)));
            }
        }

        private void Short(ItemStack proto, int amount, int depth)
        {
            if (amount <= 0)
                return;

            Add(_missing, proto, amount);
            Remember(proto, amount, 2);
            Line(depth, Lang.Get("electricalprogressivestorage:estorage-order-short-line", Qty(amount)));
        }

        private void Remember(ItemStack proto, int count, int kind)
        {
            if (count <= 0)
                return;

            for (var i = 0; i < _cells.Count; i++)
            {
                if (_cells[i].Kind != kind || !StorageAccess.Same(_world, _cells[i].Stack, proto))
                    continue;

                var sum = (long)_cells[i].Count + count;
                _cells[i] = (_cells[i].Stack, sum > int.MaxValue ? int.MaxValue : (int)sum, kind);
                return;
            }

            if (_cells.Count >= 60)
                return;

            var copy = proto.Clone();
            copy.StackSize = 1;
            _cells.Add((copy, count, kind));
        }

        private byte[] PackCells()
        {
            var tree = new TreeAttribute();
            tree.SetInt("n", _cells.Count);
            for (var i = 0; i < _cells.Count; i++)
            {
                tree["s" + i] = new ItemstackAttribute(_cells[i].Stack);
                tree.SetInt("c" + i, _cells[i].Count);
                tree.SetInt("k" + i, _cells[i].Kind);
            }

            return tree.ToBytes();
        }

        private int PlanBytes()
        {
            var seen = new List<ItemStack>();
            long items = 0;
            foreach (var cell in _cells)
            {
                if (cell.Kind != 0 && cell.Kind != 1)
                    continue;

                items += cell.Count;
                var known = false;
                foreach (var have in seen)
                {
                    if (!StorageAccess.Same(_world, have, cell.Stack))
                        continue;
                    known = true;
                    break;
                }

                if (!known)
                    seen.Add(cell.Stack);
            }

            foreach (var (proto, amount) in _liquidItems)
            {
                if (amount <= 0)
                    continue;
                items += amount;
                var known = false;
                foreach (var have in seen)
                {
                    if (!StorageAccess.Same(_world, have, proto))
                        continue;
                    known = true;
                    break;
                }

                if (!known)
                    seen.Add(proto);
            }

            if (seen.Count == 0 || items <= 0)
                return 0;

            var cap = SmallestFreeBytes();
            if (cap <= 0)
                cap = 1024;
            var count = items > int.MaxValue ? int.MaxValue : (int)items;
            return StorageAccess.UsedBytes(cap, seen.Count, count);
        }

        private int SmallestFreeBytes()
        {
            var best = 0;
            var seen = new HashSet<string>();
            foreach (var pos in _scan.Processors)
            {
                var view = ProcessorCluster.At(_world, pos);
                if (view.Merged && !ProcessorCluster.SamePos(view.Anchor, pos))
                    continue;

                var key = pos.X + "," + pos.Y + "," + pos.Z + "," + pos.dimension;
                if (!seen.Add(key))
                    continue;
                if (_world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageProcessor processor || processor.HasClaim)
                    continue;
                if (best == 0 || view.Bytes < best)
                    best = view.Bytes;
            }

            return best;
        }

        private bool Find(ItemStack proto, out BlockEntityEStorageInterface panel, out int slot, out ItemStack pattern)
        {
            panel = null!;
            slot = -1;
            pattern = null!;

            foreach (var order in _orders)
            {
                if (_stack.Contains(Key(order.Panel, order.Slot)))
                    continue;
                if (!StorageAccess.Same(_world, order.Output, proto))
                    continue;
                if (_world.BlockAccessor.GetBlockEntity(order.Panel) is not BlockEntityEStorageInterface have)
                    continue;

                var held = have.Inventory[order.Slot].Itemstack;
                if (!ItemEStoragePattern.IsEncoded(held))
                    continue;

                panel = have;
                slot = order.Slot;
                pattern = held!;
                return true;
            }

            foreach (var pos in _scan.Interfaces)
            {
                if (_world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageInterface have)
                    continue;

                for (var i = ItemSlotInterface.PatternAt; i < ItemSlotInterface.ConfigAt; i++)
                {
                    if (_stack.Contains(Key(have.Pos, i)))
                        continue;

                    var held = have.Inventory[i].Itemstack;
                    if (!ItemEStoragePattern.IsEncoded(held))
                        continue;

                    var output = ItemEStoragePattern.Primary(_world, held!);
                    if (output == null || !StorageAccess.Same(_world, output, proto))
                        continue;

                    panel = have;
                    slot = i;
                    pattern = held!;
                    return true;
                }
            }

            return false;
        }

        private BlockPos? PickFree()
        {
            var extra = new List<(ItemStack Proto, long Extra)>();
            foreach (var cell in _cells)
            {
                if ((cell.Kind != 0 && cell.Kind != 1) || cell.Count <= 0)
                    continue;
                extra.Add((cell.Stack, cell.Count));
            }

            foreach (var (proto, amount) in _liquidItems)
            {
                if (amount > 0)
                    extra.Add((proto, amount));
            }

            BlockEntityEStorageProcessor? fit = null;
            var fitBytes = 0;
            BlockEntityEStorageProcessor? roomy = null;
            var roomyBytes = -1;
            var seen = new HashSet<string>();
            foreach (var pos in _scan.Processors)
            {
                var view = ProcessorCluster.At(_world, pos);
                if (view.Merged && !ProcessorCluster.SamePos(view.Anchor, pos))
                    continue;

                var key = pos.X + "," + pos.Y + "," + pos.Z + "," + pos.dimension;
                if (!seen.Add(key))
                    continue;
                if (_world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageProcessor processor || processor.HasClaim)
                    continue;

                if (roomy == null || view.Bytes > roomyBytes || (view.Bytes == roomyBytes && StorageScan.Earlier(pos, roomy.Pos)))
                {
                    roomy = processor;
                    roomyBytes = view.Bytes;
                }

                if (!ProcessorCluster.CanHold(_world, view.Bytes, processor.Contents, extra))
                    continue;
                if (fit != null && (view.Bytes > fitBytes || (view.Bytes == fitBytes && !StorageScan.Earlier(pos, fit.Pos))))
                    continue;

                fit = processor;
                fitBytes = view.Bytes;
            }

            return (fit ?? roomy)?.Pos.Copy();
        }

        private long Held(ItemStack proto)
        {
            long have = 0;
            foreach (var entry in _held)
            {
                if (StorageAccess.Same(_world, entry.Proto, proto))
                    have += entry.Amount;
            }

            return have;
        }

        private void Hold(ItemStack proto, long amount) => Add(_held, proto, amount);

        private void Add(List<(ItemStack Proto, long Amount)> list, ItemStack proto, long amount)
        {
            if (amount <= 0)
                return;

            for (var i = 0; i < list.Count; i++)
            {
                if (!StorageAccess.Same(_world, list[i].Proto, proto))
                    continue;

                list[i] = (list[i].Proto, list[i].Amount + amount);
                return;
            }

            var copy = proto.Clone();
            copy.StackSize = 1;
            list.Add((copy, amount));
        }

        private string Finish()
        {
            var text = new StringBuilder();
            foreach (var line in _lines)
            {
                if (text.Length > 0)
                    text.Append('\n');
                text.Append(line);
            }

            Append(text, "electricalprogressivestorage:estorage-order-from", _spent);
            Append(text, "electricalprogressivestorage:estorage-order-short", _missing);
            return text.ToString();
        }

        private void Append(StringBuilder text, string title, List<(ItemStack Proto, long Amount)> rows)
        {
            if (rows.Count == 0)
                return;

            if (text.Length > 0)
                text.Append("\n\n");
            text.Append(Lang.Get(title));
            foreach (var (proto, amount) in rows)
            {
                text.Append('\n');
                text.Append(Lang.Get("electricalprogressivestorage:estorage-order-item", Name(proto), Qty(amount)));
            }
        }

        private void Line(int depth, string text)
        {
            if (_trimmed)
                return;
            if (_lines.Count >= MaxLines)
            {
                _trimmed = true;
                _lines.Add("…");
                return;
            }

            var prefix = depth <= 0 ? "" : new string('·', Math.Min(depth, 8)) + " ";
            _lines.Add(prefix + text);
        }

        private string Name(ItemStack proto)
        {
            var copy = proto.Clone();
            copy.StackSize = 1;
            if (copy.Collectible == null)
                copy.ResolveBlockOrItem(_world);
            return copy.GetName();
        }

        private static string Key(BlockPos pos, int slot)
            => pos.X + "," + pos.Y + "," + pos.Z + "," + pos.dimension + "#" + slot;

        private static string Qty(long amount)
        {
            if (amount < 1000)
                return amount.ToString();
            if (amount > int.MaxValue)
                amount = int.MaxValue;
            return StackSizeTextPatch.Format((int)amount);
        }
    }
}
