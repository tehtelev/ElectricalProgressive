using System.Collections.Generic;
using ElectricalProgressive.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Storage;

/// <summary>
/// В клетке один зарегистрированный блок-сущность. Остальные панели той же клетки живут у него в списке.
/// </summary>
public interface IPanelStack
{
    PanelStack Stack { get; }
    string Seat { get; set; }
    bool Holds(string inventoryId);
}

/// <summary>
/// Гостевые панели той же клетки. В чанке зарегистрирована только первая.
/// </summary>
public sealed class PanelStack
{
    private readonly List<BlockEntity> _guests = [];
    private readonly List<(string Code, ITreeAttribute Tree)> _pending = [];

    public bool Keep;
    private bool _promoting;
    private static readonly Dictionary<string, ITreeAttribute> PromoteTrees = new();

    public IReadOnlyList<BlockEntity> Guests => _guests;

    public int Count => 1 + _guests.Count;

    public void Write(ITreeAttribute tree) => WriteGuests(tree, _guests);

    public void Read(ITreeAttribute tree, IWorldAccessor world, BlockEntity host)
    {
        Discard();
        _pending.Clear();
        var count = tree.GetInt("panels");
        for (var i = 0; i < count && i < 5; i++)
        {
            var code = tree.GetString("panelCode" + i);
            if (string.IsNullOrEmpty(code) || tree["panel" + i] is not ITreeAttribute child)
                continue;
            if (host.Api != null)
                Spawn(world, host.Pos, code, child);
            else
                _pending.Add((code, child));
        }
    }

    public void SpawnPending(IWorldAccessor world, BlockPos pos)
    {
        if (_pending.Count == 0)
            return;
        var waiting = _pending.ToArray();
        _pending.Clear();
        foreach (var (code, tree) in waiting)
            Spawn(world, pos, code, tree);
    }

    public bool TryAdd(IWorldAccessor world, BlockEntity host, Vintagestory.API.Common.Block block, Facing facing)
    {
        if (world.Api == null || block.EntityClass == null || Count >= 6 || host is not IPanelStack seat || FaceTaken(seat, facing, block))
            return false;

        var guest = Create(world, host.Pos, block, facing, null);
        if (guest == null)
            return false;

        _guests.Add(guest);
        host.MarkDirty(true);
        StorageAccess.DirtyTopology();
        return true;
    }

    /// <summary>
    /// Пакеты панелей одной клетки идут на блок-хозяин. Номер выше обычных пакетов сети,
    /// внутри — грань и исходный номер. Свои номера пакетов мода остаются ниже этой границы.
    /// </summary>
    public const int FacePacketBase = 20000;

    public const int FacePacketStride = 2000;

    public static int FacePacket(Facing facing, int packetId)
    {
        var support = Support(facing);
        var face = support?.Index ?? 0;
        return FacePacketBase + face * FacePacketStride + packetId;
    }

    public bool Forward(BlockEntity host, IPlayer player, ref int packetId, byte[] data)
    {
        if (packetId >= FacePacketBase)
        {
            var rel = packetId - FacePacketBase;
            var face = rel / FacePacketStride;
            packetId = rel % FacePacketStride;
            if (host.Api?.World == null || face < 0 || face > 5)
                return true;

            var panel = OnSupport(host.Api.World, host.Pos, BlockFacing.ALLFACES[face]);
            if (panel == null)
                return true;
            if (ReferenceEquals(panel, host))
                return false;

            panel.OnReceivedClientPacket(player, packetId, data);
            return true;
        }

        var opened = player.InventoryManager?.OpenedInventories;
        if (opened == null)
            return false;

        foreach (var guest in _guests)
        {
            if (guest is not IPanelStack seat)
                continue;
            foreach (var inventory in opened)
            {
                if (inventory == null || !seat.Holds(inventory.InventoryID))
                    continue;
                guest.OnReceivedClientPacket(player, packetId, data);
                return true;
            }
        }

        return false;
    }

    public void ReleaseContents(IPlayer? byPlayer)
    {
        if (_promoting || Keep)
            return;
        foreach (var guest in _guests.ToArray())
            guest.OnBlockBroken(byPlayer);
    }

    public void Discard()
    {
        if (Keep)
            return;
        foreach (var guest in _guests)
            guest.OnBlockRemoved();
        _guests.Clear();
    }

