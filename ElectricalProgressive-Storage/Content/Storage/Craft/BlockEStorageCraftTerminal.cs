using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ElectricalProgressive.Content.Storage.Craft;

/// <summary>Выход крафтового терминала только показывает крафт. Забирается отдельным пакетом.</summary>
public sealed class ItemSlotCraftPreview : ItemSlot
{
    public ItemSlotCraftPreview(InventoryBase inventory) : base(inventory)
    {
    }

    public override bool CanTake() => false;

    public override bool CanHold(ItemSlot sourceSlot) => false;

    public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge) => false;

    public override void ActivateSlot(ItemSlot sourceSlot, ref ItemStackMoveOperation op)
    {
    }
}

/// <summary>
/// Крафтовый терминал без процессора. Последний рецепт запоминается,
/// и каждый следующий крафт добирает полную порцию из сети, если она там есть.
/// </summary>
public class BlockEntityEStorageCraftTerminal : BlockEntityEStorageTerminal
{
    public const int PacketId = 19104;
    public const int GridSlots = 9;
    public const int OutSlot = 9;
    public const int BenchSlots = 10;
    private const int CraftCap = 64;

    private readonly InventoryGeneric _grid;
    private readonly InventoryGeneric _reserve;
    private readonly ItemStack?[] _template = new ItemStack?[GridSlots];
    private readonly int[] _templateNeed = new int[GridSlots];
    private string _who = "";
    private bool _stocking;

    public BlockEntityEStorageCraftTerminal()
    {
        _grid = new InventoryCraftGrid();
        _reserve = new InventoryGeneric(GridSlots, "estoragecraftreserve", "0", null, (_, inv) => new ItemSlot(inv));
        _grid.SlotModified += OnGridModified;
    }

    public InventoryBase Bench => _grid;

    protected override bool Craft => true;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        var tail = Pos.X + "/" + Pos.Y + "/" + Pos.Z + Seat;
        _grid.LateInitialize("estoragecraftgrid-" + tail, api);
        _reserve.LateInitialize("estoragecraftreserve-" + tail, api);
        if (api.Side != EnumAppSide.Server)
            return;

        AdoptReserve();
        if (api.World != null)
            ReleaseReserve(api.World);
        RegisterGameTickListener(_ => Refresh(null), 500);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        var grid = new TreeAttribute();
        _grid.ToTreeAttributes(grid);
        tree["craftGrid"] = grid;
        var reserve = new TreeAttribute();
        _reserve.ToTreeAttributes(reserve);
        tree["craftReserve"] = reserve;
        tree.SetString("craftWho", _who);
        var template = new TreeAttribute();
        for (var i = 0; i < GridSlots; i++)
        {
            template.SetInt("n" + i, _templateNeed[i]);
            if (_template[i] != null)
                template["s" + i] = new ItemstackAttribute(_template[i]!.Clone());
        }

        tree["craftTemplate"] = template;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        if (tree["craftGrid"] is ITreeAttribute grid)
            _grid.FromTreeAttributes(grid);
        if (tree["craftReserve"] is ITreeAttribute reserve)
            _reserve.FromTreeAttributes(reserve);
        _who = tree.GetString("craftWho", "");
        ClearTemplate();
        if (tree["craftTemplate"] is ITreeAttribute template)
        {
            for (var i = 0; i < GridSlots; i++)
            {
                if (template["s" + i] is not ItemstackAttribute { value: { } saved }
                    || !saved.ResolveBlockOrItem(worldAccessForResolve))
                    continue;

                saved.StackSize = 1;
                saved.Attributes?.RemoveAttribute(ItemSlotPatternSample.SampleKey);
                _template[i] = saved;
                _templateNeed[i] = Math.Max(1, template.GetInt("n" + i));
            }
        }

