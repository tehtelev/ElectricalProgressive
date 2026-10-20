using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Storage.IoPort;

/// <summary>
/// Порт обмена. Диск или ячейка в слоте не входят в сеть: содержимое переносится в сеть или из неё.
/// </summary>
public class BlockEStorageIoPort : BlockEStoragePart
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel == null || !world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityEStorageIoPort port)
            return false;

        if (byPlayer.Entity.Controls.Sneak)
        {
            if (world.Side == EnumAppSide.Server)
                port.Toggle();
            return true;
        }

        return port.OnPlayerRightClick(byPlayer, blockSel);
    }

    public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
    {
        var port = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityEStorageIoPort;
        var into = port == null || port.IntoNetwork;
        var link = StorageAccess.Link(world, pos);
        var direction = into
            ? "electricalprogressivestorage:estorage-ioport-in"
            : "electricalprogressivestorage:estorage-ioport-out";
        return Lang.Get(BlockEStorageTerminal.LinkKey(link)) + "\n"
            + Lang.Get(direction) + "\n"
            + Lang.Get("electricalprogressivestorage:estorage-ioport-draw", (int)BlockEntityEStorageIoPort.Watts) + "\n"
            + Lang.Get("electricalprogressivestorage:estorage-ioport-hint");
    }
}

/// <summary>
/// Четыре слота и перенос пачками. Диск отдаёт предметы, ячейка — жидкость.
/// </summary>
public class BlockEntityEStorageIoPort : BlockEntityOpenableContainer, ITexPositionSource
{
    public const float Watts = 6f;
    public const int TogglePacket = 19120;
    public const int BatchItems = 32;
    public const int BatchLitres = 32;

    // Два столбца, сверху вниз: левый, правый, левый, правый.
    // Отверстия под индикатором: x 1–7.55 и 8.45–15, y 5–7 и 1.15–3.
    private static readonly float[] WindowX = [4.275f, 11.725f, 4.275f, 11.725f];
    private static readonly float[] WindowY = [6f, 6f, 2.075f, 2.075f];

    private readonly InventoryGeneric _inventory;
    private MeshData? _mesh;
    private ICoreClientAPI? _capi;
    private Shape? _nowTesselatingShape;
    private CollectibleObject? _nowTesselatingObj;

    public bool IntoNetwork { get; private set; } = true;

    public BlockEntityEStorageIoPort()
    {
        _inventory = new InventoryGeneric(WindowX.Length, "estorageioport", "0", null, (_, inv) => new ItemSlotDisk(inv));
        _inventory.SlotModified += _ =>
        {
            if (Api?.Side != EnumAppSide.Client || Pos == null)
                return;
            UpdateMesh();
            Api.World.BlockAccessor.MarkBlockDirty(Pos);
        };
    }

    public override InventoryBase Inventory => _inventory;

    public override string InventoryClassName => "estorageioport";

