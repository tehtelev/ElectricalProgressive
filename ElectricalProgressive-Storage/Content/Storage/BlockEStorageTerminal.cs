using System;
using System.Collections.Generic;
using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Storage;

public class BlockEStorageTerminal : BlockEStoragePart
{
    private static readonly Dictionary<(Facing, string, int), MeshData> MeshCache = [];
    private static readonly Dictionary<(Facing, string), Cuboidf[]> SelectionBoxesCache = [];
    private static readonly Dictionary<(Facing, string), Cuboidf[]> CollisionBoxesCache = [];

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        var selection = new Selection(blockSel);
        Facing facing;
        try
        {
            facing = FacingHelper.From(selection.Face, selection.Direction);
        }
        catch
        {
            return false;
        }

        if (!CanStay(world, blockSel.Position, facing))
            return false;

        return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }

    public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
    {
        var selection = new Selection(blockSel);
        var facing = FacingHelper.From(selection.Face, selection.Direction);
        if (!base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack) ||
            !TryWriteFacing(world.BlockAccessor.GetBlockEntity(blockSel.Position), facing))
        {
            return false;
        }

        return true;
    }

    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        if (world.Side != EnumAppSide.Server)
            return;
        if (!TryReadFacing(world.BlockAccessor.GetBlockEntity(pos), out var facing))
            return;

        if (CanStay(world, pos, facing))
            return;

        world.BlockAccessor.BreakBlock(pos, null);
    }

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        => RotatedBoxes(pos, CollisionBoxesCache, CollisionBoxes);

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
        => RotatedBoxes(pos, SelectionBoxesCache, SelectionBoxes);

    private Cuboidf[] RotatedBoxes(BlockPos pos, Dictionary<(Facing, string), Cuboidf[]> cache, Cuboidf[] sourceBoxes)
    {
        if (sourceBoxes == null || sourceBoxes.Length == 0)
            return [];

        var facing = Facing.UpNorth;
        var code = Code?.ToString() ?? "";
        var entity = api?.World?.BlockAccessor.GetBlockEntity(pos);
        if (TryReadFacing(entity, out var read))
        {
            facing = read;
            code = entity?.Block?.Code?.ToString() ?? code;
        }

        if (!cache.TryGetValue((facing, code), out var boxes))
        {
            boxes = (Cuboidf[])sourceBoxes.Clone();
            FacingRotations.ApplyRotations(boxes, facing);
            cache.TryAdd((facing, code), boxes);
        }

        return boxes;
    }

    public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
    {
        base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);
        if (api is not ICoreClientAPI)
            return;

        var facing = Facing.UpNorth;
        var code = Code?.ToString() ?? "";
        var entity = api.World.BlockAccessor.GetBlockEntity(pos);
        if (TryReadFacing(entity, out var read))
        {
            facing = read;
            code = entity?.Block?.Code?.ToString() ?? code;
        }

        var cacheKey = (facing, code, sourceMesh.VerticesCount);
        if (!MeshCache.TryGetValue(cacheKey, out var meshData))
        {
            meshData = sourceMesh.Clone();
            FacingRotations.ApplyRotations(meshData, facing);
            MeshCache.TryAdd(cacheKey, meshData);
        }

        sourceMesh = meshData;
        if (entity is BlockEntityEStorageInterface panel && panel.Cable != Facing.None && api is ICoreClientAPI capi)
            BlockEStorageCable.Append(capi, panel, ref sourceMesh);
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        base.OnUnloaded(api);
        MeshCache.Clear();
        SelectionBoxesCache.Clear();
        CollisionBoxesCache.Clear();
    }

    protected virtual bool CanStay(IWorldAccessor world, BlockPos pos, Facing facing)
        => MyMiniLib.CheckSolidFace(world.BlockAccessor, pos, facing);

    private static bool TryReadFacing(BlockEntity? entity, out Facing facing)
    {
        switch (entity)
        {
            case BlockEntityEStorageTerminal terminal when terminal.Facing != Facing.None:
                facing = terminal.Facing;
                return true;
            case BlockEntityEStorageInterface panel when panel.Facing != Facing.None:
                facing = panel.Facing;
                return true;
            default:
                facing = Facing.UpNorth;
                return false;
        }
    }

    private static bool TryWriteFacing(BlockEntity? entity, Facing facing)
    {
        switch (entity)
        {
            case BlockEntityEStorageTerminal terminal:
                terminal.Facing = facing;
                terminal.MarkDirty(true);
                return true;
            case BlockEntityEStorageInterface panel:
                panel.Facing = facing;
                panel.MarkDirty(true);
                return true;
            default:
                return false;
        }
    }

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel == null || !world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        var link = StorageAccess.Link(world, blockSel.Position);
        if (link != StorageLink.Online)
        {
            if (world.Api is ICoreClientAPI capi)
            {
                capi.TriggerIngameError(this, "estorage", Lang.Get(LinkKey(link)));
            }

            return true;
        }

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityEStorageTerminal terminal)
            return terminal.OnPlayerRightClick(byPlayer, blockSel);

        return false;
    }

    public static string LinkKey(StorageLink link) => link switch
    {
        StorageLink.Conflict => "electricalprogressivestorage:estorage-conflict",
        StorageLink.NoPower => "electricalprogressivestorage:estorage-nopower",
        StorageLink.NoChannel => "electricalprogressivestorage:estorage-nochannel",
        StorageLink.NoController => "electricalprogressivestorage:estorage-nocontroller",
        _ => "electricalprogressivestorage:estorage-online"
    };
}