    public void RemoveAt(IWorldAccessor world, BlockPos pos, int index, float dropQuantityMultiplier)
    {
        if (index < 0 || index >= _guests.Count)
            return;

        var guest = _guests[index];
        _guests.RemoveAt(index);
        guest.OnBlockBroken(null);
        guest.OnBlockRemoved();
        if (dropQuantityMultiplier > 0 && guest.Block != null)
            world.SpawnItemEntity(new ItemStack(guest.Block), pos.ToVec3d().Add(0.5, 0.5, 0.5));
        world.BlockAccessor.GetBlockEntity(pos)?.MarkDirty(true);
        StorageAccess.DirtyTopology();
    }

    public void Promote(IWorldAccessor world, BlockPos pos, IPlayer? byPlayer, float dropQuantityMultiplier)
    {
        if (_guests.Count == 0 || _promoting)
            return;
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntity host || host is not IPanelHost cableHost)
            return;

        var promoted = _guests[0];
        _guests.RemoveAt(0);
        if (promoted.Block == null)
            return;

        var tree = new TreeAttribute();
        promoted.ToTreeAttributes(tree);
        tree.SetInt("cable", (int)cableHost.Cable);
        tree.SetInt("cableLines", BlockEStorageCable.Pack(cableHost.CableLines));
        tree.SetString("cableCode", cableHost.CableCode ?? "");
        WriteGuests(tree, _guests);

        var oldBlock = host.Block;
        _promoting = true;
        host.OnBlockBroken(byPlayer);
        _promoting = false;

        promoted.OnBlockRemoved();
        foreach (var guest in _guests)
            guest.OnBlockRemoved();
        _guests.Clear();

        var key = PromoteKey(pos);
        Keep = true;
        try
        {
            // Initialize новой сущности забирает дерево до проверки соседей, иначе грань ещё UpNorth.
            PromoteTrees[key] = tree;
            world.BlockAccessor.SetBlock(promoted.Block.Id, pos);
            if (PromoteTrees.Remove(key, out var left))
                world.BlockAccessor.GetBlockEntity(pos)?.FromTreeAttributes(left, world);
        }
        finally
        {
            Keep = false;
            PromoteTrees.Remove(key);
        }

