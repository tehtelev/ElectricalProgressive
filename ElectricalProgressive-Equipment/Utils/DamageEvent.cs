using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;


#nullable disable
namespace ElectricalProgressive.Utils;

public class DamageEvent : ModSystem
{
  private ICoreAPI api;
  private ICoreClientAPI capi;
  
  public override bool ShouldLoad(EnumAppSide forSide) => true;

  public override void Start(ICoreAPI api) => this.api = api;

  public override void StartClientSide(ICoreClientAPI api)
  {
    base.StartClientSide(api);
    api.Event.LevelFinalize += new Action(this.Event_LevelFinalize);
    this.capi = api;
  }

  private void Event_LevelFinalize()
  {
    EntityBehaviorHealth behavior = this.capi.World.Player.Entity.GetBehavior<EntityBehaviorHealth>();
    if (behavior != null)
      this.capi.Logger.VerboseDebug("Done wearable stats");
  }

  public override void StartServerSide(ICoreServerAPI api)
  {
    base.StartServerSide(api);
    api.Event.PlayerJoin += new PlayerDelegate(this.Event_PlayerJoin);
  }

  private void Event_PlayerJoin(IServerPlayer byPlayer)
  {
    EntityBehaviorHealth behavior = byPlayer.Entity.GetBehavior<EntityBehaviorHealth>();
    if (behavior != null)
      behavior.onDamaged +=
        (OnDamagedDelegate)((dmg, dmgSource) => this.handleDamaged((IPlayer)byPlayer, dmg, dmgSource));
  }
  
  private float handleDamaged(IPlayer player, float damage, DamageSource dmgSource)
  {
    damage = this.applyShieldProtection(player, damage, dmgSource);
    return damage;
  }

    private float applyShieldProtection(IPlayer player, float damage, DamageSource dmgSource)
    {
      double num1 = 1.0471975803375244;
      float num2 = damage;
      ItemSlot[] itemSlotArray = new ItemSlot[2]
      {
        player.Entity.LeftHandItemSlot,
        player.Entity.RightHandItemSlot
      };
      for (int index = 0; index < itemSlotArray.Length; ++index)
      {
        ItemSlot itemslot = itemSlotArray[index];
        JsonObject itemAttribute = itemslot.Itemstack?.ItemAttributes?["eshield"];
        if (itemAttribute != null && itemAttribute.Exists)
        {
          Entity sourceEntity = dmgSource.SourceEntity;
          bool? nullable1;
          bool? nullable2;
          if (sourceEntity == null)
          {
            nullable1 = new bool?();
            nullable2 = nullable1;
          }
          else
          {
            JsonObject attributes = sourceEntity.Properties.Attributes;
            if (attributes == null)
            {
              nullable1 = new bool?();
              nullable2 = nullable1;
            }
            else
              nullable2 = new bool?(attributes["isProjectile"].AsBool());
          }
          nullable1 = nullable2;
          bool valueOrDefault = nullable1.GetValueOrDefault();
          string key1 = !player.Entity.Controls.Sneak || player.Entity.Attributes.GetInt("aiming", 0) == 1 ? "passive" : "active";
          float num3;
          float num4;
          if (valueOrDefault && itemAttribute["protectionChance"][key1 + "-projectile"].Exists)
          {
            num3 = itemAttribute["protectionChance"][key1 + "-projectile"].AsFloat();
            num4 = itemAttribute["projectileDamageAbsorption"].AsFloat(2f);
          }
          else
          {
            num3 = itemAttribute["protectionChance"][key1].AsFloat();
            num4 = itemAttribute["damageAbsorption"].AsFloat(2f);
          }
          double attackYaw;
          double attackPitch;
          if (dmgSource.GetAttackAngle(player.Entity.Pos.XYZ, out attackYaw, out attackPitch))
          {
            bool flag = Math.Abs(attackPitch) > 1.1344640254974365;
            double yaw = (double) player.Entity.Pos.Yaw;
            double pitch = (double) player.Entity.Pos.Pitch;
            if (valueOrDefault)
            {
              double x = dmgSource.SourceEntity.SidedPos.Motion.X;
              double y = dmgSource.SourceEntity.SidedPos.Motion.Y;
              double z = dmgSource.SourceEntity.SidedPos.Motion.Z;
              flag = Math.Sqrt(x * x + z * z) < Math.Abs(y);
            }
            if (!flag ? (double) Math.Abs(GameMath.AngleRadDistance((float) yaw, (float) attackYaw)) < num1 : (double) Math.Abs(GameMath.AngleRadDistance((float) pitch, (float) attackPitch)) < 0.5235987901687622)
            {
              float val1 = 0.0f;
              double num5 = this.api.World.Rand.NextDouble();
              if (num5 < (double) num3)
                val1 += num4;
              if (player is IServerPlayer serverPlayer)
              {
                int damageLogChatGroup = GlobalConstants.DamageLogChatGroup;
                string message = Lang.Get("{0:0.#} of {1:0.#} damage blocked by shield ({2} use)", (object) Math.Min(val1, damage), (object) damage, (object) key1);
                serverPlayer.SendMessage(damageLogChatGroup, message, EnumChatType.Notification);
              }
              if(itemslot.Itemstack.Attributes.GetInt("durability") > 1)
                damage = Math.Max(0.0f, damage - val1);
              string key2 = "blockSound" + ((double) num2 > 6.0 ? "Heavy" : "Light");
              this.api.World.PlaySoundAt(AssetLocation.Create(itemslot.Itemstack.ItemAttributes["eshield"][key2].AsString("game:held/shieldblock-wood-light"), itemslot.Itemstack.Collectible.Code.Domain).WithPathPrefixOnce("sounds/").WithPathAppendixOnce(".ogg"), player);
              if (num5 < (double) num3)
                (this.api as ICoreServerAPI).Network.BroadcastEntityPacket(player.Entity.EntityId, 200, SerializerUtil.Serialize<string>("shieldBlock" + (index == 0 ? "L" : "R")));
              if (this.api.Side == EnumAppSide.Server)
              {
                itemslot.Itemstack.Collectible.DamageItem(this.api.World, dmgSource.SourceEntity, itemslot);
                itemslot.MarkDirty();
              }
            }
          }
          else
            break;
        }
      }
      return damage;
    }
}