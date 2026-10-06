using System;
using System.Collections.Generic;
using System.Globalization;
using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Storage;

public enum StorageLink
{
    Online,
    NoController,
    Conflict,
    NoPower,
    NoChannel
}

public sealed class StorageScan
{
    public List<BlockPos> Controllers { get; } = new();
    public List<BlockPos> Drives { get; } = new();
    public List<BlockPos> Terminals { get; } = new();
    public List<BlockPos> Interfaces { get; } = new();
    public List<BlockPos> Processors { get; } = new();

    // Соседние клетки, чанк которых ещё не загружен. Пока они пустые, обход сети неполный.
    public List<BlockPos> Pending { get; } = new();
    public HashSet<string> Active { get; } = new();
    public int ChannelSupply { get; set; }
    public int ChannelUsed { get; set; }
    public bool Conflict { get; set; }

    // Каналы, которые реально прошли через компонент кабеля. Ключ — клетка и маска компоненты.
    public Dictionary<string, int> CableUsed { get; } = new();

    public BlockPos? Controller
    {
        get
        {
            BlockPos? best = null;
            foreach (var pos in Controllers)
            {
                if (best == null || Earlier(pos, best))
                    best = pos;
            }

            return best;
        }
    }

    public static bool Earlier(BlockPos a, BlockPos b)
    {
        if (a.dimension != b.dimension)
            return a.dimension < b.dimension;
        if (a.X != b.X)
            return a.X < b.X;
        if (a.Y != b.Y)
            return a.Y < b.Y;
        return a.Z < b.Z;
    }

    public bool Serves(BlockPos pos)
        => Active.Contains(pos.X + "," + pos.Y + "," + pos.Z + "," + pos.dimension);
}

/// <summary>
/// Сеть хранения: кабель на грани и устройства, которых этот кабель касается.
/// Диск хранит типы как в Applied Energistics: 63 типа, байты = объём,
/// каждый тип занимает bytes/128, оставшиеся байты держат по 8 предметов.
/// Ячейка хранит до четырёх жидкостей отдельно от дисков: один байт — 8 литров на всех.
/// </summary>
public static class StorageAccess
{
    public const int MaxTypes = 63;
    public const int ItemsPerByte = 8;
    public const int LitresPerByte = 8;
    public const int MaxCellTypes = 4;
    public const int ViewSlots = 300;
    public const int LiquidPacketId = 19101;
    public const int OrderPacketId = 19103;
    public const string StacksKey = "diskStacks";
    public const string CellStacksKey = "cellStacks";
    public const string CraftGhostKey = "estorageCraft";
    public const string OrderKey = "estorageOrder";

    public static long ContentGeneration { get; private set; }

    private static readonly Dictionary<string, StorageScan> Cache = new();

    public static void DirtyTopology() => Cache.Clear();

    public static void TouchContents() => ContentGeneration++;

    public static bool IsPart(Vintagestory.API.Common.Block? block)
    {
        if (block?.Code == null || block.Code.Domain != "electricalprogressivestorage")
            return false;

        return block.FirstCodePart() is "estoragecable" or "estoragefiber"
            or "estoragedrive" or "estorageterminal" or "estorageliquidterminal"
            or "estoragepatternterminal" or "estorageinterface" or "estorageprocessor"
            or "estoragecontroller";
    }

    public static bool IsCable(Vintagestory.API.Common.Block? block)
        => block?.FirstCodePart() is "estoragecable" or "estoragefiber";

    // Сколько каналов привод или терминал пропускает дальше, сверх своего собственного.
    private const int DeviceChannels = 8;

    public static int CableChannels(Vintagestory.API.Common.Block block)
    {
        var fallback = block.FirstCodePart() == "estoragefiber" ? 256 : 8;
        return block.Attributes?["channels"].AsInt(fallback) ?? fallback;
    }

