using System.Text;
using Vintagestory.API.Common;

namespace ElectricalProgressive.Utils;

/// <summary>
/// Сетка блока зависит от того, какой предмет лежит, а не от размера стака.
/// По этому ключу решаем, нужна ли перерисовка чанка.
/// </summary>
public static class InventoryVisual
{
    public static bool Changed(ref string? cache, InventoryBase? inventory)
    {
        var key = Key(inventory);
        if (cache == key)
            return false;

        cache = key;
        return true;
    }

    public static string Key(InventoryBase? inventory)
    {
        if (inventory == null)
            return "";

        var sb = new StringBuilder(inventory.Count * 8);
        for (var i = 0; i < inventory.Count; i++)
        {
            if (i > 0)
                sb.Append('|');
            sb.Append(inventory[i]?.Itemstack?.Collectible?.Code?.ToString());
        }

        return sb.ToString();
    }
}
