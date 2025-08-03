using System.Text;
using ElectricalProgressive.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ElectricalProgressive.Content.Item.Armor;

class EShield : Vintagestory.API.Common.Item
{
    int consume;


    
    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        consume = MyMiniLib.GetAttributeInt(this, "consume", 20);

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
        if (durability > amount)
        {
            durability -= amount;
            itemslot.Itemstack.Attributes.SetInt("durability", durability);
        }
        else
        {
            durability = 1;
            itemslot.Itemstack.Attributes.SetInt("durability", durability);
        }

        itemslot.MarkDirty();
    }



    public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
    {
        string str1 = byEntity.LeftHandItemSlot == slot ? "left" : "right";
        string str2 = byEntity.LeftHandItemSlot == slot ? "right" : "left";
        if (byEntity.Controls.Sneak && !byEntity.Controls.RightMouseDown)
        {
            if (!byEntity.AnimManager.IsAnimationActive("raiseshield-" + str1))
                byEntity.AnimManager.StartAnimation("raiseshield-" + str1);
        }
        else if (byEntity.AnimManager.IsAnimationActive("raiseshield-" + str1))
            byEntity.AnimManager.StopAnimation("raiseshield-" + str1);
        if (byEntity.AnimManager.IsAnimationActive("raiseshield-" + str2))
            byEntity.AnimManager.StopAnimation("raiseshield-" + str2);

        int durability = slot.Itemstack.Attributes.GetInt("durability");

        if (durability > 1)
            slot.Itemstack.Item.LightHsv = new byte[3] {   7,     3,   20  };
        else if (durability <= 1)
            slot.Itemstack.Item.LightHsv = new byte[3] { 0, 0, 0 } ;

        base.OnHeldIdle(slot, byEntity);
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

        JsonObject itemAttribute = inSlot.Itemstack?.ItemAttributes?["eshield"]!;

        if (itemAttribute == null || !itemAttribute.Exists)
            return;

        if (itemAttribute["protectionChance"]["active-projectile"].Exists)
        {
            float num1 = itemAttribute["protectionChance"]["active-projectile"].AsFloat();
            float num2 = itemAttribute["protectionChance"]["passive-projectile"].AsFloat();
            float num3 = itemAttribute["projectileDamageAbsorption"].AsFloat();
            dsc.AppendLine("<strong>" + Lang.Get("Projectile protection") + "</strong>");
            dsc.AppendLine(Lang.Get("shield-stats",  (int) (100.0 * (double) num1),  (int) (100.0 * (double) num2),  num3));
            dsc.AppendLine();
        }
        float num4 = itemAttribute["damageAbsorption"].AsFloat();
        float num5 = itemAttribute["protectionChance"]["active"].AsFloat();
        float num6 = itemAttribute["protectionChance"]["passive"].AsFloat();
        dsc.AppendLine("<strong>" + Lang.Get("Melee attack protection") + "</strong>");
        dsc.AppendLine(Lang.Get("shield-stats",  (int) (100.0 * (double) num5),  (int) (100.0 * (double) num6),  num4));
        dsc.AppendLine();

        int energy = inSlot.Itemstack!.Attributes.GetInt("durability") * consume; //текущая энергия
        int maxEnergy = inSlot.Itemstack!.Collectible.GetMaxDurability(inSlot.Itemstack) * consume;       //максимальная энергия
        dsc.AppendLine(energy + "/" + maxEnergy + " " + Lang.Get("J"));

    }






}