public class BlockEntityEStorageTerminal : BlockEntityOpenableContainer
{
    private readonly InventoryEStorageTerminal _inventory;
    public Facing Facing = Facing.UpNorth;
    private long _seenContent = -1;
    private bool _settled;
    private bool _pushed;

    public BlockEntityEStorageTerminal()
    {
        _inventory = CreateInventory();
    }

    protected virtual InventoryEStorageTerminal CreateInventory() => new();

    protected virtual bool Liquid => false;

    protected virtual bool Pattern => false;

    public override InventoryBase Inventory => _inventory;

    public override string InventoryClassName => Liquid ? "estorageliquidterminal" : "estorageterminal";

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        _inventory.Origin = Pos.Copy();
        _inventory.AfterRebuild = () =>
        {
            if (Api?.Side == EnumAppSide.Server)
                MarkDirty(true);
        };
        var prefix = Liquid ? "estorageliquidterminal-" : "estorageterminal-";
        _inventory.LateInitialize(prefix + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(_ => RefreshIfNeeded(), 500);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("facing", (int)Facing);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        var raw = tree.GetInt("facing", (int)Facing.UpNorth);
        Facing = raw == 0 ? Facing.UpNorth : (Facing)raw;
    }

    private void RefreshIfNeeded()
    {
        if (Api.World == null)
            return;

        var scan = StorageAccess.GetScan(Api.World, Pos);
        var settled = scan.Pending.Count == 0;
        var generation = StorageAccess.ContentGeneration;
        if (_pushed && _seenContent == generation && _settled == settled)
            return;

        var becameSettled = settled && !_settled;
        _seenContent = generation;
        _settled = settled;
        var changed = _inventory.Rebuild();
        // Первый проход в Initialize слишком ранний: диски ещё без предмета, и клиент запоминает пустое окно.
        if (changed || !_pushed || becameSettled)
        {
            MarkDirty(true);
            _pushed = true;
        }
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.World != null && StorageAccess.Link(Api.World, Pos) != StorageLink.Online)
            return false;

        // И сервер, и клиент собирают вид с дисков прямо сейчас. Иначе окно открывается пустым,
        // пока в терминал что-нибудь не положат.
        _inventory.Rebuild();

        if (Api.Side == EnumAppSide.Server)
        {
            MarkDirty(true);
            byPlayer.InventoryManager.OpenInventory(_inventory);
        }

