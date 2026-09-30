using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ElectricalProgressive.Content.Storage;

public class InventoryEStoragePattern : InventoryEStorageTerminal
{
    public InventoryEStoragePattern()
        : base(false, "estoragepatternterminal")
    {
    }
}

public class ItemSlotPatternBlank : ItemSlot
{
    public ItemSlotPatternBlank(InventoryBase inventory) : base(inventory)
    {
    }

    public override bool CanHold(ItemSlot sourceSlot) => ItemEStoragePattern.IsBlank(sourceSlot.Itemstack);
}

public class ItemSlotPatternOut : ItemSlot
{
    public ItemSlotPatternOut(InventoryBase inventory) : base(inventory)
    {
    }

    public override bool CanHold(ItemSlot sourceSlot)
    {
        var stack = sourceSlot.Itemstack;
        if (!ItemEStoragePattern.IsEncoded(stack) || stack == null)
            return false;
        if (Empty || Itemstack == null || Inventory.Api?.World == null)
            return Empty;
        return StorageAccess.Same(Inventory.Api.World, Itemstack, stack);
    }
}

/// <summary>
/// Выход крафта только показывает рецепт верстака. Забрать его нельзя.
/// </summary>
public class ItemSlotPatternPreview : ItemSlot
{
    public ItemSlotPatternPreview(InventoryBase inventory) : base(inventory)
    {
    }

    public override bool CanTake() => false;

    public override bool CanHold(ItemSlot sourceSlot) => false;

    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
    }
}

public class BlockEntityEStoragePatternTerminal : BlockEntityEStorageTerminal
{
    public const int PacketId = 19102;
    public const int BlankSlot = 0;
    public const int EncodedSlot = 1;
    public const int GridSlot = 2;
    public const int CraftOutSlot = 11;
    public const int ProcessSlot = 12;
    public const int BenchSlots = 15;

    private readonly InventoryGeneric _bench;
    private bool _migrated;

    public bool Processing;
    public bool Substitute;

    public BlockEntityEStoragePatternTerminal()
    {
        _bench = new InventoryGeneric(BenchSlots, "estoragepatternbench", "0", null, NewBenchSlot);
    }

    public InventoryBase Bench => _bench;

    protected override InventoryEStorageTerminal CreateInventory() => new InventoryEStoragePattern();

    protected override bool Pattern => true;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        _bench.LateInitialize("estoragepatternbench-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);
        if (_migrated && api.Side == EnumAppSide.Server)
            MarkDirty(true);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        var bench = new TreeAttribute();
        _bench.ToTreeAttributes(bench);
        tree["bench"] = bench;
        tree.SetBool("processing", Processing);
        tree.SetBool("substitute", Substitute);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        if (tree["bench"] is ITreeAttribute bench)
            _bench.FromTreeAttributes(bench);
        Processing = tree.GetBool("processing");
        Substitute = tree.GetBool("substitute");
        for (var i = 0; i < _bench.Count; i++)
            ResolveSlot(_bench[i], worldAccessForResolve);
        Migrate(tree, worldAccessForResolve);
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        var opened = base.OnPlayerRightClick(byPlayer, blockSel);
        if (opened && Api.Side == EnumAppSide.Server)
        {
            byPlayer.InventoryManager.OpenInventory(_bench);
            RefreshCraft(byPlayer);
        }

        return opened;
    }

    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
    {
        if (packetid == PacketId && Api.Side == EnumAppSide.Server && data is { Length: >= 4 })
        {
            if (data[0] == 10 && data.Length >= 21)
                HandleBench(fromPlayer, data);
            else
                Handle(fromPlayer, data[0], data[1]);
            return;
        }

        base.OnReceivedClientPacket(fromPlayer, packetid, data);
    }

    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        if (Api.World != null)
        {
            for (var i = 0; i < _bench.Count; i++)
            {
                if (i == CraftOutSlot)
                    continue;
                var stack = _bench[i].Itemstack;
                if (stack == null || _bench[i].Empty)
                    continue;
                Api.World.SpawnItemEntity(stack.Clone(), Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                _bench[i].Itemstack = null;
            }
        }

        base.OnBlockBroken(byPlayer);
    }

    private void Handle(IPlayer player, byte op, byte arg)
    {
        if (Api.World == null || StorageAccess.Link(Api.World, Pos) != StorageLink.Online)
            return;

        switch (op)
        {
            case 3:
                Processing = arg != 0;
                RefreshCraft(player);
                MarkDirty(true);
                break;
            case 4:
                Substitute = !Substitute;
                MarkDirty(true);
                break;
            case 5:
                EncodeOne(player);
                break;
        }
    }

