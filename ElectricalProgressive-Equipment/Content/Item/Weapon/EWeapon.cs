using System;
using System.Text;
using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Item.Weapon;

public class EWeapon : Vintagestory.API.Common.Item
{
    int consume;
    int fireCost;

    private double lastUpdateTime = 0;
    private const double interval = 5000; //интервал обновления меча

   

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        CollectibleBehaviorAnimationAuthoritative collectibleBehaviorAnimationAuthoritative = GetCollectibleBehavior<CollectibleBehaviorAnimationAuthoritative>(withInheritance: true);
        if (collectibleBehaviorAnimationAuthoritative == null)
        {
            api.World.Logger.Warning("Spear {0} uses ItemSpear class, but lacks required AnimationAuthoritative behavior. I'll take the freedom to add this behavior, but please fix json item type.", Code);
            collectibleBehaviorAnimationAuthoritative = new CollectibleBehaviorAnimationAuthoritative(this);
            collectibleBehaviorAnimationAuthoritative.OnLoaded(api);
            CollectibleBehaviors = CollectibleBehaviors.Append(collectibleBehaviorAnimationAuthoritative);
        }

        collectibleBehaviorAnimationAuthoritative.OnBeginHitEntity += EWeapon_OnBeginHitEntity;
        

        consume = MyMiniLib.GetAttributeInt(this, "consume", 20);
        fireCost = MyMiniLib.GetAttributeInt(this, "fireCost", 0);

    }





    

    /// <summary>
    /// Обновление меча в руке
    /// </summary>
    /// <param name="world"></param>
    /// <param name="stack"></param>
    public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
    {

        double currentTime = api.World.ElapsedMilliseconds;
        if (slot.Itemstack.Item.Variant["type"] == "hot" && currentTime - lastUpdateTime >= interval)
        {
            
            DamageItem(api.World, byEntity, slot);
            lastUpdateTime = currentTime;

            if (slot.Itemstack.Attributes.GetInt("durability")<=1) //тушим
            {
                var newItem = api.World.GetItem(new AssetLocation("electricalprogressiveequipment", "static-saber-common"));
                var newStack = new ItemStack(newItem);
                if (slot.Itemstack.Attributes != null)
                {
                    newStack.Attributes = slot.Itemstack.Attributes.Clone();
                }

                slot.Itemstack = newStack;
                
            }

            slot.MarkDirty();

        }


    }

    public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
    {
        int durability = slot.Itemstack.Attributes.GetInt("durability");

        if (durability <= 1)
        {
            handling = EnumHandHandling.PreventDefault;
            return;
        }


        base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);


    }


    /// <summary>
    /// Пока на земле
    /// </summary>
    /// <param name="entityItem"></param>
    public override void OnGroundIdle(EntityItem entityItem)
    {
        base.OnGroundIdle(entityItem);
        double currentTime = api.World.ElapsedMilliseconds;
        if (entityItem.Itemstack.Item.Variant["type"] == "hot" &&  currentTime - lastUpdateTime >= interval)
        {
                        
            var slot = new DummySlot();
            slot.Itemstack = entityItem.Itemstack; //связываем их
            DamageItem(api.World, null!, slot);
            slot.MarkDirty();
            entityItem.Itemstack = slot.Itemstack;            

            lastUpdateTime = currentTime;

            if (entityItem.Itemstack.Attributes.GetInt("durability") <= 1) //тушим
            {
                var newItem = api.World.GetItem(new AssetLocation("electricalprogressiveequipment", "static-saber-common"));
                var newStack = new ItemStack(newItem);
                if (entityItem.Itemstack.Attributes != null)
                {
                    newStack.Attributes = entityItem.Itemstack.Attributes.Clone();
                }

                entityItem.Itemstack = newStack; // Обновляем предмет сущности

            }
                        
        }
       
    }

    /// <summary>
    /// Попал по противнику
    /// </summary>
    /// <param name="byEntity"></param>
    /// <param name="handling"></param>
    private void EWeapon_OnBeginHitEntity(EntityAgent byEntity, ref EnumHandling handling)
    {
        if (byEntity.World.Side == EnumAppSide.Client)
        {
            return;
        }

        EntitySelection? entitySelection = (byEntity as EntityPlayer)?.EntitySelection;
        //меч горит и противник живой
        if (entitySelection != null && entitySelection.Entity.Alive && this.Variant["type"] == "hot")
        {
            entitySelection.Entity.IsOnFire = true;
        }
    }


    public override string GetHeldTpUseAnimation(ItemSlot activeHotbarSlot, Entity byEntity)
    {
        return null!;
    }



    public override bool OnHeldAttackCancel(float secondsPassed, ItemSlot slot, EntityAgent byEntity,
        BlockSelection blockSelection, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
    {
        return false;
    }


    public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
    {
        return secondsUsed < 2.0F || secondsUsed > 2.1F;
    }


    /// <summary>
    /// Левая кнопка мыши зажата
    /// </summary>
    /// <param name="secondsPassed"></param>
    /// <param name="slot"></param>
    /// <param name="byEntity"></param>
    /// <param name="blockSelection"></param>
    /// <param name="entitySel"></param>
    /// <returns></returns>
    public override bool OnHeldAttackStep(float secondsPassed, ItemSlot slot, EntityAgent byEntity,
       BlockSelection blockSelection, EntitySelection entitySel)
    {

        
        secondsPassed *= 1.25f;

        float backwards = -Math.Min(0.35f, 2 * secondsPassed);
        float stab = Math.Min(1.2f, 20 * Math.Max(0, secondsPassed - 0.35f));

        if (byEntity.World.Side == EnumAppSide.Client)
        {
            IClientWorldAccessor? world = byEntity.World as IClientWorldAccessor;

            int energy = slot.Itemstack.Attributes.GetInt("durability")* consume;

            if (stab > 1.15f && byEntity.Attributes.GetInt("didattack") == 0 && energy > consume)
            {
                world!.TryAttackEntity(entitySel);
                byEntity.Attributes.SetInt("didattack", 1);
                world.AddCameraShake(0.25f);
            }
        }



        return secondsPassed < 1.2f;

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


    public override void OnHeldAttackStop(float secondsPassed, ItemSlot slot, EntityAgent byEntity,
        BlockSelection blockSelection, EntitySelection entitySel)
    {

    }




    /// <summary>
    /// Удерживаем правую кнопку мыши
    /// </summary>
    /// <param name="secondsUsed"></param>
    /// <param name="slot"></param>
    /// <param name="byEntity"></param>
    /// <param name="blockSel"></param>
    /// <param name="entitySel"></param>
    public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
    {

        //время удерживания по сути
        if (secondsUsed < 2.0f || secondsUsed > 2.1F)
        {
            return;
        }

        byEntity.StopAnimation("helditemready");

        ItemStack EW = slot.Itemstack;

        //зажигаем
        int energy = EW.Attributes.GetInt("durability")* consume;
        if (EW.Item.Variant["type"] != "hot")
        {
            //хватает заряда?            
            if (energy > fireCost)
            {
                energy -= fireCost;

                EW.Attributes.SetInt("durability", Math.Max(1, energy / consume));

                slot.MarkDirty();
            }
            else
            {
                return;
            }



            var newItem = api.World.GetItem(new AssetLocation("electricalprogressiveequipment", "static-saber-hot"));
            var newStack = new ItemStack(newItem);
            if (EW.Attributes != null)
            {
                newStack.Attributes = EW.Attributes.Clone();
            }

            slot.Itemstack = newStack;
            slot.MarkDirty();

            if (byEntity.World.Side == EnumAppSide.Client)
            {
                byEntity.World.PlaySoundAt(new AssetLocation("game:sounds/effect/swoosh.ogg"), byEntity, null, false);
            }
        }
        else //тушим
        {
            var newItem = api.World.GetItem(new AssetLocation("electricalprogressiveequipment", "static-saber-common"));
            var newStack = new ItemStack(newItem);
            if (EW.Attributes != null)
            {
                newStack.Attributes = EW.Attributes.Clone();
            }

            slot.Itemstack = newStack;
            slot.MarkDirty();
        }



    }



    /// <summary>
    /// Нажал правую кнопку мыши
    /// </summary>
    /// <param name="itemslot"></param>
    /// <param name="byEntity"></param>
    /// <param name="blockSel"></param>
    /// <param name="entitySel"></param>
    /// <param name="firstEvent"></param>
    /// <param name="handling"></param>
    public override void OnHeldInteractStart(ItemSlot itemslot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        base.OnHeldInteractStart(itemslot, byEntity, blockSel, entitySel, firstEvent, ref handling);
        if (handling != EnumHandHandling.PreventDefault)
        {
            handling = EnumHandHandling.PreventDefault;
            byEntity.StartAnimation("helditemready");
        }

    }


    /// <summary>
    /// Отпустил правую кнопку мыши
    /// </summary>
    /// <param name="secondsUsed"></param>
    /// <param name="slot"></param>
    /// <param name="byEntity"></param>
    /// <param name="blockSel"></param>
    /// <param name="entitySel"></param>
    /// <param name="cancelReason"></param>
    /// <returns></returns>
    public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
    {
        return true;
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



    /// <summary>
    /// Получаем помощь по взаимодействию с предметом в руке
    /// </summary>
    /// <param name="inSlot"></param>
    /// <returns></returns>
    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
    {
        return new WorldInteraction[] {
                new WorldInteraction()
                {
                    ActionLangCode = "saber_right",
                    MouseButton = EnumMouseButton.Right,
                }
            }.Append(base.GetHeldInteractionHelp(inSlot));
    }
}