        for (var i = 0; i < _grid.Count; i++)
            Resolve(_grid[i], worldAccessForResolve);
        for (var i = 0; i < _reserve.Count; i++)
            Resolve(_reserve[i], worldAccessForResolve);
        AdoptReserve();
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        var opened = base.OnPlayerRightClick(byPlayer, blockSel);
        if (opened && Api.Side == EnumAppSide.Server)
        {
            _who = byPlayer.PlayerUID;
            byPlayer.InventoryManager.OpenInventory(_grid);
            Refresh(byPlayer);
        }

        return opened;
    }

    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
    {
        if (Stack.Forward(this, fromPlayer, ref packetid, data))
            return;

        if (packetid == PacketId && Api.Side == EnumAppSide.Server && data is { Length: >= 21 } && data[0] == 10)
        {
            HandleBench(fromPlayer, data);
            return;
        }

        base.OnReceivedClientPacket(fromPlayer, packetid, data);
    }

    public override bool Holds(string inventoryId)
        => base.Holds(inventoryId) || inventoryId == _grid.InventoryID || inventoryId == _reserve.InventoryID;

    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        var world = Api?.World;
        if (world != null)
        {
            for (var i = 0; i < GridSlots; i++)
            {
                if (IsSample(_grid[i].Itemstack))
                    _grid[i].Itemstack = null;
                else
                    Spill(world, _grid[i]);
            }

            _grid[OutSlot].Itemstack = null;
            ReleaseReserve(world);
        }

        base.OnBlockBroken(byPlayer);
    }

    private void HandleBench(IPlayer player, byte[] data)
    {
        var world = Api.World;
        var mouse = player.InventoryManager.MouseItemSlot;
        if (world == null || mouse == null)
            return;

        var slot = BitConverter.ToInt32(data, 1);
        if (slot < 0 || slot >= _grid.Count)
            return;

        _who = player.PlayerUID;
        var op = new ItemStackMoveOperation(
            world,
            (EnumMouseButton)BitConverter.ToInt32(data, 5),
            (EnumModifierKey)BitConverter.ToInt32(data, 9),
            (EnumMergePriority)BitConverter.ToInt32(data, 13))
        {
            WheelDir = BitConverter.ToInt32(data, 17),
            ActingPlayer = player
        };
        if (slot == OutSlot)
        {
            Take(player, op.ShiftDown);
            return;
        }

        _grid.ActivateSlot(slot, mouse, ref op);
        Refresh(player);
        MarkDirty(true);
    }

    private void Take(IPlayer player, bool shift)
    {
        var world = Api.World;
        var mouse = player.InventoryManager.MouseItemSlot;
        if (world == null || mouse == null || _stocking)
            return;

        _who = player.PlayerUID;
        _stocking = true;
        var crafted = 0;
        try
        {
            while (crafted < (shift ? CraftCap : 1))
            {
                var known = Match(world, player, ReadGrid()) != null || (HasTemplate() && !Conflicts(world, ReadGrid()));
                if (!Stock(world, player))
                {
                    if (crafted == 0)
                    {
                        Fail(
                            player,
                            !known
                                ? "electricalprogressivestorage:estorage-craft-norecipe"
                                : StorageAccess.Link(world, Pos) != StorageLink.Online
                                    ? "electricalprogressivestorage:estorage-craft-offline"
                                    : "electricalprogressivestorage:estorage-craft-missing");
                    }

                    break;
                }

                var source = ReadGrid();
                var mass = Mass(source);
                var recipe = Match(world, player, source);
                var made = recipe == null ? null : ItemEStoragePattern.ResolveCraft(world, player, source);
                if (recipe == null || made == null || made.StackSize < 1)
                {
                    if (crafted == 0)
                        Fail(player, "electricalprogressivestorage:estorage-craft-norecipe");
                    break;
                }

                if (!shift && !MouseFits(world, mouse, made))
                {
                    if (crafted == 0)
                        Fail(player, "electricalprogressivestorage:estorage-craft-hands");
                    break;
                }

                var temp = new ItemSlot[GridSlots];
                for (var i = 0; i < GridSlots; i++)
                    temp[i] = new DummySlot(source[i]?.Clone());
                if (!recipe.ConsumeInput(player, temp, 3))
                {
                    if (crafted == 0)
                        Fail(player, "electricalprogressivestorage:estorage-craft-missing");
                    break;
                }

                for (var i = 0; i < GridSlots; i++)
                {
                    _grid[i].Itemstack = temp[i].Itemstack;
                    _grid[i].MarkDirty();
                }

                RememberConsumed(world, source, temp);
                if (!Give(world, player, mouse, made, shift))
                    break;

                crafted++;
                if (!shift || Mass(ReadGrid()) >= mass)
                    break;
            }

            if (crafted > 0)
                Stock(world, player);
            Paint(player);
        }
        finally
        {
            _stocking = false;
        }

        MarkDirty(true);
    }

    private void OnGridModified(int slotId)
    {
        if (_stocking || slotId == OutSlot || Api?.Side != EnumAppSide.Server)
            return;
        if (GridEmpty())
            ClearTemplate();
        Refresh(null);
    }

    private void Refresh(IPlayer? player)
    {
        if (_stocking || Api?.Side != EnumAppSide.Server)
            return;

        _stocking = true;
        try
        {
            if (Paint(player))
                MarkDirty(false);
        }
        finally
        {
            _stocking = false;
        }
    }

    private bool Paint(IPlayer? player)
    {
        var world = Api?.World;
        if (world == null)
            return false;

        player ??= Online(world);
        if (player == null)
            return false;

        var source = ReadGrid();
        var recipe = Match(world, player, source);
        ItemStack?[] want;
        int[] need;
        if (recipe != null)
        {
            var guessed = Needs(recipe, source);
            if (!SameRecipe(world, source, guessed))
                Learn(source, guessed);
            need = CopyNeed();
            want = source;
        }
        else if (HasTemplate() && !Conflicts(world, source))
        {
            want = Overlay(source);
            recipe = Match(world, player, want);
            need = CopyNeed();
        }
        else
            return Show(null);

        if (recipe == null || (!Paid(source, need) && !Supply(world, want, need)))
            return Show(null);

        var shown = Paid(source, need) ? source : want;
        return Show(ItemEStoragePattern.ResolveCraft(world, player, shown));
    }

    /// <summary>
    /// Кладёт в сетку ровно одну порцию. Из сети забирает только когда там хватает на все пустые клетки сразу.
    /// Чужой или повреждённый предмет в клетке не заменяется.
    /// </summary>
    private bool Refill(IWorldAccessor world, ItemStack?[] want, int[] need)
    {
        if (!Supply(world, want, need))
            return Paid(ReadGrid(), need);

        for (var i = 0; i < GridSlots; i++)
        {
            var sample = want[i];
            if (need[i] <= 0 || sample == null)
                continue;

            var haveCount = GridCount(world, i, sample);
            var cap = Math.Max(1, sample.Collectible?.MaxStackSize ?? 1);
            var missing = Math.Min(need[i] - haveCount, cap - haveCount);
            if (missing <= 0)
                continue;

            var proto = sample.Clone();
            proto.StackSize = 1;
            proto.Attributes?.RemoveAttribute(ItemSlotPatternSample.SampleKey);
            var taken = StorageAccess.Extract(world, Pos, proto, missing);
            if (taken == null || taken.StackSize <= 0)
                continue;

            var slot = _grid[i];
            var stack = slot.Itemstack;
            if (haveCount == 0 || stack == null || slot.Empty)
                slot.Itemstack = taken;
            else
                stack.StackSize += taken.StackSize;
            slot.MarkDirty();
        }

        return Paid(ReadGrid(), need);
    }

    private bool Stock(IWorldAccessor world, IPlayer player)
    {
        var source = ReadGrid();
        var recipe = Match(world, player, source);
        if (recipe != null)
        {
            var guessed = Needs(recipe, source);
            if (!SameRecipe(world, source, guessed))
                Learn(source, guessed);
            if (Paid(source, _templateNeed) || Refill(world, _template, _templateNeed))
                return Paid(ReadGrid(), _templateNeed);
            return false;
        }

        if (!HasTemplate() || Conflicts(world, source) || !Refill(world, _template, _templateNeed))
            return false;

        source = ReadGrid();
        recipe = Match(world, player, source);
        return recipe != null && Paid(source, _templateNeed);
    }

    private bool SameRecipe(IWorldAccessor world, ItemStack?[] source, int[] need)
    {
        if (!HasTemplate())
            return false;

        for (var i = 0; i < GridSlots; i++)
        {
            var sample = _template[i];
            var have = source[i];
            if (sample == null || _templateNeed[i] <= 0)
            {
                if (have != null && need[i] > 0)
                    return false;
                continue;
            }

            if (have == null || !StorageAccess.Same(world, have, sample))
                return false;
        }

        return true;
    }

    private int[] CopyNeed()
    {
        var copy = new int[GridSlots];
        for (var i = 0; i < GridSlots; i++)
            copy[i] = _templateNeed[i];
        return copy;
    }

    private bool Supply(IWorldAccessor world, ItemStack?[] want, int[] need)
    {
        if (StorageAccess.Link(world, Pos) != StorageLink.Online)
            return false;

        var protos = new List<ItemStack>();
        var totals = new List<int>();
        for (var i = 0; i < GridSlots; i++)
        {
            var sample = want[i];
            if (need[i] <= 0 || sample == null)
                continue;

            var missing = need[i] - GridCount(world, i, sample);
            if (missing <= 0)
                continue;

            var found = -1;
            for (var k = 0; k < protos.Count; k++)
            {
                if (!StorageAccess.Same(world, protos[k], sample))
                    continue;
                found = k;
                break;
            }

            if (found < 0)
            {
                protos.Add(sample);
                totals.Add(missing);
            }
            else
                totals[found] += missing;
        }

        for (var k = 0; k < protos.Count; k++)
        {
            if (StorageAccess.Count(world, Pos, protos[k]) < totals[k])
                return false;
        }

        return true;
    }

    private void RememberConsumed(IWorldAccessor world, ItemStack?[] before, ItemSlot[] after)
    {
        for (var i = 0; i < GridSlots; i++)
        {
            var had = before[i];
            if (had == null)
                continue;

            var left = after[i].Itemstack;
            var used = had.StackSize;
            if (left != null && !after[i].Empty && StorageAccess.Same(world, left, had))
                used = had.StackSize - left.StackSize;
            if (used <= 0)
                continue;

            var copy = had.Clone();
            copy.StackSize = 1;
            copy.Attributes?.RemoveAttribute(ItemSlotPatternSample.SampleKey);
            _template[i] = copy;
            _templateNeed[i] = used;
        }
    }

    private void Learn(ItemStack?[] source, int[] need)
    {
        for (var i = 0; i < GridSlots; i++)
        {
            if (source[i] == null || need[i] <= 0)
            {
                _template[i] = null;
                _templateNeed[i] = 0;
                continue;
            }

            var copy = source[i]!.Clone();
            copy.StackSize = 1;
            copy.Attributes?.RemoveAttribute(ItemSlotPatternSample.SampleKey);
            _template[i] = copy;
            _templateNeed[i] = need[i];
        }
    }

    private ItemStack?[] Overlay(ItemStack?[] source)
    {
        var view = new ItemStack?[GridSlots];
        for (var i = 0; i < GridSlots; i++)
        {
            if (source[i] != null)
                view[i] = source[i];
            else if (_template[i] != null && _templateNeed[i] > 0)
                view[i] = _template[i];
        }

        return view;
    }

    private bool Conflicts(IWorldAccessor world, ItemStack?[] source)
    {
        for (var i = 0; i < GridSlots; i++)
        {
            var have = source[i];
            if (have == null)
                continue;
            var sample = _template[i];
            if (sample == null || _templateNeed[i] <= 0 || !StorageAccess.Same(world, have, sample))
                return true;
        }

        return false;
    }

    private bool HasTemplate()
    {
        for (var i = 0; i < GridSlots; i++)
        {
            if (_template[i] != null && _templateNeed[i] > 0)
                return true;
        }

        return false;
    }

    private void ClearTemplate()
    {
        for (var i = 0; i < GridSlots; i++)
        {
            _template[i] = null;
            _templateNeed[i] = 0;
        }
    }

    private bool GridEmpty()
    {
        for (var i = 0; i < GridSlots; i++)
        {
            if (!_grid[i].Empty && !IsSample(_grid[i].Itemstack))
                return false;
        }

        return true;
    }

    private int GridCount(IWorldAccessor world, int index, ItemStack want)
    {
        var slot = _grid[index];
        var have = slot.Itemstack;
        if (have == null || slot.Empty || IsSample(have) || !StorageAccess.Same(world, have, want))
            return 0;
        return have.StackSize;
    }

    private bool Give(IWorldAccessor world, IPlayer player, ItemSlot mouse, ItemStack made, bool shift)
    {
        if (!shift)
        {
            if (mouse.Empty)
                mouse.Itemstack = made;
            else if (mouse.Itemstack != null)
                mouse.Itemstack.StackSize += made.StackSize;
            mouse.MarkDirty();
            return true;
        }

        var give = made.Clone();
        player.InventoryManager.TryGiveItemstack(give, true);
        if (give.StackSize <= 0)
            return true;

        ReturnStack(world, give);
        return false;
    }

    private static int Mass(ItemStack?[] grid)
    {
        var mass = 0;
        for (var i = 0; i < GridSlots; i++)
        {
            if (grid[i] != null)
                mass += grid[i]!.StackSize;
        }

        return mass;
    }

    private void AdoptReserve()
    {
        for (var i = 0; i < GridSlots; i++)
        {
            if (IsSample(_grid[i].Itemstack))
                _grid[i].Itemstack = null;

            var held = _reserve[i].Itemstack;
            if (held == null || _reserve[i].Empty || !_grid[i].Empty)
                continue;

            _grid[i].Itemstack = held;
            _reserve[i].Itemstack = null;
        }
    }

    private void ReleaseReserve(IWorldAccessor world)
    {
        for (var i = 0; i < _reserve.Count; i++)
            Spill(world, _reserve[i]);
    }

    private bool Spill(IWorldAccessor world, ItemSlot slot)
    {
        var stack = slot.Itemstack;
        if (stack == null || slot.Empty || IsSample(stack))
        {
            if (IsSample(stack))
                slot.Itemstack = null;
            return false;
        }

        slot.Itemstack = null;
        ReturnStack(world, stack);
        return true;
    }

    private void ReturnStack(IWorldAccessor world, ItemStack stack)
    {
        if (stack.StackSize <= 0)
            return;
        var put = StorageAccess.Link(world, Pos) == StorageLink.Online
            ? StorageAccess.Insert(world, Pos, stack, stack.StackSize)
            : 0;
        if (put >= stack.StackSize)
            return;
        var left = stack.Clone();
        left.StackSize -= put;
        if (left.StackSize > 0)
            world.SpawnItemEntity(left, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }

    private bool Show(ItemStack? result)
    {
        var slot = _grid[OutSlot];
        var have = slot.Itemstack;
        if (result == null)
        {
            if (have == null)
                return false;
            slot.Itemstack = null;
            slot.MarkDirty();
            return true;
        }

        if (have != null && Api.World != null && StorageAccess.Same(Api.World, have, result) && have.StackSize == result.StackSize)
            return false;
        slot.Itemstack = result;
        slot.MarkDirty();
        return true;
    }

    private ItemStack?[] ReadGrid()
    {
        var grid = new ItemStack?[GridSlots];
        for (var i = 0; i < GridSlots; i++)
        {
            var stack = _grid[i].Itemstack;
            if (stack == null || _grid[i].Empty || IsSample(stack))
                continue;
            var copy = stack.Clone();
            copy.Attributes.RemoveAttribute(ItemSlotPatternSample.SampleKey);
            grid[i] = copy;
        }

        return grid;
    }

    private IPlayer? Online(IWorldAccessor world)
    {
        if (_who.Length > 0 && world.PlayerByUid(_who) is { } known)
            return known;
        foreach (var player in world.AllOnlinePlayers)
            return player;
        return null;
    }

    private static bool Paid(ItemStack?[] grid, int[] need)
    {
        for (var i = 0; i < GridSlots; i++)
        {
            if (need[i] <= 0)
                continue;
            if (grid[i] == null || grid[i]!.StackSize < need[i])
                return false;
        }

        return true;
    }

    private static int[] Needs(GridRecipe recipe, ItemStack?[] samples)
    {
        var need = new int[GridSlots];
        var list = recipe.ResolvedIngredients;
        var used = new bool[list?.Length ?? 0];
        for (var i = 0; i < GridSlots; i++)
        {
            var sample = samples[i];
            if (sample == null)
                continue;
            var count = 1;
            if (list != null)
            {
                for (var n = 0; n < list.Length; n++)
                {
                    var ingredient = list[n];
                    if (used[n] || ingredient == null || !ingredient.SatisfiesAsIngredient(sample, false))
                        continue;
                    used[n] = true;
                    count = Math.Max(1, ingredient.Quantity);
                    break;
                }
            }

            need[i] = count;
        }

        return need;
    }

    private static GridRecipe? Match(IWorldAccessor world, IPlayer player, ItemStack?[] grid)
    {
        var slots = new ItemSlot[GridSlots];
        for (var i = 0; i < GridSlots; i++)
        {
            var sample = grid[i]?.Clone();
            if (sample != null)
                sample.StackSize = 1;
            slots[i] = new DummySlot(sample);
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

    private static bool MouseFits(IWorldAccessor world, ItemSlot mouse, ItemStack stack)
    {
        if (mouse.Empty || mouse.Itemstack == null)
            return true;
        if (!StorageAccess.Same(world, mouse.Itemstack, stack))
            return false;
        var cap = Math.Max(1, mouse.Itemstack.Collectible?.MaxStackSize ?? 1);
        return mouse.StackSize + stack.StackSize <= cap;
    }

    private static bool IsSample(ItemStack? stack)
        => stack?.Attributes?.HasAttribute(ItemSlotPatternSample.SampleKey) == true;

    /// <summary>
    /// Шифт из инвентаря игрока сдаёт в сеть. Иначе открытая сетка перехватывает стопку.
    /// В клетки предмет кладётся кликом.
    /// </summary>
    private sealed class InventoryCraftGrid : InventoryGeneric
    {
        public InventoryCraftGrid()
            : base(BenchSlots, "estoragecraftgrid", "0", null, NewGridSlot)
        {
        }

        public override WeightedSlot GetBestSuitedSlot(ItemSlot sourceSlot, ItemStackMoveOperation op, List<ItemSlot> skipSlots)
            => new();
    }

    private static void Fail(IPlayer player, string key)
    {
        if (player is IServerPlayer server)
            server.SendIngameError("estoragecraft", Lang.Get(key));
    }

    private static ItemSlot NewGridSlot(int index, InventoryGeneric inventory)
        => index == OutSlot ? new ItemSlotCraftPreview(inventory) : new ItemSlot(inventory);

    private static void Resolve(ItemSlot slot, IWorldAccessor world)
    {
        if (slot.Itemstack != null && !slot.Itemstack.ResolveBlockOrItem(world))
            slot.Itemstack = null;
    }
}