    private void HandleBench(IPlayer player, byte[] data)
    {
        var world = Api.World;
        var mouse = player.InventoryManager.MouseItemSlot;
        if (world == null || mouse == null || StorageAccess.Link(world, Pos) != StorageLink.Online)
            return;

        var slot = BitConverter.ToInt32(data, 1);
        if (slot < 0 || slot >= _bench.Count || slot == CraftOutSlot)
            return;

        var op = new ItemStackMoveOperation(
            world,
            (EnumMouseButton)BitConverter.ToInt32(data, 5),
            (EnumModifierKey)BitConverter.ToInt32(data, 9),
            (EnumMergePriority)BitConverter.ToInt32(data, 13))
        {
            WheelDir = BitConverter.ToInt32(data, 17),
            ActingPlayer = player
        };
        _bench.ActivateSlot(slot, mouse, ref op);
        RefreshCraft(player);
        MarkDirty(true);
    }

    private void EncodeOne(IPlayer player)
    {
        var world = Api.World;
        if (world == null)
            return;
        if (!ItemEStoragePattern.IsBlank(_bench[BlankSlot].Itemstack))
        {
            Fail(player, "electricalprogressivestorage:estorage-pattern-noblank");
            return;
        }

        var made = ItemEStoragePattern.Encode(world, player, ReadGrid(), ReadProcess(), Processing, Substitute);
        if (made == null)
        {
            Fail(player, Processing
                ? "electricalprogressivestorage:estorage-pattern-needout"
                : "electricalprogressivestorage:estorage-craft-norecipe");
            return;
        }

        var dummy = new DummySlot(made);
        var op = new ItemStackMoveOperation(world, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, 1)
        {
            ActingPlayer = player
        };
        dummy.TryPutInto(_bench[EncodedSlot], ref op);
        if (!dummy.Empty)
        {
            Fail(player, "electricalprogressivestorage:estorage-pattern-outputfull");
            return;
        }

        _bench[BlankSlot].TakeOut(1);
        _bench[BlankSlot].MarkDirty();
        _bench[EncodedSlot].MarkDirty();
        MarkDirty(true);
    }

    private void RefreshCraft(IPlayer player)
    {
        var world = Api.World;
        if (world == null)
            return;

        var preview = _bench[CraftOutSlot];
        ItemStack? resolved = null;
        if (!Processing)
            resolved = ItemEStoragePattern.ResolveCraft(world, player, ReadGrid());
        if (Same(world, preview.Itemstack, resolved))
            return;
        preview.Itemstack = resolved;
        preview.MarkDirty();
    }

    private ItemStack?[] ReadGrid()
    {
        var grid = new ItemStack?[9];
        for (var i = 0; i < 9; i++)
            grid[i] = _bench[GridSlot + i].Itemstack?.Clone();
        return grid;
    }

    private ItemStack?[] ReadProcess()
    {
        var outputs = new ItemStack?[3];
        for (var i = 0; i < 3; i++)
            outputs[i] = _bench[ProcessSlot + i].Itemstack?.Clone();
        return outputs;
    }

    private void Migrate(ITreeAttribute tree, IWorldAccessor world)
    {
        if (tree["grid"] is not ITreeAttribute grid)
            return;

        for (var i = 0; i < 9; i++)
        {
            var slot = _bench[GridSlot + i];
            if (!slot.Empty)
                continue;
            var stack = ReadStack(grid["g" + i], world);
            if (stack == null)
                continue;
            slot.Itemstack = stack;
            _migrated = true;
        }

        if (!Processing || !_bench[ProcessSlot].Empty)
            return;
        var result = ReadStack(tree["result"], world);
        if (result == null)
            return;
        _bench[ProcessSlot].Itemstack = result;
        _migrated = true;
    }

    private static bool Same(IWorldAccessor world, ItemStack? left, ItemStack? right)
    {
        if (left == null || right == null)
            return left == null && right == null;
        return left.Equals(world, right) && left.StackSize == right.StackSize;
    }

    private static void Fail(IPlayer player, string key)
    {
        if (player is IServerPlayer server)
            server.SendIngameError("estoragepattern", Lang.Get(key));
    }

    private static ItemSlot NewBenchSlot(int index, InventoryGeneric inventory)
    {
        if (index == BlankSlot)
            return new ItemSlotPatternBlank(inventory);
        if (index == EncodedSlot)
            return new ItemSlotPatternOut(inventory);
        if (index == CraftOutSlot)
            return new ItemSlotPatternPreview(inventory);
        return new ItemSlot(inventory);
    }

    private static ItemStack? ReadStack(IAttribute? attribute, IWorldAccessor world)
    {
        if (attribute is not ItemstackAttribute { value: { } stack })
            return null;
        var copy = stack.Clone();
        if (!copy.ResolveBlockOrItem(world))
            return null;
        return copy;
    }

    private static void ResolveSlot(ItemSlot slot, IWorldAccessor world)
    {
        if (slot.Itemstack != null && !slot.Itemstack.ResolveBlockOrItem(world))
            slot.Itemstack = null;
    }
}
