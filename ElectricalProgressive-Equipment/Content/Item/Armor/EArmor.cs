using System.Text;
using ElectricalProgressive.Utils;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Item.Armor
{
    class EArmor : CollectibleBehaviorWearable
    {
        public int consume;

        
        public EArmor(CollectibleObject collObj) : base(collObj) { }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            consume = MyMiniLib.GetAttributeInt(collObj, "consume", 20);

        }

        public override void OnDamageItem(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, ref int amount, ref EnumHandling bhHandling)
        {
            // Обработка собственной прочности (энергии)
            var durability = itemslot.Itemstack.Attributes.GetInt("durability");
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
            bhHandling = EnumHandling.PreventDefault; // Предотвращаем стандартное уменьшение прочности
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            // Сначала вызываем базовый метод, чтобы добавить стандартную информацию о броне
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            // Добавляем информацию об энергии
            var energy = inSlot.Itemstack.Attributes.GetInt("durability") * consume;
            var maxEnergy = inSlot.Itemstack.Collectible.GetMaxDurability(inSlot.Itemstack) * consume;
            dsc.AppendLine(energy + "/" + maxEnergy + " " + Lang.Get("electricalprogressivebasics:J"));
        }
    }
}