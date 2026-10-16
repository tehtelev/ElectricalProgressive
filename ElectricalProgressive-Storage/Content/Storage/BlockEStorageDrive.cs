using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Storage;

public class BlockEStorageDrive : BlockEStoragePart
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (blockSel == null || !world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            return false;

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityEStorageDrive drive)
            return drive.OnPlayerRightClick(byPlayer, blockSel);

        return false;
    }
}

public class ItemSlotDisk : ItemSlot
{
    public ItemSlotDisk(InventoryBase inventory) : base(inventory)
    {
    }

    public override int MaxSlotStackSize => 1;

    public override bool CanHold(ItemSlot sourceSlot)
    {
        var item = sourceSlot.Itemstack?.Item;
        return item is ItemEStorageDisk || item is ItemEStorageCell;
    }
}

public class BlockEntityEStorageDrive : BlockEntityOpenableContainer, ITexPositionSource
{
    private readonly InventoryGeneric _inventory;
    private readonly MeshData?[] _meshes = new MeshData?[10];
    private readonly string?[] _shown = new string?[10];
    private ICoreClientAPI? _capi;
    private Shape? _nowTesselatingShape;
    private CollectibleObject? _nowTesselatingObj;

    public BlockEntityEStorageDrive()
    {
        _inventory = new InventoryGeneric(10, "estoragedrive", "0", null, (_, inv) => new ItemSlotDisk(inv));
        _inventory.SlotModified += slot =>
        {
            StorageAccess.TouchContents();
            if (_capi == null || Pos == null || slot < 0 || slot >= _shown.Length)
                return;

            // Содержимое диска меш ячейки не меняет. Перерисовка чанка мигает свечением соседей.
            var code = BayCode(slot);
            if (code == _shown[slot])
                return;

            _shown[slot] = code;
            UpdateMesh(slot);
            Api.World.BlockAccessor.MarkBlockDirty(Pos);
        };
    }

    public override InventoryBase Inventory => _inventory;

    public override string InventoryClassName => "estoragedrive";

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        KeepFormerSlots(tree, worldAccessForResolve);
    }

    /// <summary>
    /// Слот дальше десятого переносится в свободную ячейку, иначе падает у блока.
    /// </summary>
    private void KeepFormerSlots(ITreeAttribute tree, IWorldAccessor world)
    {
        if (tree?["inventory"] is not ITreeAttribute inv || inv["slots"] is not ITreeAttribute slots)
            return;

        foreach (var entry in slots)
        {
            if (!int.TryParse(entry.Key, out var index) || index < _inventory.Count)
                continue;
            if (entry.Value is not ItemstackAttribute { value: { } stack })
                continue;

            var kept = stack.Clone();
            if (world != null && !kept.ResolveBlockOrItem(world))
                continue;

            var placed = false;
            for (var i = 0; i < _inventory.Count; i++)
            {
                if (!_inventory[i].Empty)
                    continue;
                _inventory[i].Itemstack = kept;
                placed = true;
                break;
            }

            if (!placed && world != null && Pos != null)
                world.SpawnItemEntity(kept, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }
    }

    public override void Initialize(ICoreAPI api)
    {
        if (api is ICoreClientAPI capi)
            _capi = capi;
        base.Initialize(api);
        _inventory.LateInitialize("estoragedrive-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);
        if (api.Side == EnumAppSide.Server)
        {
            // Привод часто поднимается после терминала. Без этого терминал так и хранит пустой обход.
            StorageAccess.DirtyTopology();
            StorageAccess.TouchContents();
        }
        else
        {
            for (var i = 0; i < _meshes.Length; i++)
            {
                _shown[i] = BayCode(i);
                UpdateMesh(i);
            }
            api.World.BlockAccessor.MarkBlockDirty(Pos);
        }
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
        for (var i = 0; i < _meshes.Length; i++)
        {
            if (_meshes[i] != null)
                mesher.AddMeshData(_meshes[i]);
        }

        return false;
    }

    private string? BayCode(int slot)
        => _inventory[slot].Itemstack?.Collectible?.Code?.ToString();

    private void UpdateMesh(int slot)
    {
        if (_capi == null || slot < 0 || slot >= _meshes.Length)
            return;

        if (_inventory[slot].Empty)
        {
            _meshes[slot] = null;
            return;
        }

        var mesh = GenMesh(_inventory[slot]);
        if (mesh != null)
        {
            PlaceInBay(mesh, slot);
            var yaw = FaceYaw(Block);
            if (yaw != 0)
                mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, yaw * GameMath.DEG2RAD, 0f);
        }
        _meshes[slot] = mesh;
    }

    /// <summary>
    /// Лицо на юге (z 9.6). У диска корпус длинный и уходит до z −4.4: поворот Y 180 выводит лицо
    /// в северное окно, а зад садится около z 6.2, не доходя до задней панели (z 7).
    /// Высота 4.1 и ширина 15.2 при масштабе 1.58/4.1 дают в щели примерно 5.9×1.6. Лампа остаётся кубом.
    /// </summary>
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
            "east" => 90,
            "south" => 0,
            "west" => 270,
            _ => 180
        };
    }

    private static void PlaceInBay(MeshData mesh, int slot)
    {
        var origin = new Vec3f(0.5f, 0.5f, 0.5f);
        var row = slot / 2;
        var x = (slot % 2 == 0 ? 12f : 4f) / 16f;
        var y = (14f - row * 3f) / 16f;
        const float card = 1.58f / 4.1f;
        mesh.Scale(origin, card, card, card);
        mesh.Rotate(origin, 0f, 180f * GameMath.DEG2RAD, 0f);
        var faceZ = 9.6f / 16f;
        var zRot = 1f - (0.5f + (faceZ - 0.5f) * card);
        mesh.Translate(x - 0.5f, y - 0.5f, 0.85f / 16f - zRot);
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
            Api.World.Logger.Error("Не удалось нарисовать предмет в приводе {0}: {1}", stack.Item.Code, e.Message);
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
        else
            Api.World.Logger.Warning("Текстура привода не найдена: {0}", path);

        return inserted ?? capi.BlockTextureAtlas.UnknownTexturePosition;
    }

    public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (Api.Side == EnumAppSide.Client)
        {
            toggleInventoryDialogClient(byPlayer, () =>
                new GuiDialogEStorageDrive(
                    Lang.Get("electricalprogressivestorage:estorage-drive-title"),
                    Inventory,
                    Pos,
                    (ICoreClientAPI)Api));
        }

        return true;
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        StorageAccess.DirtyTopology();
    }
}
