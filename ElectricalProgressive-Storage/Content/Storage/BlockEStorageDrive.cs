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

public class BlockEntityEStorageDrive : BlockEntityOpenableContainer
{
    private readonly InventoryGeneric _inventory;

    public BlockEntityEStorageDrive()
    {
        _inventory = new InventoryGeneric(8, "estoragedrive", "0", null, (_, inv) => new ItemSlotDisk(inv));
        _inventory.SlotModified += _ => StorageAccess.TouchContents();
    }

    public override InventoryBase Inventory => _inventory;

    public override string InventoryClassName => "estoragedrive";

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        KeepFormerSlots(tree, worldAccessForResolve);
    }

    /// <summary>
    /// Раньше слотов было десять. Девятый и десятый переносятся в свободную ячейку, иначе падают у блока.
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
        base.Initialize(api);
        _inventory.LateInitialize("estoragedrive-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z, api);
        if (api.Side == EnumAppSide.Server)
        {
            // Привод часто поднимается после терминала. Без этого терминал так и хранит пустой обход.
            StorageAccess.DirtyTopology();
            StorageAccess.TouchContents();
        }
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