        if (dropQuantityMultiplier > 0 && oldBlock != null)
            world.SpawnItemEntity(new ItemStack(oldBlock), pos.ToVec3d().Add(0.5, 0.5, 0.5));
        world.BlockAccessor.GetBlockEntity(pos)?.MarkDirty(true);
        StorageAccess.DirtyTopology();
    }

    public BlockEntity? At(BlockEntity host, int index)
    {
        if (index <= 0)
            return host;
        var guest = index - 1;
        return guest >= 0 && guest < _guests.Count ? _guests[guest] : null;
    }

    public static void ClaimPromote(BlockEntity entity)
    {
        if (entity.Api?.World == null || entity.Pos == null)
            return;
        if (!PromoteTrees.Remove(PromoteKey(entity.Pos), out var tree))
            return;
        entity.FromTreeAttributes(tree, entity.Api.World);
    }

    private static string PromoteKey(BlockPos pos)
        => pos.X + "/" + pos.Y + "/" + pos.Z + "/" + pos.dimension;

    public static BlockFacing? Support(Facing facing)
    {
        foreach (var face in FacingHelper.Faces(facing))
            return face;
        return null;
    }

    public static Facing FacingOf(BlockEntity entity) => entity switch
    {
        BlockEntityEStorageTerminal terminal => terminal.Facing,
        BlockEntityEStorageInterface panel => panel.Facing,
        BlockEntityEStorageBus bus => bus.Facing,
        _ => Facing.None
    };

    public static string SeatOf(Facing facing)
    {
        var support = Support(facing);
        return support == null ? "" : "/" + support.Index;
    }

    public static bool FaceTaken(IPanelStack host, Facing placing, Vintagestory.API.Common.Block? incoming = null)
    {
        var support = Support(placing);
        if (support == null)
            return true;
        if (Occupied(host, support))
            return true;
        // Интерфейс и шина садятся на жилу. Кабель на этой грани им не мешает.
        if (incoming is BlockEStorageInterface or BlockEStorageBus)
            return false;
        return host is IPanelHost panel && CableUses(panel.Cable, support);
    }

    public static bool BlocksCable(IPanelStack host, Facing cableFacing)
    {
        foreach (var face in FacingHelper.Faces(cableFacing))
        {
            var on = OnFace(host, face);
            if (on == null || on is BlockEntityEStorageInterface or BlockEntityEStorageBus)
                continue;
            return true;
        }

        return false;
    }

    private static BlockEntity? OnFace(IPanelStack host, BlockFacing face)
    {
        if (host is BlockEntity entity && Support(FacingOf(entity)) == face)
            return entity;
        foreach (var guest in host.Stack.Guests)
        {
            if (Support(FacingOf(guest)) == face)
                return guest;
        }

        return null;
    }

    public static bool MountedAny(IWorldAccessor world, BlockPos pos, BlockFacing face)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is not IPanelStack stack)
            return false;
        if (stack is BlockEntity host && Support(FacingOf(host)) == face)
            return true;
        foreach (var guest in stack.Stack.Guests)
        {
            if (Support(FacingOf(guest)) == face)
                return true;
        }

        return false;
    }

    public static bool Meets(IWorldAccessor world, BlockPos cablePos, Facing component, BlockPos panelPos)
    {
        if (component == Facing.None || cablePos == null || panelPos == null || cablePos.dimension != panelPos.dimension)
            return false;
        if (world.BlockAccessor.GetBlockEntity(panelPos) is not IPanelStack stack)
            return false;
        if (stack is IPanelHost host && BlockEStorageCable.MeetsPanel(cablePos, component, panelPos, host.Facing))
            return true;
        foreach (var guest in stack.Stack.Guests)
        {
            if (BlockEStorageCable.MeetsPanel(cablePos, component, panelPos, FacingOf(guest)))
                return true;
        }

        return false;
    }

    public static BlockEntity? OnSupport(IWorldAccessor world, BlockPos pos, BlockFacing face)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is not IPanelStack stack || stack is not BlockEntity host)
            return null;
        if (Support(FacingOf(host)) == face)
            return host;
        foreach (var guest in stack.Stack.Guests)
        {
            if (Support(FacingOf(guest)) == face)
                return guest;
        }

        return null;
    }

    public static int Kind(IWorldAccessor world, BlockPos pos, bool interfaces)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is not IPanelStack stack || stack is not BlockEntity host)
            return 0;
        var count = IsKind(host, interfaces) ? 1 : 0;
        foreach (var guest in stack.Stack.Guests)
        {
            if (IsKind(guest, interfaces))
                count++;
        }

        return count;
    }

    public static int Buses(IWorldAccessor world, BlockPos pos)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is not IPanelStack stack || stack is not BlockEntity host)
            return 0;
        var count = host is BlockEntityEStorageBus ? 1 : 0;
        foreach (var guest in stack.Stack.Guests)
        {
            if (guest is BlockEntityEStorageBus)
                count++;
        }

        return count;
    }

    public static IEnumerable<BlockEntityEStorageInterface> Interfaces(IWorldAccessor world, BlockPos pos)
    {
        var host = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityEStorageInterface;
        if (host != null)
            yield return host;
        if (world.BlockAccessor.GetBlockEntity(pos) is not IPanelStack stack)
            yield break;
        foreach (var guest in stack.Stack.Guests)
        {
            if (guest is BlockEntityEStorageInterface face && !ReferenceEquals(face, host))
                yield return face;
        }
    }

    public static BlockEntityEStorageInterface? Interface(IWorldAccessor world, BlockPos pos, int slot)
    {
        BlockEntityEStorageInterface? encoded = null;
        foreach (var face in Interfaces(world, pos))
        {
            if (face.OwnsJob(slot))
                return face;
            if (encoded == null && Encoded(face, slot))
                encoded = face;
        }

        return encoded;
    }

    /// <summary>
    /// Грань клетки. Слот шаблона у интерфейсов одной клетки общий, без грани читается чужой шаблон.
    /// </summary>
    public static BlockEntityEStorageInterface? Interface(IWorldAccessor world, BlockPos pos, int slot, string seat)
    {
        BlockEntityEStorageInterface? seated = null;
        foreach (var face in Interfaces(world, pos))
        {
            if (face.Seat != (seat ?? ""))
                continue;
            seated = face;
            if (face.OwnsJob(slot))
                return face;
        }

        // Старая ссылка без грани писалась как хозяин клетки. Если заказ один, это он.
        if ((seat ?? "") == "")
        {
            BlockEntityEStorageInterface? only = null;
            var owners = 0;
            foreach (var face in Interfaces(world, pos))
            {
                if (!face.OwnsJob(slot))
                    continue;
                owners++;
                only = face;
            }

            if (owners == 1 && (seated == null || !seated.OwnsJob(slot)))
                return only;
        }

        if (seated != null && Encoded(seated, slot))
            return seated;
        return seated ?? Interface(world, pos, slot);
    }

    public static BlockEntityEStorageInterface? Feeding(IWorldAccessor world, BlockPos panelPos, BlockPos assembler)
    {
        foreach (var face in Interfaces(world, panelPos))
        {
            if (face.Feeds(world, assembler))
                return face;
        }

        return null;
    }

    public static BlockEntityEStorageInterface? FeedingAny(IWorldAccessor world, BlockPos assembler)
    {
        foreach (var face in BlockFacing.ALLFACES)
        {
            var panel = Feeding(world, assembler.AddCopy(face), assembler);
            if (panel != null)
                return panel;
        }

        return null;
    }

    public static BlockEntity? View(IWorldAccessor world, BlockPos? pos, InventoryBase? inventory)
    {
        if (pos == null || inventory == null)
            return null;
        var host = world.BlockAccessor.GetBlockEntity(pos);
        if (Matches(host, inventory))
            return host;
        if (host is IPanelStack stack)
        {
            foreach (var guest in stack.Stack.Guests)
            {
                if (Matches(guest, inventory))
                    return guest;
            }
        }

        return host;
    }

    private static bool Matches(BlockEntity? entity, InventoryBase inventory)
    {
        if (entity is BlockEntityOpenableContainer container && ReferenceEquals(container.Inventory, inventory))
            return true;
        if (entity is BlockEntityEStoragePatternTerminal pattern && ReferenceEquals(pattern.Bench, inventory))
            return true;
        if (entity is BlockEntityEStorageCraftTerminal craft && ReferenceEquals(craft.Bench, inventory))
            return true;
        return false;
    }

    private static bool Encoded(BlockEntityEStorageInterface face, int slot)
    {
        if (slot < 0 || slot >= face.Inventory.Count)
            return false;
        return ItemEStoragePattern.IsEncoded(face.Inventory[slot].Itemstack);
    }

    private static bool IsKind(BlockEntity entity, bool interfaces)
        => interfaces ? entity is BlockEntityEStorageInterface : entity is BlockEntityEStorageTerminal;

    private static bool Occupied(IPanelStack host, BlockFacing face)
    {
        if (host is BlockEntity entity && Support(FacingOf(entity)) == face)
            return true;
        foreach (var guest in host.Stack.Guests)
        {
            if (Support(FacingOf(guest)) == face)
                return true;
        }

        return false;
    }

    private static bool CableUses(Facing cable, BlockFacing face)
    {
        foreach (var bit in FacingHelper.Faces(cable))
        {
            if (bit == face)
                return true;
        }

        return false;
    }

    private static void WriteGuests(ITreeAttribute tree, List<BlockEntity> guests)
    {
        tree.SetInt("panels", guests.Count);
        for (var i = 0; i < guests.Count; i++)
        {
            var guest = guests[i];
            tree.SetString("panelCode" + i, guest.Block?.Code?.ToString() ?? "");
            var child = new TreeAttribute();
            guest.ToTreeAttributes(child);
            tree["panel" + i] = child;
        }
    }

    private void Spawn(IWorldAccessor world, BlockPos pos, string code, ITreeAttribute? tree)
    {
        if (world.Api == null)
            return;
        var block = world.GetBlock(new AssetLocation(code));
        if (block == null || block.Id == 0 || block.EntityClass == null)
            return;
        var guest = Create(world, pos, block, Facing.None, tree);
        if (guest != null)
            _guests.Add(guest);
    }

    private static BlockEntity? Create(IWorldAccessor world, BlockPos pos, Vintagestory.API.Common.Block block, Facing facing, ITreeAttribute? tree)
    {
        if (world.Api == null || block.EntityClass == null)
            return null;
        var guest = world.Api.ClassRegistry.CreateBlockEntity(block.EntityClass);
        if (guest == null)
            return null;

        guest.Pos = pos.Copy();
        guest.Block = block;
        if (tree != null)
            guest.FromTreeAttributes(tree, world);
        else
            ApplyFacing(guest, facing);

        if (guest is IPanelStack seat)
            seat.Seat = SeatOf(FacingOf(guest));
        guest.Initialize(world.Api);
        return guest;
    }

    private static void ApplyFacing(BlockEntity entity, Facing facing)
    {
        switch (entity)
        {
            case BlockEntityEStorageTerminal terminal:
                terminal.Facing = facing;
                break;
            case BlockEntityEStorageInterface panel:
                panel.Facing = facing;
                break;
            case BlockEntityEStorageBus bus:
                bus.Facing = facing;
                break;
        }
    }
}
