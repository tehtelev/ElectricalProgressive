using System.Text;
using ElectricalProgressive.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Item.Armor;

class EArmor : ItemWearable
{
    public int consume;
    public int consumefly;
    public float flySpeed;


    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        consume = MyMiniLib.GetAttributeInt(this, "consume", 20);
        consumefly = MyMiniLib.GetAttributeInt(this, "consumeFly", 40);
        flySpeed = MyMiniLib.GetAttributeFloat(this, "speedFly", 2.0F);
    }


    /// <summary>
    /// Уменьшаем прочность
    /// </summary>
    /// <param name="world"></param>
    /// <param name="byEntity"></param>
    /// <param name="itemslot"></param>
    /// <param name="amount"></param>
    public override void DamageItem(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, int amount = 1)
    {
        int durability = itemslot.Itemstack.Attributes.GetInt("durability");
        if (durability >= amount)
        {
            durability -= amount;
            itemslot.Itemstack.Attributes.SetInt("durability", durability);
        }
        else
        {
            durability = 0;
            itemslot.Itemstack.Attributes.SetInt("durability", durability);
        }

        itemslot.MarkDirty();
    }



    /// <summary>
    /// Информация о предмете
    /// </summary>
    /// <param name="inSlot"></param>
    /// <param name="dsc"></param>
    /// <param name="world"></param>
    /// <param name="withDebugInfo"></param>
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

        int energy = inSlot.Itemstack.Attributes.GetInt("durability") * consume; //текущая энергия
        int maxEnergy = inSlot.Itemstack.Collectible.GetMaxDurability(inSlot.Itemstack) * consume;       //максимальная энергия
        dsc.AppendLine(energy + "/" + maxEnergy + " " + Lang.Get("J"));
    }

   
}