    public static int CableChannels(IWorldAccessor world, BlockPos pos, Vintagestory.API.Common.Block block, Facing mask)
    {
        var per = CableChannels(block);
        if (block.FirstCodePart() == "estoragefiber" || mask == Facing.None)
            return per;
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageCable entity)
            return per;
        return per * entity.LinesOf(mask);
    }

    /// <summary>
    /// По каждому проводу в клетке: сколько каналов занято и сколько провод пропускает.
    /// На углу берётся более тонкая грань.
    /// </summary>
    public static List<(int Used, int Capacity)> CableLoads(IWorldAccessor world, BlockPos pos)
    {
        var loads = new List<(int Used, int Capacity)>();
        if (world.BlockAccessor.GetBlock(pos) is not Vintagestory.API.Common.Block block || !IsCable(block))
            return loads;
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageCable entity || entity.Connection == Facing.None)
            return loads;

        var scan = GetScan(world, pos);
        foreach (var component in Components(entity.Connection))
        {
            var capacity = CableChannels(world, pos, block, component);
            scan.CableUsed.TryGetValue(Key(pos) + ":" + (int)component, out var used);
            if (used < 0)
                used = 0;
            if (capacity < 0)
                capacity = 0;
            if (used > capacity)
                used = capacity;
            loads.Add((used, capacity));
        }

        return loads;
    }

    private static void AddCableUsed(StorageScan scan, BlockPos pos, Facing mask, int used)
    {
        if (used <= 0 || mask == Facing.None)
            return;

        var key = Key(pos) + ":" + (int)mask;
        scan.CableUsed.TryGetValue(key, out var have);
        scan.CableUsed[key] = have + used;
    }

    private readonly struct NetNode
    {
        public readonly BlockPos Pos;
        public readonly Facing Mask;

        public NetNode(BlockPos pos, Facing mask)
        {
            Pos = pos;
            Mask = mask;
        }
    }

    private static readonly (BlockFacing Face, Facing Mask)[] FaceMasks =
    [
        (BlockFacing.NORTH, Facing.NorthAll),
        (BlockFacing.EAST, Facing.EastAll),
        (BlockFacing.SOUTH, Facing.SouthAll),
        (BlockFacing.WEST, Facing.WestAll),
        (BlockFacing.UP, Facing.UpAll),
        (BlockFacing.DOWN, Facing.DownAll)
    ];

    private static void Seed(IWorldAccessor world, BlockPos start, Queue<NetNode> queue)
    {
        if (world.BlockAccessor.GetChunkAtBlockPos(start) != null && TryFaced(world, start, out var connection))
        {
            foreach (var mask in Components(connection))
                queue.Enqueue(new NetNode(start.Copy(), mask));
            return;
        }

        queue.Enqueue(new NetNode(start.Copy(), Facing.None));
    }

    private static bool TryFaced(IWorldAccessor world, BlockPos pos, out Facing connection)
    {
        connection = Facing.None;
        if (world.BlockAccessor.GetBlock(pos) is not BlockEStorageCable)
            return false;
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageCable entity || entity.Connection == Facing.None)
            return false;
        connection = entity.Connection;
        return true;
    }

    private static bool CableTouches(Facing connection, BlockFacing toward)
        => (connection & FacingHelper.FromFace(toward)) != 0
           || (connection & FacingHelper.FromDirection(toward)) != 0;

    private static bool SamePlace(BlockPos a, BlockPos b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.dimension == b.dimension;

    // Отрезки одной грани — один провод. Грани смыкаются, если на углу есть оба встречных отрезка.
    private static List<Facing> Components(Facing connection)
    {
        var present = new List<BlockFacing>();
        foreach (var (face, mask) in FaceMasks)
        {
            if ((connection & mask) != 0)
                present.Add(face);
        }

        var used = new bool[present.Count];
        var result = new List<Facing>();
        for (var i = 0; i < present.Count; i++)
        {
            if (used[i])
                continue;

            var stack = new Stack<int>();
            stack.Push(i);
            used[i] = true;
            var mask = Facing.None;
            while (stack.Count > 0)
            {
                var index = stack.Pop();
                var face = present[index];
                mask |= connection & FacingHelper.FromFace(face);
                for (var j = 0; j < present.Count; j++)
                {
                    if (used[j])
                        continue;
                    var other = present[j];
                    if ((connection & FacingHelper.From(face, other)) != 0
                        && (connection & FacingHelper.From(other, face)) != 0)
                    {
                        used[j] = true;
                        stack.Push(j);
                    }
                }
            }

            result.Add(mask);
        }

        return result;
    }

    private static List<NetNode> CollectLinks(IWorldAccessor world, BlockPos pos, Facing mask)
    {
        var list = new List<NetNode>();
        var seen = new HashSet<string>();

        void Add(BlockPos next, Facing nextMask)
        {
            if (seen.Add(Key(next) + "/" + (int)nextMask))
                list.Add(new NetNode(next, nextMask));
        }

        void Consider(BlockPos next, Facing any)
        {
            if (!TryFaced(world, next, out var connection))
                return;
            var hit = connection & any;
            if (hit == Facing.None)
                return;
            foreach (var component in Components(connection))
            {
                if ((component & hit) == 0)
                    continue;
                Add(next, component);
                return;
            }
        }

        if (mask != Facing.None)
        {
            foreach (var face in BlockFacing.ALLFACES)
            {
                if (!CableTouches(mask, face))
                    continue;
                var next = pos.AddCopy(face);
                var block = world.BlockAccessor.GetBlock(next);
                if (!IsPart(block) || TryFaced(world, next, out _))
                    continue;
                Add(next, Facing.None);
            }

            foreach (var direction in FacingHelper.Directions(mask))
            {
                var filter = FacingHelper.FromDirection(direction);
                foreach (var face in FacingHelper.Faces(mask & filter))
                {
                    Consider(pos.AddCopy(direction),
                        FacingHelper.From(face, direction.Opposite) | FacingHelper.From(direction.Opposite, face));
                    Consider(pos.AddCopy(direction).AddCopy(face),
                        FacingHelper.From(direction.Opposite, face.Opposite) | FacingHelper.From(face.Opposite, direction.Opposite));
                    Consider(pos.AddCopy(face),
                        FacingHelper.From(direction, face.Opposite) | FacingHelper.From(face.Opposite, direction));
                }
            }

            return list;
        }

        foreach (var face in BlockFacing.ALLFACES)
        {
            var next = pos.AddCopy(face);
            var block = world.BlockAccessor.GetBlock(next);
            if (!IsPart(block))
                continue;
            if (TryFaced(world, next, out var connection))
            {
                foreach (var component in Components(connection))
                {
                    if (CableTouches(component, face.Opposite))
                        Add(next, component);
                }

                continue;
            }

            Add(next, Facing.None);
        }

        return list;
    }

    public const string PartitionKey = "diskPartition";
    public const string PriorityKey = "diskPriority";

    public static int DiskPriority(ItemStack disk) => disk.Attributes.GetInt(PriorityKey);

    public static bool DiskAccepts(IWorldAccessor world, ItemStack disk, ItemStack proto)
    {
        var tree = disk.Attributes.GetTreeAttribute(PartitionKey);
        if (tree == null)
            return true;

        var any = false;
        for (var i = 0; i < 9; i++)
        {
            var sample = tree.GetItemstack(i.ToString());
            if (sample == null)
                continue;
            any = true;
            sample.ResolveBlockOrItem(world);
            if (sample.Collectible != null && Same(world, sample, proto))
                return true;
        }

        return !any;
    }

    public static StorageScan GetScan(IWorldAccessor world, BlockPos start)
    {
        var key = world.Side + ":" + start.X + "," + start.Y + "," + start.Z + "," + start.dimension;
        if (Cache.TryGetValue(key, out var cached) && !PendingChunksLoaded(world, cached))
            return cached;

        var scan = new StorageScan();
        var seen = new HashSet<string>();
        var listed = new HashSet<string>();
        var queue = new Queue<NetNode>();
        Seed(world, start, queue);

        while (queue.Count > 0 && seen.Count < 8192)
        {
            var node = queue.Dequeue();
            var pos = node.Pos;
            if (!seen.Add(Key(pos) + "/" + (int)node.Mask))
                continue;

            // Незагруженный чанк нельзя считать воздухом: за ним может быть привод.
            if (world.BlockAccessor.GetChunkAtBlockPos(pos) == null)
            {
                scan.Pending.Add(pos.Copy());
                continue;
            }

            var block = world.BlockAccessor.GetBlock(pos);
            if (!IsPart(block))
                continue;

            if (!listed.Add(Key(pos)))
            {
                foreach (var link in CollectLinks(world, pos, node.Mask))
                    queue.Enqueue(link);
                continue;
            }

            switch (block.FirstCodePart())
            {
                case "estoragecontroller":
                    scan.Controllers.Add(pos.Copy());
                    break;
                case "estoragedrive":
                    scan.Drives.Add(pos.Copy());
                    break;
                case "estorageterminal":
                case "estorageliquidterminal":
                case "estoragepatternterminal":
                    scan.Terminals.Add(pos.Copy());
                    break;
                case "estorageinterface":
                    scan.Interfaces.Add(pos.Copy());
                    break;
                case "estorageprocessor":
                    scan.Processors.Add(pos.Copy());
                    break;
            }

            foreach (var link in CollectLinks(world, pos, node.Mask))
                queue.Enqueue(link);
        }

        AssignChannels(world, scan);
        Cache[key] = scan;
        return scan;
    }

    private static void AssignChannels(IWorldAccessor world, StorageScan scan)
    {
        if (scan.Controllers.Count == 0)
            return;

        var groups = ControllerGroups(scan.Controllers);
        if (groups.Count != 1)
        {
            scan.Conflict = true;
            KeepChanneled(scan.Drives, scan);
            KeepChanneled(scan.Terminals, scan);
            KeepChanneled(scan.Interfaces, scan);
            KeepChanneled(scan.Processors, scan);
            return;
        }

        var group = groups[0];
        var exits = new List<(BlockPos From, NetNode Next)>();
        var pool = 0;
        foreach (var controller in group)
        {
            scan.Active.Add(Key(controller));
            var faces = 0;
            foreach (var next in CollectLinks(world, controller, Facing.None))
            {
                var block = world.BlockAccessor.GetBlock(next.Pos);
                if (!IsPart(block) || block.FirstCodePart() == "estoragecontroller")
                    continue;
                faces++;
                exits.Add((controller, next));
            }

            pool += 32 * Math.Max(1, faces);
        }

        scan.ChannelSupply = pool;
        var source = Key(group[0]);
        var fedBy = new Dictionary<string, HashSet<string>>();
        foreach (var (from, next) in exits)
        {
            if (pool <= 0)
                break;
            var give = Math.Min(32, pool);
            pool -= Serve(world, next.Pos, next.Mask, give, from, Facing.None, source, fedBy, scan);
        }

        // Процессор в этой сети участвует в крафте, даже если на него не хватило канала.
        foreach (var pos in scan.Processors)
            scan.Active.Add(Key(pos));

        KeepChanneled(scan.Drives, scan);
        KeepChanneled(scan.Terminals, scan);
        KeepChanneled(scan.Interfaces, scan);
        KeepChanneled(scan.Processors, scan);
    }

    // Контроллеры гранью к грани — один мультиблок. Через кабель это уже второй контроллер.
    private static List<List<BlockPos>> ControllerGroups(List<BlockPos> controllers)
    {
        var left = new List<BlockPos>(controllers);
        var groups = new List<List<BlockPos>>();
        while (left.Count > 0)
        {
            var group = new List<BlockPos> { left[0] };
            left.RemoveAt(0);
            var grown = true;
            while (grown)
            {
                grown = false;
                for (var i = left.Count - 1; i >= 0; i--)
                {
                    if (!TouchesGroup(left[i], group))
                        continue;
                    group.Add(left[i]);
                    left.RemoveAt(i);
                    grown = true;
                }
            }

            groups.Add(group);
        }

        return groups;
    }

    private static bool TouchesGroup(BlockPos pos, List<BlockPos> group)
    {
        foreach (var have in group)
        {
            if (have.dimension != pos.dimension)
                continue;
            var apart = Math.Abs(have.X - pos.X) + Math.Abs(have.Y - pos.Y) + Math.Abs(have.Z - pos.Z);
            if (apart == 1)
                return true;
        }

        return false;
    }

    private static int Serve(IWorldAccessor world, BlockPos pos, Facing mask, int incoming, BlockPos from, Facing fromMask, string source, Dictionary<string, HashSet<string>> fedBy, StorageScan scan)
    {
        if (incoming <= 0)
            return 0;

        var block = world.BlockAccessor.GetBlock(pos);
        if (!IsPart(block) || block.FirstCodePart() == "estoragecontroller")
            return 0;

        var key = Key(pos);
        var cable = mask != Facing.None || IsCable(block);
        if (!cable)
        {
            // Чужой блок прямоугольника канал не ест и кабель не режет: пропускает входящие дальше.
            if (block.FirstCodePart() == "estorageprocessor" && !ProcessorCluster.IsAnchor(world, pos))
            {
                if (!scan.Active.Add(key))
                    return 0;
                return Forward(world, pos, mask, incoming, from, fromMask, source, fedBy, scan);
            }

            if (!scan.Active.Add(key))
                return 0;
            scan.ChannelUsed++;
            var through = Math.Min(Math.Max(0, incoming - 1), DeviceChannels);
            return 1 + Forward(world, pos, mask, through, from, fromMask, source, fedBy, scan);
        }

        var feedKey = mask == Facing.None ? key : key + ":" + (int)mask;
        if (!fedBy.TryGetValue(feedKey, out var sources))
            fedBy[feedKey] = sources = new HashSet<string>();
        if (!sources.Add(source))
            return 0;

        scan.Active.Add(key);
        var capacity = CableChannels(world, pos, block, mask);
        var left = Math.Min(incoming, capacity);
        var carried = Forward(world, pos, mask, left, from, fromMask, source, fedBy, scan);
        AddCableUsed(scan, pos, mask, carried);
        return carried;
    }

    private static int Forward(IWorldAccessor world, BlockPos pos, Facing mask, int budget, BlockPos from, Facing fromMask, string source, Dictionary<string, HashSet<string>> fedBy, StorageScan scan)
    {
        if (budget <= 0)
            return 0;

        var devices = new List<NetNode>();
        var cables = new List<NetNode>();
        foreach (var next in CollectLinks(world, pos, mask))
        {
            if (SamePlace(next.Pos, from) && next.Mask == fromMask)
                continue;
            var neighbor = world.BlockAccessor.GetBlock(next.Pos);
            if (!IsPart(neighbor) || neighbor.FirstCodePart() == "estoragecontroller")
                continue;
            if (next.Mask != Facing.None || IsCable(neighbor))
                cables.Add(next);
            else
                devices.Add(next);
        }

        var used = 0;
        foreach (var child in devices)
        {
            if (used >= budget)
                break;
            used += Serve(world, child.Pos, child.Mask, budget - used, pos, mask, source, fedBy, scan);
        }

        foreach (var child in cables)
        {
            if (used >= budget)
                break;
            used += Serve(world, child.Pos, child.Mask, budget - used, pos, mask, source, fedBy, scan);
        }

        return used;
    }

    private static void KeepChanneled(List<BlockPos> list, StorageScan scan)
    {
        list.RemoveAll(pos => !scan.Serves(pos));
    }

    private static string Key(BlockPos pos)
        => pos.X + "," + pos.Y + "," + pos.Z + "," + pos.dimension;

    private static bool PendingChunksLoaded(IWorldAccessor world, StorageScan scan)
    {
        foreach (var pos in scan.Pending)
        {
            if (world.BlockAccessor.GetChunkAtBlockPos(pos) != null)
                return true;
        }

        return false;
    }

    public static BEBehaviorEStorageController? ControllerOf(IWorldAccessor world, BlockPos from)
    {
        var controller = GetScan(world, from).Controller;
        if (controller == null)
            return null;

        return world.BlockAccessor.GetBlockEntity(controller)?.GetBehavior<BEBehaviorEStorageController>();
    }

    public static StorageLink Link(IWorldAccessor world, BlockPos from)
    {
        var scan = GetScan(world, from);
        if (scan.Controllers.Count == 0)
            return StorageLink.NoController;
        if (scan.Conflict)
            return StorageLink.Conflict;
        if (!HasPower(world, scan))
            return StorageLink.NoPower;
        return scan.Serves(from) ? StorageLink.Online : StorageLink.NoChannel;
    }

    public static bool HasPower(IWorldAccessor world, StorageScan scan)
    {
        foreach (var pos in scan.Controllers)
        {
            if (world.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorEStorageController>() is { Powered: true })
                return true;
        }

        return false;
    }

    public static float Demand(IWorldAccessor world, StorageScan scan)
    {
        var disks = 0;
        foreach (var pos in scan.Drives)
            disks += CountDisks(world, pos);

        return 8f + scan.Drives.Count * 10f + (scan.Terminals.Count + scan.Interfaces.Count) * 6f
            + scan.Processors.Count * 4f + AssemblerWatts(world, scan) + disks * 2f;
    }

    private static float AssemblerWatts(IWorldAccessor world, StorageScan scan)
    {
        var seen = new HashSet<string>();
        float watts = 0f;
        foreach (var pos in scan.Interfaces)
        {
            if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityEStorageInterface panel)
                continue;

            var mount = panel.MountedOn();
            if (mount == null || world.BlockAccessor.GetBlock(mount).FirstCodePart() != "estorageassembler")
                continue;

            if (!seen.Add(mount.X + "," + mount.Y + "," + mount.Z + "," + mount.dimension))
                continue;

            watts += 8f;
            if (world.BlockAccessor.GetBlockEntity(mount) is BlockEntityEStorageAssembler { Working: true })
                watts += BlockEntityEStorageAssembler.WorkWatts;
        }

        return watts;
    }

    public static int CountDisks(IWorldAccessor world, BlockPos drivePos)
    {
        InventoryBase? inventory = world.BlockAccessor.GetBlockEntity(drivePos) is BlockEntityEStorageDrive drive
            ? drive.Inventory
            : null;
        if (inventory == null)
            return 0;

        var count = 0;
        for (var i = 0; i < inventory.Count; i++)
        {
            if (inventory[i].Itemstack?.Item is ItemEStorageDisk or ItemEStorageCell)
                count++;
        }

        return count;
    }

    public static bool Same(IWorldAccessor world, ItemStack a, ItemStack b)
    {
        if (a.Collectible == null || b.Collectible == null)
            return false;

        return a.Equals(world, b, GlobalConstants.IgnoredStackAttributes);
    }

    public static int Count(IWorldAccessor world, BlockPos from, ItemStack proto)
    {
        if (proto.Collectible == null || Link(world, from) != StorageLink.Online)
            return 0;

        var count = 0;
        foreach (var disk in EnumerateDisks(world, from))
        {
            foreach (var entry in ReadDisk(world, disk.Stack))
            {
                if (Same(world, entry, proto))
                    count += entry.StackSize;
            }
        }

        return count;
    }

    public static int BytesOf(ItemStack disk)
    {
        var attrs = disk.Collectible?.Attributes;
        if (attrs == null)
            return 1024;

        return attrs["bytes"].AsInt(1024);
    }

    public static int BytesPerType(int bytes) => Math.Max(1, bytes / 128);

    public static int UsedBytes(int bytes, int types, int items)
    {
        if (types <= 0 || items <= 0)
            return 0;

        return types * BytesPerType(bytes) + (items + ItemsPerByte - 1) / ItemsPerByte;
    }

    public static bool Fits(int bytes, int types, int items)
    {
        if (types == 0)
            return items == 0;
        if (types > MaxTypes || items < 0)
            return false;

        return UsedBytes(bytes, types, items) <= bytes;
    }

    public static int RoomForAdditional(int bytes, int types, int items, bool newType)
    {
        var nextTypes = types + (newType ? 1 : 0);
        if (nextTypes <= 0 || nextTypes > MaxTypes || !Fits(bytes, types == 0 ? 0 : types, items))
            return 0;
        if (types == 0 && items == 0)
            nextTypes = 1;

        if (!Fits(bytes, nextTypes, items) && !(types == 0 && items == 0))
            return 0;

        var lo = 0;
        var hi = Math.Max(0, bytes * ItemsPerByte);
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (Fits(bytes, nextTypes, items + mid))
                lo = mid;
            else
                hi = mid - 1;
        }

        return lo;
    }

    public static List<ItemStack> ReadDisk(IWorldAccessor world, ItemStack disk)
        => ReadKeyed(world, disk, StacksKey);

    public static void WriteDisk(ItemStack disk, List<ItemStack> stacks)
        => WriteKeyed(disk, StacksKey, stacks);

    public static List<ItemStack> ReadCell(IWorldAccessor world, ItemStack cell)
        => ReadKeyed(world, cell, CellStacksKey);

    public static void WriteCell(ItemStack cell, List<ItemStack> stacks)
        => WriteKeyed(cell, CellStacksKey, stacks);

    private static List<ItemStack> ReadKeyed(IWorldAccessor world, ItemStack host, string key)
    {
        var list = new List<ItemStack>();
        var tree = host.Attributes?.GetTreeAttribute(key);
        if (tree == null)
            return list;

        var names = new List<string>();
        foreach (var kv in tree)
            names.Add(kv.Key);
        names.Sort(StringComparer.Ordinal);

        foreach (var name in names)
        {
            if (tree[name] is not ItemstackAttribute { value: { StackSize: > 0 } stack })
                continue;

            // Вложенный стак после загрузки мира хранит только id. Collectible пустой, пока его не разрешить.
            if (stack.Collectible == null && !stack.ResolveBlockOrItem(world))
                continue;
            if (stack.Collectible == null)
                continue;

            list.Add(stack.Clone());
        }

        return list;
    }

    private static void WriteKeyed(ItemStack host, string key, List<ItemStack> stacks)
    {
        host.Attributes ??= new TreeAttribute();
        var kept = new List<ItemStack>();
        foreach (var stack in stacks)
        {
            if (stack is { StackSize: > 0 })
                kept.Add(stack);
        }

        if (kept.Count == 0)
        {
            host.Attributes.RemoveAttribute(key);
            return;
        }

        var tree = new TreeAttribute();
        for (var i = 0; i < kept.Count; i++)
            tree["s" + i.ToString("D3")] = new ItemstackAttribute(kept[i].Clone());
        host.Attributes[key] = tree;
    }

    public static int RoomOnDisk(IWorldAccessor world, ItemStack disk, ItemStack proto)
    {
        if (disk.Item is not ItemEStorageDisk || proto.Item is ItemEStorageDisk or ItemEStorageCell || !DiskAccepts(world, disk, proto))
            return 0;

        var bytes = BytesOf(disk);
        var entries = ReadDisk(world, disk);
        var items = 0;
        var has = false;
        foreach (var entry in entries)
        {
            items += entry.StackSize;
            if (Same(world, entry, proto))
                has = true;
        }

        return RoomForAdditional(bytes, entries.Count, items, !has);
    }

    public static int RoomFor(IWorldAccessor world, BlockPos from, ItemStack proto)
    {
        if (Link(world, from) != StorageLink.Online || proto.Collectible == null)
            return 0;

        var room = 0;
        foreach (var disk in EnumerateDisks(world, from))
            room += RoomOnDisk(world, disk.Stack, proto);
        return room;
    }

    public static int Insert(IWorldAccessor world, BlockPos from, ItemStack proto, int count)
    {
        if (count <= 0 || Link(world, from) != StorageLink.Online || proto.Collectible == null)
            return 0;

        var left = count;
        foreach (var preferExisting in new[] { true, false })
        {
            foreach (var disk in EnumerateDisks(world, from))
            {
                if (left <= 0)
                    break;

                var entries = ReadDisk(world, disk.Stack);
                var has = false;
                foreach (var entry in entries)
                {
                    if (Same(world, entry, proto))
                    {
                        has = true;
                        break;
                    }
                }

                if (has != preferExisting)
                    continue;

                var room = RoomOnDisk(world, disk.Stack, proto);
                var add = Math.Min(room, left);
                if (add <= 0)
                    continue;

                if (has)
                {
                    foreach (var entry in entries)
                    {
                        if (!Same(world, entry, proto))
                            continue;
                        entry.StackSize += add;
                        break;
                    }
                }
                else
                {
                    var created = proto.Clone();
                    created.StackSize = add;
                    entries.Add(created);
                }

                WriteDisk(disk.Stack, entries);
                disk.Drive.MarkDirty();
                left -= add;
            }
        }

        if (left != count)
            TouchContents();
        return count - left;
    }

    public static ItemStack? Extract(IWorldAccessor world, BlockPos from, ItemStack proto, int count)
    {
        if (count <= 0 || Link(world, from) != StorageLink.Online || proto.Collectible == null)
            return null;

        var left = count;
        var taken = proto.Clone();
        taken.StackSize = 0;

        foreach (var disk in EnumerateDisks(world, from))
        {
            if (left <= 0)
                break;

            var entries = ReadDisk(world, disk.Stack);
            var changed = false;
            foreach (var entry in entries)
            {
                if (left <= 0 || !Same(world, entry, proto))
                    continue;

                var move = Math.Min(entry.StackSize, left);
                entry.StackSize -= move;
                taken.StackSize += move;
                left -= move;
                changed = true;
            }

            if (!changed)
                continue;

            WriteDisk(disk.Stack, entries);
            disk.Drive.MarkDirty();
        }

        if (taken.StackSize <= 0)
            return null;

        TouchContents();
        return taken;
    }

    public static List<ItemStack> Aggregate(IWorldAccessor world, BlockPos from)
    {
        var result = new List<ItemStack>();
        if (Link(world, from) != StorageLink.Online)
            return result;

        foreach (var disk in EnumerateDisks(world, from))
        {
            foreach (var entry in ReadDisk(world, disk.Stack))
            {
                var found = false;
                foreach (var have in result)
                {
                    if (!Same(world, have, entry))
                        continue;
                    have.StackSize += entry.StackSize;
                    found = true;
                    break;
                }

                if (!found)
                    result.Add(entry.Clone());
            }
        }

        result.Sort((a, b) => string.Compare(SortName(a), SortName(b), StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private static string SortName(ItemStack stack)
        => stack.Collectible == null ? "" : stack.GetName() ?? "";

    public static void DescribeDisk(ItemStack disk, IWorldAccessor world, System.Text.StringBuilder dsc)
    {
        var bytes = BytesOf(disk);
        var entries = ReadDisk(world, disk);
        var items = 0;
        foreach (var entry in entries)
            items += entry.StackSize;

        dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-disk-types", entries.Count, MaxTypes));
        dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-disk-bytes", UsedBytes(bytes, entries.Count, items), bytes));
        dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-disk-items", items));
    }

    /// <summary>
    /// Верхняя оценка сети: байты диска × 8 предметов и 63 типа на диск.
    /// Типы тоже едят байты, поэтому предметов на практике меньше.
    /// </summary>
    public static void DiskBudgets(IWorldAccessor world, BlockPos from, out long itemCap, out int typeCap)
    {
        itemCap = 0;
        typeCap = 0;
        foreach (var disk in EnumerateDisks(world, from))
        {
            itemCap += (long)BytesOf(disk.Stack) * ItemsPerByte;
            typeCap += MaxTypes;
        }
    }

    public static WaterTightContainableProps? LiquidProps(ItemStack? stack)
    {
        if (stack == null)
            return null;

        var props = BlockLiquidContainerBase.GetContainableProps(stack);
        if (props == null || props.ItemsPerLitre <= 0)
            return null;

        return props;
    }

    public static BlockLiquidContainerBase? ContainerOf(ItemStack? stack)
        => stack?.Collectible as BlockLiquidContainerBase;

    public static float StackLitres(ItemStack stack)
    {
        var props = LiquidProps(stack);
        if (props == null)
            return 0;

        return stack.StackSize / props.ItemsPerLitre;
    }

    public static int CapacityLitres(ItemStack cell) => BytesOf(cell) * LitresPerByte;

    public static int RoomOnCell(IWorldAccessor world, ItemStack cell, ItemStack proto)
    {
        var props = LiquidProps(proto);
        if (cell.Item is not ItemEStorageCell || props == null)
            return 0;

        var have = 0f;
        var types = 0;
        var same = false;
        foreach (var entry in ReadCell(world, cell))
        {
            if (entry.StackSize <= 0)
                continue;

            types++;
            have += StackLitres(entry);
            if (Same(world, entry, proto))
                same = true;
        }

        if (!same && types >= MaxCellTypes)
            return 0;

        var roomLitres = CapacityLitres(cell) - have;
        if (roomLitres <= 0)
            return 0;

        return (int)Math.Floor(roomLitres * props.ItemsPerLitre + 1e-3);
    }

    public static int RoomForLiquid(IWorldAccessor world, BlockPos from, ItemStack proto)
    {
        if (Link(world, from) != StorageLink.Online || LiquidProps(proto) == null)
            return 0;

        long room = 0;
        foreach (var cell in EnumerateCells(world, from))
        {
            room += RoomOnCell(world, cell.Stack, proto);
            if (room >= int.MaxValue)
                return int.MaxValue;
        }

        return (int)room;
    }

    public static int InsertLiquid(IWorldAccessor world, BlockPos from, ItemStack proto, int count)
    {
        if (count <= 0 || Link(world, from) != StorageLink.Online || LiquidProps(proto) == null)
            return 0;

        var left = count;
        foreach (var preferExisting in new[] { true, false })
        {
            foreach (var cell in EnumerateCells(world, from))
            {
                if (left <= 0)
                    break;

                var entries = ReadCell(world, cell.Stack);
                var has = false;
                foreach (var entry in entries)
                {
                    if (Same(world, entry, proto))
                    {
                        has = true;
                        break;
                    }
                }

                if (has != preferExisting)
                    continue;

                var room = RoomOnCell(world, cell.Stack, proto);
                var add = Math.Min(room, left);
                if (add <= 0)
                    continue;

                if (has)
                {
                    foreach (var entry in entries)
                    {
                        if (!Same(world, entry, proto))
                            continue;
                        entry.StackSize += add;
                        break;
                    }
                }
                else
                {
                    var created = proto.Clone();
                    created.StackSize = add;
                    entries.Add(created);
                }

                WriteCell(cell.Stack, entries);
                cell.Drive.MarkDirty();
                left -= add;
            }
        }

        if (left != count)
            TouchContents();
        return count - left;
    }

    public static int CountLiquid(IWorldAccessor world, BlockPos from, ItemStack proto)
    {
        if (Link(world, from) != StorageLink.Online || LiquidProps(proto) == null)
            return 0;

        long have = 0;
        foreach (var cell in EnumerateCells(world, from))
        {
            foreach (var entry in ReadCell(world, cell.Stack))
            {
                if (entry.StackSize <= 0 || !Same(world, entry, proto))
                    continue;
                have += entry.StackSize;
                if (have >= int.MaxValue)
                    return int.MaxValue;
            }
        }

        return (int)have;
    }

    public static ItemStack? ExtractLiquid(IWorldAccessor world, BlockPos from, ItemStack proto, int count)
    {
        if (count <= 0 || Link(world, from) != StorageLink.Online || LiquidProps(proto) == null)
            return null;

        var left = count;
        var taken = proto.Clone();
        taken.StackSize = 0;

        foreach (var cell in EnumerateCells(world, from))
        {
            if (left <= 0)
                break;

            var entries = ReadCell(world, cell.Stack);
            var changed = false;
            foreach (var entry in entries)
            {
                if (left <= 0 || !Same(world, entry, proto))
                    continue;

                var move = Math.Min(entry.StackSize, left);
                entry.StackSize -= move;
                taken.StackSize += move;
                left -= move;
                changed = true;
            }

            if (!changed)
                continue;

            WriteCell(cell.Stack, entries);
            cell.Drive.MarkDirty();
        }

        if (taken.StackSize <= 0)
            return null;

        TouchContents();
        return taken;
    }

    public static List<ItemStack> AggregateLiquid(IWorldAccessor world, BlockPos from)
    {
        var result = new List<ItemStack>();
        if (Link(world, from) != StorageLink.Online)
            return result;

        foreach (var cell in EnumerateCells(world, from))
        {
            foreach (var entry in ReadCell(world, cell.Stack))
            {
                var found = false;
                foreach (var have in result)
                {
                    if (!Same(world, have, entry))
                        continue;

                    var sum = (long)have.StackSize + entry.StackSize;
                    have.StackSize = sum > int.MaxValue ? int.MaxValue : (int)sum;
                    found = true;
                    break;
                }

                if (!found)
                    result.Add(entry.Clone());
            }
        }

        result.Sort((a, b) => string.Compare(SortName(a), SortName(b), StringComparison.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>
    /// Посуда в курсоре. Содержимое в атрибутах описано на одну штуку, а StackSize — сколько таких посудин.
    /// Левая кнопка проходит по всей стопке, правая меняет одну посудину на один литр.
    /// </summary>
    public static void TransferContainer(IWorldAccessor world, BlockPos from, IPlayer player, ItemSlot mouse, ItemStack? hovered, bool oneLitre)
    {
        if (Link(world, from) != StorageLink.Online)
            return;
        if (mouse.Itemstack?.Collectible is not BlockLiquidContainerBase container)
            return;

        var raw = container.GetContent(mouse.Itemstack);
        if (raw != null && raw.StackSize > 0)
            PourContainers(world, from, player, mouse, container, oneLitre);
        else if (hovered != null && hovered.StackSize > 0)
            FillContainers(world, from, player, mouse, container, hovered, oneLitre);

        mouse.MarkDirty();
    }

    private static void PourContainers(IWorldAccessor world, BlockPos from, IPlayer player, ItemSlot mouse, BlockLiquidContainerBase container, bool oneLitre)
    {
        ItemStack? primary = null;
        ItemStack? extra = null;
        var guard = Math.Max(1, mouse.Itemstack?.StackSize ?? 0);
        for (var n = 0; n < guard; n++)
        {
            if (mouse.Itemstack == null || mouse.Itemstack.StackSize <= 0)
                break;

            var content = container.GetContent(mouse.Itemstack);
            if (content == null || content.StackSize <= 0)
                break;

            var proto = content.Clone();
            if (proto.Collectible == null && !proto.ResolveBlockOrItem(world))
                break;
            var props = LiquidProps(proto);
            if (props == null)
                break;

            var one = Math.Max(1, (int)Math.Round(props.ItemsPerLitre));
            var want = oneLitre ? Math.Min(proto.StackSize, one) : proto.StackSize;
            var moved = InsertLiquid(world, from, proto, want);
            if (moved <= 0)
                break;

            var single = mouse.Itemstack.Clone();
            single.StackSize = 1;
            var removed = container.TryTakeContent(single, moved);
            if (removed == null || removed.StackSize <= 0)
            {
                ExtractLiquid(world, from, proto, moved);
                break;
            }

            if (removed.StackSize < moved)
                ExtractLiquid(world, from, proto, moved - removed.StackSize);

            DetachOne(mouse);
            KeepAside(world, player, container, ref primary, ref extra, single);
            if (oneLitre)
                break;
        }

        PlaceChanged(world, player, mouse, primary, extra);
    }

    private static void FillContainers(IWorldAccessor world, BlockPos from, IPlayer player, ItemSlot mouse, BlockLiquidContainerBase container, ItemStack target, bool oneLitre)
    {
        var props = LiquidProps(target);
        if (props == null)
            return;

        ItemStack? primary = null;
        ItemStack? extra = null;
        var guard = Math.Max(1, mouse.Itemstack?.StackSize ?? 0);
        for (var n = 0; n < guard; n++)
        {
            if (mouse.Itemstack == null || mouse.Itemstack.StackSize <= 0)
                break;

            var existing = container.GetContent(mouse.Itemstack);
            if (existing != null && existing.StackSize > 0)
                break;

            var roomLitres = container.CapacityLitres - container.GetCurrentLitres(mouse.Itemstack);
            if (roomLitres <= 0)
                break;

            var roomItems = (int)Math.Floor(roomLitres * props.ItemsPerLitre + 1e-3);
            var one = Math.Max(1, (int)Math.Round(props.ItemsPerLitre));
            var want = oneLitre ? Math.Min(roomItems, one) : roomItems;
            if (want <= 0)
                break;

            var taken = ExtractLiquid(world, from, target, want);
            if (taken == null || taken.StackSize <= 0)
                break;

            var single = mouse.Itemstack.Clone();
            single.StackSize = 1;
            var put = container.TryPutLiquid(single, taken, taken.StackSize / props.ItemsPerLitre);
            if (put < 0)
                put = 0;
            if (put < taken.StackSize)
                InsertLiquid(world, from, taken, taken.StackSize - put);
            if (put <= 0)
                break;

            DetachOne(mouse);
            KeepAside(world, player, container, ref primary, ref extra, single);
            if (oneLitre)
                break;
        }

        PlaceChanged(world, player, mouse, primary, extra);
    }

    private static void DetachOne(ItemSlot mouse)
    {
        if (mouse.Itemstack == null)
            return;

        mouse.Itemstack.StackSize--;
        if (mouse.Itemstack.StackSize <= 0)
            mouse.Itemstack = null;
    }

    private static void KeepAside(IWorldAccessor world, IPlayer player, BlockLiquidContainerBase container, ref ItemStack? primary, ref ItemStack? extra, ItemStack single)
    {
        if (TryPile(container, ref primary, single) || TryPile(container, ref extra, single))
            return;

        HandOff(world, player, single);
    }

    private static bool TryPile(BlockLiquidContainerBase container, ref ItemStack? pile, ItemStack single)
    {
        if (pile == null)
        {
            pile = single;
            return true;
        }

        if (!SameFill(container, pile, single))
            return false;

        var max = Math.Max(1, pile.Collectible?.MaxStackSize ?? 1);
        if (pile.StackSize >= max)
            return false;

        pile.StackSize++;
        return true;
    }

    private static bool SameFill(BlockLiquidContainerBase container, ItemStack a, ItemStack b)
    {
        var left = container.GetContent(a);
        var right = container.GetContent(b);
        var leftSize = left?.StackSize ?? 0;
        var rightSize = right?.StackSize ?? 0;
        if (leftSize == 0 && rightSize == 0)
            return true;
        if (left == null || right == null || leftSize != rightSize)
            return false;

        return left.Collectible == right.Collectible;
    }

    private static void PlaceChanged(IWorldAccessor world, IPlayer player, ItemSlot mouse, ItemStack? primary, ItemStack? extra)
    {
        if (mouse.Itemstack == null)
        {
            mouse.Itemstack = primary;
            HandOff(world, player, extra);
            return;
        }

        HandOff(world, player, primary);
        HandOff(world, player, extra);
    }

    private static void HandOff(IWorldAccessor world, IPlayer player, ItemStack? stack)
    {
        if (stack == null || stack.StackSize <= 0)
            return;

        if (player.InventoryManager?.TryGiveItemstack(stack, true) == true)
            return;
        if (stack.StackSize <= 0 || player.Entity == null)
            return;

        world.SpawnItemEntity(stack, player.Entity.Pos.XYZ);
    }

    public static void DescribeCell(ItemStack cell, IWorldAccessor world, System.Text.StringBuilder dsc)
    {
        var bytes = BytesOf(cell);
        var entries = ReadCell(world, cell);
        var litres = 0f;
        foreach (var entry in entries)
            litres += StackLitres(entry);

        var used = litres <= 0 ? 0 : Math.Min(bytes, (int)Math.Ceiling(litres / LitresPerByte - 1e-4));
        dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-disk-types", entries.Count, MaxCellTypes));
        dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-disk-bytes", used, bytes));
        dsc.AppendLine(Lang.Get("electricalprogressivestorage:estorage-cell-litres", LitresText(litres), LitresText(CapacityLitres(cell))));
    }

    private static string LitresText(float litres)
    {
        if (litres < 0)
            litres = 0;
        var rounded = Math.Round(litres, 1);
        if (Math.Abs(rounded - Math.Round(rounded)) < 0.05)
            return ((int)Math.Round(rounded)).ToString(CultureInfo.InvariantCulture);

        return rounded.ToString("0.0", CultureInfo.InvariantCulture);
    }

    public static void CellBudgets(IWorldAccessor world, BlockPos from, out long litreCap, out int typeCap)
    {
        litreCap = 0;
        typeCap = 0;
        foreach (var cell in EnumerateCells(world, from))
        {
            litreCap += (long)BytesOf(cell.Stack) * LitresPerByte;
            typeCap += MaxCellTypes;
        }
    }

    private readonly struct DiskRef
    {
        public DiskRef(BlockEntity holder, ItemStack stack)
        {
            Drive = holder;
            Stack = stack;
        }

        public BlockEntity Drive { get; }
        public ItemStack Stack { get; }
    }

    private static IEnumerable<DiskRef> EnumerateDisks(IWorldAccessor world, BlockPos from)
    {
        var found = new List<DiskRef>();
        CollectDisks(world, from, found);
        found.Sort((a, b) => DiskPriority(b.Stack).CompareTo(DiskPriority(a.Stack)));
        return found;
    }

    private static void CollectDisks(IWorldAccessor world, BlockPos from, List<DiskRef> found)
    {
        var scan = GetScan(world, from);
        foreach (var pos in scan.Drives)
        {
            InventoryBase? inventory = world.BlockAccessor.GetBlockEntity(pos) is BlockEntityEStorageDrive drive
                ? drive.Inventory
                : null;
            if (inventory == null)
                continue;

            var holder = world.BlockAccessor.GetBlockEntity(pos);
            if (holder == null)
                continue;
            for (var i = 0; i < inventory.Count; i++)
            {
                var slot = inventory[i];
                if (slot.Itemstack is ItemStack stack && stack.Item is ItemEStorageDisk)
                    found.Add(new DiskRef(holder, stack));
            }
        }
    }

    private static IEnumerable<DiskRef> EnumerateCells(IWorldAccessor world, BlockPos from)
    {
        var found = new List<DiskRef>();
        CollectCells(world, from, found);
        found.Sort((a, b) => DiskPriority(b.Stack).CompareTo(DiskPriority(a.Stack)));
        return found;
    }

    private static void CollectCells(IWorldAccessor world, BlockPos from, List<DiskRef> found)
    {
        var scan = GetScan(world, from);
        foreach (var pos in scan.Drives)
        {
            InventoryBase? inventory = world.BlockAccessor.GetBlockEntity(pos) is BlockEntityEStorageDrive drive
                ? drive.Inventory
                : null;
            if (inventory == null)
                continue;

            var holder = world.BlockAccessor.GetBlockEntity(pos);
            if (holder == null)
                continue;
            for (var i = 0; i < inventory.Count; i++)
            {
                var slot = inventory[i];
                if (slot.Itemstack is ItemStack stack && stack.Item is ItemEStorageCell)
                    found.Add(new DiskRef(holder, stack));
            }
        }
    }
}
