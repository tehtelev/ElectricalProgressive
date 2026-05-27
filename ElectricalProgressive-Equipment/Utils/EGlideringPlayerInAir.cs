using System;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

#nullable disable
namespace Vintagestory.API.Common.Entities;

public class EGlideringPlayerInAir : PModuleInAir
{
    // Параметры форсажа
    private const float GLIDE_SPEED_BOOST = 0.002f; // Увеличение GlideSpeed за тик
    private const float MAX_GLIDE_SPEED = 0.75f; // Максимальный GlideSpeed (как в ванилле)

    private float airMovingStrengthFalling;

    public override void Initialize(JsonObject config, Entity entity)
    {
        base.Initialize(config, entity);
        this.airMovingStrengthFalling = this.AirMovingStrength / 4f;
    }

    // Проверка, надет ли элитр
    private bool HasEGliderEquipped(Entity entity)
    {
        // Получаем игрока из сущности
        if (entity is not EntityPlayer player) return false;

        // Получаем IPlayer из EntityPlayer, затем InventoryManager
        var inventoryManager = player.Player?.InventoryManager;
        if (inventoryManager == null) return false;

        // Получаем инвентарь рюкзака
        var backpackInventory = inventoryManager.GetOwnInventory("backpack");
        if (backpackInventory == null) return false;

        // Проверяем слоты рюкзака на наличие ItemEGlider
        foreach (ItemSlot itemSlot in backpackInventory)
        {
            if (itemSlot is ItemSlotBackpack && itemSlot.Itemstack?.Collectible is ItemEGlider)
                return true;
        }

        return false;
    }

    public override void ApplyFlying(
        float dt,
        Entity entity,
        EntityPos pos,
        EntityControls controls)
    {
        if (controls.Gliding)
        {
            double num1 = Math.Cos(pos.Pitch);
            double num2 = Math.Sin(pos.Pitch);
            double num3 = Math.Cos(pos.Yaw);
            double num4 = Math.Sin(pos.Yaw);
            double num5 = num2 + 0.15;
        
            // ФОРСАЖ: увеличиваем скорость (если зажат Shift и надет элитр)
            if (controls.ShiftKey && HasEGliderEquipped(entity))
            {

                controls.GlideSpeed = Math.Min(MAX_GLIDE_SPEED, controls.GlideSpeed + GLIDE_SPEED_BOOST);
                entity.World.Logger.Notification($"[FORSAZH] Speed:  -> {controls.GlideSpeed:F4}");
            
                // Если форсаж активен - НЕ уменьшаем скорость в этом тике
                // Пропускаем обычное замедление
            }
       
            double max = entity.Stats.GetBlended("gliderSpeedMax") - 0.8;
            double num6 = GameMath.Clamp(controls.GlideSpeed, 0.005f, max);
            float blended = entity.Stats.GetBlended("gliderLiftMax");
            double y = Math.Min(num2 * num6, blended);
            pos.Motion.Add(-num1 * num4 * num6, y, -num1 * num3 * num6);
            pos.Motion.Mul(GameMath.Clamp(1.0 - pos.Motion.Length() * 0.13, 0.0, 1.0));
        }
        else
        {
            base.ApplyFlying(dt, entity, pos, controls);
        }
    }
}