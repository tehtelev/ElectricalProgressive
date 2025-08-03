using System;
using System.Drawing;
using System.Text;
using ElectricalProgressive.Utils;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Content.Item.Weapon;

public class ESpear : Vintagestory.API.Common.Item
{
    int consume;
    int lightstrike;
    public SkillItem[] toolModes = Array.Empty<SkillItem>();

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

        lightstrike= MyMiniLib.GetAttributeInt(this, "lightstrike", 2000);
        consume = MyMiniLib.GetAttributeInt(this, "consume", 20);


        ICoreClientAPI? capi = api as ICoreClientAPI;
        if (capi == null)
            return;

        //задаем режимы копья
        toolModes = ObjectCacheUtil.GetOrCreate(api, "spearToolModes", () => new SkillItem[2]
        {
            new SkillItem
            {
                Code = new AssetLocation("1size"),
                Name = Lang.Get("spear_common")
            }.WithIcon(capi, capi.Gui.LoadSvg(new AssetLocation("electricalprogressiveequipment:textures/icons/spear-common.svg"),252,256,252,256,ColorUtil.WhiteArgb)),
            new SkillItem
            {
                Code = new AssetLocation("3size"),
                Name = Lang.Get("spear_flash")
            }.WithIcon(capi, capi.Gui.LoadSvg(new AssetLocation("electricalprogressiveequipment:textures/icons/spear-flash.svg"), 252, 266, 252, 266, ColorUtil.WhiteArgb))
        });
    }





    public override SkillItem[] GetToolModes(ItemSlot slot, IClientPlayer forPlayer, BlockSelection blockSel)
    {
        return toolModes;
    }

    /// <summary>
    /// Берем выбранный режим копья
    /// </summary>
    /// <param name="slot"></param>
    /// <param name="byPlayer"></param>
    /// <param name="blockSel"></param>
    /// <returns></returns>
    public override int GetToolMode(ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel)
    {
        return slot.Itemstack.Attributes.GetInt("toolMode");
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        for (int index = 0; toolModes != null && index < toolModes.Length; ++index)
            toolModes[index]?.Dispose();
    }


    /// <summary>
    /// Игрок выбрал режим копья
    /// </summary>
    /// <param name="slot"></param>
    /// <param name="byPlayer"></param>
    /// <param name="blockSel"></param>
    /// <param name="toolMode"></param>
    public override void SetToolMode(
        ItemSlot slot,
        IPlayer byPlayer,
        BlockSelection blockSel,
        int toolMode)
    {
        ItemSlot mouseItemSlot = byPlayer.InventoryManager.MouseItemSlot;
        if (!mouseItemSlot.Empty && mouseItemSlot.Itemstack.Block != null)
        {
            api.Event.PushEvent("keepopentoolmodedlg");
        }
        else
            slot.Itemstack.Attributes.SetInt(nameof(toolMode), toolMode);
    }





    public override string GetHeldTpUseAnimation(ItemSlot activeHotbarSlot, Entity byEntity)
    {
        return null!;
    }
    public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
    {
        return true;
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


    public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
    {
        if (byEntity.Attributes.GetInt("aimingCancel") == 1)
        {
            return;
        }

        byEntity.Attributes.SetInt("aiming", 0);
        byEntity.StopAnimation("aim");

        //время прицеливания по сути
        if (secondsUsed < 0.5f)
        {
            return;
        }

        float damage = 1.5f;
        if (slot.Itemstack.Collectible.Attributes != null)
        {
            damage = slot.Itemstack.Collectible.Attributes["damage"].AsFloat();
        }

        (api as ICoreClientAPI)?.World.AddCameraShake(0.17f);

        IPlayer player = null!;
        if (byEntity is EntityPlayer)
        {
            player = byEntity.World.PlayerByUid(((EntityPlayer)byEntity).PlayerUID);
        }

        //берем режим копья
        bool can = false;
        if (GetToolMode(slot, player, blockSel) == 1)
            can = true;

        ItemStack projectileStack = slot.TakeOut(1);
        slot.MarkDirty();


        byEntity.World.PlaySoundAt(new AssetLocation("game:sounds/player/throw"), byEntity, player, randomizePitch: false, 8f);

        //берем проджектайл нашего копья и работаем с ним
        EntityProperties entityType = byEntity.World.GetEntityType(new AssetLocation("electricalprogressiveequipment:static-spear-projectile"));
        EntityESpear entityProjectile = (byEntity.World.ClassRegistry.CreateEntity(entityType) as EntityESpear)!;

        int energy = projectileStack.Attributes.GetInt("durability") * consume;
        
        //а заряда на обычный урон хватит?
        if (energy > consume)
            entityProjectile.Damage = damage;
        else
            entityProjectile.Damage = 0.0f;


        //а заряда на молнию хватит? режим инструмента?
        if (energy > lightstrike + consume && can)
        {
            entityProjectile.canStrike = true;
        }
        else
            entityProjectile.canStrike = false;




        entityProjectile.FiredBy = byEntity; 
        entityProjectile.DamageTier = Attributes["damageTier"].AsInt();
        entityProjectile.ProjectileStack = projectileStack;
        entityProjectile.DropOnImpactChance = 1.1f;
        entityProjectile.DamageStackOnImpact = true;
        entityProjectile.Weight = 0.3f;

        float num = 1f - byEntity.Attributes.GetFloat("aimingAccuracy");
        double num2 = byEntity.WatchedAttributes.GetDouble("aimingRandPitch", 1.0) * (double)num * 0.75;
        double num3 = byEntity.WatchedAttributes.GetDouble("aimingRandYaw", 1.0) * (double)num * 0.75;
        Vec3d vec3d = byEntity.ServerPos.XYZ.Add(0.0, byEntity.LocalEyePos.Y - 0.2, 0.0);
        Vec3d pos = (vec3d.AheadCopy(1.0, (double)byEntity.ServerPos.Pitch + num2, (double)byEntity.ServerPos.Yaw + num3) - vec3d) * 0.65 * byEntity.Stats.GetBlended("bowDrawingStrength");
        Vec3d posWithDimension = byEntity.ServerPos.BehindCopy(0.15).XYZ.Add(byEntity.LocalEyePos.X, byEntity.LocalEyePos.Y - 0.2, byEntity.LocalEyePos.Z);
        entityProjectile.ServerPos.SetPosWithDimension(posWithDimension);
        entityProjectile.ServerPos.Motion.Set(pos);
        entityProjectile.Pos.SetFrom(entityProjectile.ServerPos);
        entityProjectile.World = byEntity.World;
        entityProjectile.SetRotation();

        byEntity.World.SpawnEntity(entityProjectile);
        byEntity.StartAnimation("throw");
        if (byEntity is EntityPlayer)
        {
            RefillSlotIfEmpty(slot, byEntity, (ItemStack itemstack) => itemstack.Collectible is ESpear);
        }

        float pitchModifier = (byEntity as EntityPlayer)!.talkUtil.pitchModifier;
        player.Entity.World.PlaySoundAt(new AssetLocation("game:sounds/player/strike"), player.Entity, player, pitchModifier * 0.9f + (float)api.World.Rand.NextDouble() * 0.2f, 16f, 0.35f);
    }




    public override void OnHeldInteractStart(ItemSlot itemslot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
    {
        base.OnHeldInteractStart(itemslot, byEntity, blockSel, entitySel, firstEvent, ref handling);
        if (handling != EnumHandHandling.PreventDefault)
        {
            handling = EnumHandHandling.PreventDefault;
            byEntity.Attributes.SetInt("aiming", 1);
            byEntity.Attributes.SetInt("aimingCancel", 0);
            byEntity.StartAnimation("aim");
        }
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

            int energy = slot.Itemstack.Attributes.GetInt("durability") * consume;

            if (stab > 1.15f && byEntity.Attributes.GetInt("didattack") == 0 && energy > consume)
            {
                world!.TryAttackEntity(entitySel);
                byEntity.Attributes.SetInt("didattack", 1);
                world.AddCameraShake(0.25f);
            }
        }


        //return true;
        return secondsPassed < 1.2f;
        
    }

    public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
    {
        byEntity.Attributes.SetInt("aiming", 0);
        byEntity.StopAnimation("aim");
        if (cancelReason != 0)
        {
            byEntity.Attributes.SetInt("aimingCancel", 1);
        }

        return true;
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

        if (inSlot.Itemstack.Collectible.Attributes != null)
        {
            float num = 1.5f;
            if (inSlot.Itemstack.Collectible.Attributes != null)
            {
                num = inSlot.Itemstack.Collectible.Attributes["damage"].AsFloat();
            }

            dsc.AppendLine(num + Lang.Get("piercing-damage-thrown"));
        }
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
                    ActionLangCode = "heldhelp-throw",
                    MouseButton = EnumMouseButton.Right,
                }
            }.Append(base.GetHeldInteractionHelp(inSlot));
    }
}