    public override void Initialize(ICoreAPI api)
    {
        if (api is ICoreClientAPI capi)
            _capi = capi;
        base.Initialize(api);
        _inventory.LateInitialize("estorageioport-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(_ => Pump(), 200);
        else
        {
            UpdateMesh();
            api.World.BlockAccessor.MarkBlockDirty(Pos);
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("dir", IntoNetwork ? 1 : 0);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        IntoNetwork = tree.GetInt("dir", 1) != 0;
    }

    public void Toggle()
    {
        IntoNetwork = !IntoNetwork;
        MarkDirty(true);
    }

    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
    {
        if (packetid == TogglePacket)
        {
            Toggle();
            return;
        }

        base.OnReceivedClientPacket(fromPlayer, packetid, data);
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            toggleInventoryDialogClient(byPlayer, () =>
                new GuiDialogEStorageIoPort(
                    Lang.Get("electricalprogressivestorage:estorage-ioport-title"),
                    Inventory,
                    Pos,
                    (ICoreClientAPI)Api));
        }

        return true;
    }

    private void Pump()
    {
        if (Api.Side != EnumAppSide.Server || Pos == null)
            return;

        if (StorageAccess.Link(Api.World, Pos) != StorageLink.Online)
            return;

        for (var i = 0; i < _inventory.Count; i++)
        {
            var stack = _inventory[i].Itemstack;
            if (stack?.Item is ItemEStorageDisk)
                PumpDisk(_inventory[i]);
            else if (stack?.Item is ItemEStorageCell)
                PumpCell(_inventory[i]);
        }
    }

    private void PumpDisk(ItemSlot slot)
    {
        var disk = slot.Itemstack;
        if (disk == null)
            return;

        var world = Api.World;
        var entries = StorageAccess.ReadDisk(world, disk);
        if (IntoNetwork)
        {
            var budget = BatchItems;
            var moved = 0;
            foreach (var entry in entries)
            {
                if (budget <= 0 || entry.StackSize <= 0)
                    continue;
                var did = StorageAccess.Insert(world, Pos, entry, Math.Min(budget, entry.StackSize));
                if (did <= 0)
                    continue;
                entry.StackSize -= did;
                budget -= did;
                moved += did;
            }

            if (moved <= 0)
                return;
            StorageAccess.WriteDisk(disk, entries);
            slot.MarkDirty();
            return;
        }

        var changed = false;
        var left = BatchItems;
        foreach (var proto in StorageAccess.Aggregate(world, Pos))
        {
            if (left <= 0)
                break;
            if (proto.Item is ItemEStorageDisk or ItemEStorageCell)
                continue;
            var room = StorageAccess.RoomOnDisk(world, disk, proto);
            var want = Math.Min(left, Math.Min(room, proto.StackSize));
            if (want <= 0)
                continue;
            var got = StorageAccess.Extract(world, Pos, proto, want);
            if (got == null || got.StackSize <= 0)
                continue;
            Merge(world, entries, got);
            StorageAccess.WriteDisk(disk, entries);
            left -= got.StackSize;
            changed = true;
        }

        if (changed)
            slot.MarkDirty();
    }

    private void PumpCell(ItemSlot slot)
    {
        var cell = slot.Itemstack;
        if (cell == null)
            return;

        var world = Api.World;
        var entries = StorageAccess.ReadCell(world, cell);
        var litres = BatchLitres;
        var changed = false;
        if (IntoNetwork)
        {
            foreach (var entry in entries)
            {
                if (litres <= 0 || entry.StackSize <= 0)
                    continue;
                var want = Math.Min(entry.StackSize, ItemsForLitres(entry, litres));
                if (want <= 0)
                    continue;
                var did = StorageAccess.InsertLiquid(world, Pos, entry, want);
                if (did <= 0)
                    continue;
                entry.StackSize -= did;
                litres -= LitresOf(entry, did);
                changed = true;
            }
        }
        else
        {
            foreach (var proto in StorageAccess.AggregateLiquid(world, Pos))
            {
                if (litres <= 0)
                    break;
                var room = StorageAccess.RoomOnCell(world, cell, proto);
                var want = Math.Min(room, Math.Min(proto.StackSize, ItemsForLitres(proto, litres)));
                if (want <= 0)
                    continue;
                var got = StorageAccess.ExtractLiquid(world, Pos, proto, want);
                if (got == null || got.StackSize <= 0)
                    continue;
                Merge(world, entries, got);
                StorageAccess.WriteCell(cell, entries);
                litres -= LitresOf(got, got.StackSize);
                changed = true;
            }
        }

        if (!changed)
            return;
        if (IntoNetwork)
            StorageAccess.WriteCell(cell, entries);
        slot.MarkDirty();
    }

    private static void Merge(IWorldAccessor world, System.Collections.Generic.List<ItemStack> entries, ItemStack got)
    {
        foreach (var entry in entries)
        {
            if (!StorageAccess.Same(world, entry, got))
                continue;
            entry.StackSize += got.StackSize;
            return;
        }

        entries.Add(got.Clone());
    }

    private static int ItemsForLitres(ItemStack stack, int litres)
    {
        var props = StorageAccess.LiquidProps(stack);
        if (props == null || litres <= 0)
            return 0;
        return Math.Max(1, (int)Math.Round(litres * props.ItemsPerLitre));
    }

    private static int LitresOf(ItemStack stack, int items)
    {
        var props = StorageAccess.LiquidProps(stack);
        if (props == null || items <= 0 || props.ItemsPerLitre <= 0)
            return 0;
        return Math.Max(1, (int)Math.Ceiling(items / props.ItemsPerLitre - 1e-4));
    }

    public Size2i AtlasSize => _capi!.BlockTextureAtlas.Size;

    public TextureAtlasPosition this[string textureCode]
    {
        get
        {
            AssetLocation? assetLocation = null;
            if (_nowTesselatingObj is Vintagestory.API.Common.Item item && item.Textures != null)
            {
                if (item.Textures.TryGetValue(textureCode, out var compositeTexture))
                    assetLocation = compositeTexture?.Baked?.BakedName;
                else if (item.Textures.TryGetValue("all", out compositeTexture))
                    assetLocation = compositeTexture?.Baked?.BakedName;
            }

            if (assetLocation == null)
                _nowTesselatingShape?.Textures?.TryGetValue(textureCode, out assetLocation);

            if (assetLocation == null)
            {
                var domain = _nowTesselatingObj?.Code?.Domain ?? "electricalprogressivestorage";
                assetLocation = new AssetLocation(domain, "textures/item/" + textureCode);
            }

            return TexPos(assetLocation);
        }
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        base.OnTesselation(mesher, tessThreadTesselator);
        if (_mesh != null)
            mesher.AddMeshData(_mesh);
        return false;
    }

    private void UpdateMesh()
    {
        if (_capi == null)
            return;
        MeshData? combined = null;
        var count = Math.Min(_inventory.Count, WindowX.Length);
        for (var i = 0; i < count; i++)
        {
            if (_inventory[i].Empty)
                continue;

            var part = GenMesh(_inventory[i]);
            if (part == null)
                continue;

            PlaceInWindow(part, Block, WindowX[i], WindowY[i]);
            if (combined == null)
                combined = part;
            else
                combined.AddMeshData(part);
        }

        _mesh = combined;
    }

    private static void PlaceInWindow(MeshData mesh, Vintagestory.API.Common.Block? block, float centerX, float centerY)
    {
        var origin = new Vec3f(0.5f, 0.5f, 0.5f);
        const float card = 5.5f / 15.2f;
        mesh.Scale(origin, card, card, card);
        var faceZ = 9.6f / 16f;
        var faceLocal = 0.5f + (faceZ - 0.5f) * card;
        mesh.Translate(centerX / 16f - 0.5f, centerY / 16f - 0.5f, 15.8f / 16f - faceLocal);
        var yaw = FaceYaw(block);
        if (yaw != 0)
            mesh.Rotate(origin, 0f, yaw * GameMath.DEG2RAD, 0f);
    }

    private static int FaceYaw(Vintagestory.API.Common.Block? block)
    {
        string? code = null;
        if (block?.Variant != null)
        {
            if (!block.Variant.TryGetValue("horizontalorientation", out code))
                block.Variant.TryGetValue("side", out code);
        }

        return code switch
        {
            "east" => 270,
            "south" => 180,
            "west" => 90,
            _ => 0
        };
    }

    private MeshData? GenMesh(ItemSlot slot)
    {
        var stack = slot.Itemstack;
        if (stack?.Item == null || _capi == null)
            return null;

        try
        {
            _nowTesselatingObj = stack.Collectible;
            _nowTesselatingShape = stack.Item.Shape == null
                ? null
                : _capi.TesselatorManager.GetCachedShape(stack.Item.Shape.Base);
            _capi.Tesselator.TesselateItem(stack.Item, out var mesh, this);
            if (mesh == null || mesh.VerticesCount == 0 || mesh.RenderPassesAndExtraBits == null)
                return null;
            var pass = (short)EnumChunkRenderPass.OpaqueNoCull;
            for (var i = 0; i < mesh.RenderPassesAndExtraBits.Length; i++)
                mesh.RenderPassesAndExtraBits[i] = pass;
            return mesh;
        }
        catch (Exception e)
        {
            Api.World.Logger.Error("Не удалось нарисовать предмет в порте {0}: {1}", stack.Item.Code, e.Message);
            return null;
        }
    }

    private TextureAtlasPosition TexPos(AssetLocation texturePath)
    {
        var capi = _capi ?? throw new InvalidOperationException();
        var found = capi.BlockTextureAtlas[texturePath];
        if (found != null)
            return found;

        var path = texturePath.Clone();
        var cut = path.Path.IndexOf("++", StringComparison.Ordinal);
        if (cut >= 0)
            path.Path = path.Path.Substring(0, cut);

        TextureAtlasPosition? inserted = null;
        var asset = capi.Assets.TryGet(path.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
        if (asset != null)
        {
            capi.BlockTextureAtlas.GetOrInsertTexture(path, out _, out var placed, null, 0.005f);
            inserted = placed;
        }

        return inserted ?? capi.BlockTextureAtlas.UnknownTexturePosition;
    }
}

/// <summary>
/// Окно четырёх слотов и кнопка направления.
/// </summary>
public class GuiDialogEStorageIoPort : GuiDialogBlockEntity
{
    private ElementBounds? _close;
    private double _winW;
    private double _head;
    private bool _drag;
    private int _dragX;
    private int _dragY;
    private bool _into = true;

    public GuiDialogEStorageIoPort(string dialogTitle, InventoryBase inventory, BlockPos pos, ICoreClientAPI capi)
        : base(dialogTitle, inventory, pos, capi)
    {
        if (IsDuplicate)
            return;

        if (capi.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityEStorageIoPort port)
            _into = port.IntoNetwork;
        capi.World.Player.InventoryManager.OpenInventory(inventory);
        SetupDialog();
    }

    public override void OnRenderGUI(float deltaTime)
    {
        if (capi.World.BlockAccessor.GetBlockEntity(BlockEntityPosition) is BlockEntityEStorageIoPort port
            && port.IntoNetwork != _into)
        {
            _into = port.IntoNetwork;
            SetupDialog();
        }

        base.OnRenderGUI(deltaTime);
    }

    public override void OnMouseDown(MouseEvent args)
    {
        if (_close != null && _close.PointInside(capi.Input.MouseX, capi.Input.MouseY))
        {
            args.Handled = true;
            CloseIconPressed();
            return;
        }

        base.OnMouseDown(args);
        if (!args.Handled && OnFrame())
            BeginDrag();
    }

    public override void OnMouseMove(MouseEvent args)
    {
        if (_drag && SingleComposer != null)
        {
            var bounds = SingleComposer.Bounds;
            bounds.fixedX += (args.X - _dragX) / RuntimeEnv.GUIScale;
            bounds.fixedY += (args.Y - _dragY) / RuntimeEnv.GUIScale;
            _dragX = args.X;
            _dragY = args.Y;
            bounds.CalcWorldBounds();
        }

        base.OnMouseMove(args);
    }

    public override void OnMouseUp(MouseEvent args)
    {
        if (_drag && SingleComposer != null)
            capi.Gui.SetDialogPosition(SingleComposer.DialogName, new Vec2i((int)SingleComposer.Bounds.fixedX, (int)SingleComposer.Bounds.fixedY));
        _drag = false;
        base.OnMouseUp(args);
    }

    private void BeginDrag()
    {
        var bounds = SingleComposer?.Bounds;
        if (bounds == null)
            return;

        _drag = true;
        _dragX = capi.Input.MouseX;
        _dragY = capi.Input.MouseY;
        if (bounds.Alignment == EnumDialogArea.None)
            return;

        bounds.Alignment = EnumDialogArea.None;
        bounds.fixedOffsetX = 0;
        bounds.fixedOffsetY = 0;
        bounds.fixedX = bounds.absX / RuntimeEnv.GUIScale;
        bounds.fixedY = bounds.absY / RuntimeEnv.GUIScale;
        bounds.absMarginX = 0;
        bounds.absMarginY = 0;
        bounds.MarkDirtyRecursive();
        bounds.CalcWorldBounds();
    }

    private bool OnFrame()
    {
        var bounds = SingleComposer?.Bounds;
        if (bounds == null || !bounds.PointInside(capi.Input.MouseX, capi.Input.MouseY))
            return false;

        var scale = RuntimeEnv.GUIScale;
        var x = (capi.Input.MouseX - bounds.renderX) / scale;
        var y = (capi.Input.MouseY - bounds.renderY) / scale;
        return y >= 0 && y < _head && x >= 0 && x <= _winW;
    }

    private void SetupDialog()
    {
        const int columns = 2;
        var rows = Math.Max(1, (Inventory.Count + columns - 1) / columns);
        var cell = Ae2Gui.Slots(0, 0, columns, rows);
        _winW = Math.Max(240, cell.fixedWidth + Ae2Gui.Margin * 2);
        _head = Ae2Gui.Header;
        var slotY = Ae2Gui.Header + Ae2Gui.Gap;
        var grid = Ae2Gui.Slots((_winW - cell.fixedWidth) / 2, slotY, columns, rows);
        var indexes = new int[Inventory.Count];
        for (var i = 0; i < indexes.Length; i++)
            indexes[i] = i;
        var button = Ae2Gui.Box(Ae2Gui.Margin, slotY + cell.fixedHeight + Ae2Gui.Gap, _winW - Ae2Gui.Margin * 2, 28);
        var hint = Ae2Gui.Box(Ae2Gui.Margin, button.fixedY + button.fixedHeight + 6, _winW - Ae2Gui.Margin * 2, 22);
        var winH = hint.fixedY + hint.fixedHeight + Ae2Gui.Margin;
        _close = Ae2Gui.Box(_winW - Ae2Gui.Margin - 20, 11, 20, 20);
        var title = Ae2Gui.Box(Ae2Gui.Margin + 2, 8, _winW - 70, 26);
        var frame = ElementBounds.Fixed(0, 0, _winW, winH);
        var bg = ElementBounds.Fill.WithFixedPadding(0);
        bg.BothSizing = ElementSizing.FitToChildren;
        bg.WithChildren(frame, _close);

        var direction = _into
            ? "electricalprogressivestorage:estorage-ioport-in"
            : "electricalprogressivestorage:estorage-ioport-out";
        var sheet = Ae2Gui.Window(capi, frame, _close.fixedX, _close.fixedY, _close.fixedWidth);

        ClearComposers();
        SingleComposer = capi.Gui
            .CreateCompo("estorageioport" + BlockEntityPosition, Ae2Gui.Dialog())
            .BeginChildElements(bg)
            .AddStaticElement(sheet)
            .AddStaticText(DialogTitle, Ae2Gui.Title(), title)
            .AddSlots(capi, Inventory, DoSendPacket, columns, indexes, grid, "cell")
            .AddInteractiveElement(new GuiElementAe2Button(capi, button, Lang.Get(direction), () =>
                capi.Network.SendBlockEntityPacket(BlockEntityPosition, BlockEntityEStorageIoPort.TogglePacket, Array.Empty<byte>())), "dir")
            .AddStaticText(Lang.Get("electricalprogressivestorage:estorage-ioport-hint"), Ae2Gui.Label(), hint)
            .EndChildElements()
            .Compose();
    }
}
