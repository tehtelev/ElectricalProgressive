using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Storage.Drive;

/// <summary>
/// Пустой диск или ячейка отдаёт корпус и компонент того же объёма.
/// </summary>
internal static class EmptyHousingSplit
{
    public static bool CanCraft(ItemStack? input, IRecipeIngredient? ingredient, bool liquid)
        => ingredient?.Name != "split" || input == null || !Holds(input, liquid);

    public static void OnHeld(ItemSlot slot, EntityAgent byEntity, bool firstEvent, ref EnumHandHandling handling, bool liquid)
    {
        if (!firstEvent)
            return;

        var held = slot.Itemstack;
        if (held == null || Holds(held, liquid))
            return;

        var tier = held.Collectible.Variant?["tier"];
        if (string.IsNullOrEmpty(tier))
            tier = held.Collectible.Code.EndVariant();
        if (string.IsNullOrEmpty(tier))
            return;

        var world = byEntity.World;
        var housingCode = liquid
            ? "electricalprogressivestorage:estoragecellhousing"
            : "electricalprogressivestorage:estoragediskhousing";
        var housing = world.GetItem(new AssetLocation(housingCode));
        var partCode = liquid ? "electricalprogressivestorage:estoragecellcomponent-" : "electricalprogressivestorage:estoragecomponent-";
        var part = world.GetItem(new AssetLocation(partCode + tier));
        if (housing == null || part == null)
            return;

        // Пакет уходит на сервер только если клиент поставил PreventDefault.
        // На сервере присед к этому моменту может быть ещё false.
        if (world.Side != EnumAppSide.Server)
        {
            if (byEntity.Controls.ShiftKey != true && byEntity.Controls.Sneak != true)
                return;
            handling = EnumHandHandling.PreventDefault;
            return;
        }

        if (byEntity is not EntityPlayer player)
            return;

        handling = EnumHandHandling.PreventDefault;
        Give(world, player.Player, new ItemStack(housing, 1));
        Give(world, player.Player, new ItemStack(part, 1));
        slot.TakeOut(1);
        slot.MarkDirty();
    }

    private static bool Holds(ItemStack stack, bool liquid)
    {
        var key = liquid ? StorageAccess.CellStacksKey : StorageAccess.StacksKey;
        var tree = stack.Attributes?.GetTreeAttribute(key);
        if (tree == null)
            return false;

        foreach (var pair in tree)
        {
            if (pair.Value is ItemstackAttribute { value.StackSize: > 0 })
                return true;
        }

        return false;
    }

    private static void Give(IWorldAccessor world, IPlayer player, ItemStack stack)
    {
        if (player.InventoryManager?.TryGiveItemstack(stack, true) == true && stack.StackSize <= 0)
            return;
        if (stack.StackSize <= 0 || player.Entity == null)
            return;

        world.SpawnItemEntity(stack, player.Entity.Pos.XYZ);
    }
}
