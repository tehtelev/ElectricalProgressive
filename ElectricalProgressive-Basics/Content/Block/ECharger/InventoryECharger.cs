using ElectricalProgressive.Interface;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ElectricalProgressive.Content.Block.ECharger;
public class InventoryECharger : InventoryGeneric
{


    public InventoryECharger(ICoreAPI api)
      : base(api)
    {
      
    }

    public InventoryECharger(int slots, string className, string instanceID, ICoreAPI api, NewSlotDelegate onNewSlot)
      : base(slots, className, instanceID, api)
    {
       
    }



    /// <summary>
    /// Автозагрузка духовки
    /// </summary>
    /// <param name="atBlockFace"></param>
    /// <param name="fromSlot"></param>
    /// <returns></returns>
    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        return this[0];
    }



    /// <summary>
    /// Автопулл из духовки
    /// </summary>
    /// <param name="atBlockFace"></param>
    /// <returns></returns>
    public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
    {
        var working = false;
        int durability;         //текущая прочность
        int maxDurability;      //максимальная прочность


        var entityStack = this[0].Itemstack;

        // со стаком что - то не так? выкидываем
        if (entityStack is null ||
            entityStack.StackSize == 0 ||
            entityStack.Collectible == null ||
            entityStack.Collectible.Attributes == null)
            return this[0];

        if (entityStack.Item != null &&
            entityStack.Collectible.Attributes["chargable"].AsBool(false)) //предмет?
        {
            durability = entityStack.Attributes.GetInt("durability");
            maxDurability = entityStack.Collectible.GetMaxDurability(entityStack);
            working = durability < maxDurability;
        }
        else if (entityStack.Block is IEnergyStorageItem) //блок?
        {
            durability = entityStack.Attributes.GetInt("durability");
            maxDurability = entityStack.Collectible.GetMaxDurability(entityStack);
            working = durability < maxDurability;
        }

        // если не работает, то выкидываем предмет
        if (!working)
            return this[0];


        return null!;
    }

}
