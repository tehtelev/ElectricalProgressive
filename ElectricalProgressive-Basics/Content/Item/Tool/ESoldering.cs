using System.Text;
using ElectricalProgressive.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Item.Tool;

class ESoldering : Vintagestory.API.Common.Item
{
    int consume;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        consume = MyMiniLib.GetAttributeInt(this, "consume", 20);

    }


    private static SimpleParticleProperties dustParticles; //частицы пыли

    static ESoldering()
    {
       
    }

    


    /// <summary>
    /// Уменьшаем прочность
    /// </summary>
    /// <param name="world"></param>
    /// <param name="byEntity"></param>
    /// <param name="itemslot"></param>
    /// <param name="amount"></param>
    public override void DamageItem(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, int amount = 1, bool destroyOnZeroDurability=false)
    {
        var durability = itemslot.Itemstack.Attributes.GetInt("durability");
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

        var energy = inSlot.Itemstack.Attributes.GetInt("durability") * consume; //текущая энергия
        var maxEnergy = inSlot.Itemstack.Collectible.GetMaxDurability(inSlot.Itemstack) * consume;       //максимальная энергия

        dsc.AppendLine(energy + "/" + maxEnergy + " " + Lang.Get("electricalprogressivebasics:J"));
    }



}