        if (Api.Side == EnumAppSide.Client)
        {
            var title = Pattern
                ? "electricalprogressivestorage:estorage-pattern-title"
                : Liquid
                    ? "electricalprogressivestorage:estorage-liquid-title"
                    : "electricalprogressivestorage:estorage-terminal-title";
            var liquid = Liquid;
            var pattern = Pattern;
            toggleInventoryDialogClient(byPlayer, () =>
                new GuiDialogEStorageTerminal(
                    Lang.Get(title),
                    Inventory,
                    Pos,
                    (ICoreClientAPI)Api,
                    liquid,
                    pattern));
        }

        return true;
    }

    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
    {
        if (!Liquid && !Pattern && packetid == StorageAccess.OrderPacketId && Api.Side == EnumAppSide.Server && data is { Length: > 0 })
        {
            Order(fromPlayer, data);
            return;
        }

        if (Liquid && packetid == StorageAccess.LiquidPacketId && Api.Side == EnumAppSide.Server && Api.World != null && data is { Length: >= 8 })
        {
            var mouse = fromPlayer.InventoryManager?.MouseItemSlot;
            if (mouse != null)
            {
                var oneLitre = BitConverter.ToInt32(data, 0) != 0;
                var slot = BitConverter.ToInt32(data, 4);
                ItemStack? hovered = null;
                if (slot >= 0 && slot < _inventory.Count)
                    hovered = _inventory[slot].Itemstack;
                StorageAccess.TransferContainer(Api.World, Pos, fromPlayer, mouse, hovered, oneLitre);
                _inventory.Rebuild();
                MarkDirty(true);
            }

            return;
        }

        base.OnReceivedClientPacket(fromPlayer, packetid, data);
    }

    private void Order(IPlayer player, byte[] data)
    {
        var reply = new EStorageOrderReply();
        if (Api.World == null)
        {
            reply.Failed = true;
            reply.Text = Lang.Get("electricalprogressivestorage:estorage-order-none");
            EStorageOrderSync.Send(player, reply);
            return;
        }

        var link = StorageAccess.Link(Api.World, Pos);
        if (link != StorageLink.Online)
        {
            reply.Failed = true;
            reply.Text = Lang.Get(BlockEStorageTerminal.LinkKey(link));
            EStorageOrderSync.Send(player, reply);
            return;
        }

        var tree = new TreeAttribute();
        try
        {
            tree.FromBytes(data);
        }
        catch
        {
            reply.Failed = true;
            EStorageOrderSync.Send(player, reply);
            return;
        }

        var count = tree.GetInt("count");
        if (count <= 0 || tree["out"] is not ItemstackAttribute { value: { } proto })
        {
            reply.Failed = true;
            reply.Text = Lang.Get("electricalprogressivestorage:estorage-order-none");
            EStorageOrderSync.Send(player, reply);
            return;
        }

        proto.ResolveBlockOrItem(Api.World);
        proto.Attributes.RemoveAttribute(StorageAccess.CraftGhostKey);
        proto.Attributes.RemoveAttribute(StorageAccess.OrderKey);
        if (proto.Collectible == null)
        {
            reply.Failed = true;
            reply.Text = Lang.Get("electricalprogressivestorage:estorage-order-none");
            EStorageOrderSync.Send(player, reply);
            return;
        }

        var plan = CraftPlan.Build(Api.World, Pos, proto, count, player);
        reply.Text = plan.Text;
        reply.Bytes = plan.Bytes;
        reply.Cells = plan.Cells ?? [];
        reply.NoCpu = plan.NoCpu;
        reply.Failed = plan.Failed;
        if (tree.GetBool("start") && !plan.Failed && !plan.NoCpu)
        {
            if (CraftPlan.Place(Api.World, plan))
                reply.Started = true;
            else
                reply.NoCpu = true;
        }

        EStorageOrderSync.Send(player, reply);
    }

    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        _inventory.Rebuilding = true;
        for (var i = 0; i < _inventory.Count; i++)
            _inventory[i].Itemstack = null;
        _inventory.Rebuilding = false;
        base.OnBlockBroken(byPlayer);
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        StorageAccess.DirtyTopology();
    }
}

public class BlockEntityEStorageLiquidTerminal : BlockEntityEStorageTerminal
{
    protected override InventoryEStorageTerminal CreateInventory() => new InventoryEStorageLiquid();

    protected override bool Liquid => true;
}
