using System;
using System.Collections.Generic;
using ElectricalProgressive.Net;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Systems;

/// <summary>
/// Система обработки форсажа элитра (клиент + сервер)
/// </summary>
public class EGliderFlightPacketHandler : ModSystem
{
    #region Константы
    
    private const float GLIDE_SPEED_BOOST = 0.002f;
    private const float MAX_GLIDE_SPEED = 0.75f;
    private const float COOLDOWN_DURATION = 0.5f;
    private const int DURABILITY_LOSS_AMOUNT = 10;
    private const float DURABILITY_LOSS_INTERVAL = 1.0f;
    
    #endregion
    
    #region Серверные поля
    
    private ICoreServerAPI? sapi;
    private Dictionary<string, bool> activeAfterburners = new();
    private Dictionary<string, float> afterburnerTimers = new();
    
    #endregion
    
    #region Клиентские поля
    
    private ICoreClientAPI? capi;
    private IClientNetworkChannel? clientChannel;
    private bool wasAfterburnerActive = false;
    private bool isAfterburnerCooldown = false;
    private float cooldownTimer = 0f;
    
    #endregion
    
    #region Регистрация каналов
    
    public override bool ShouldLoad(EnumAppSide forSide) => true;
    
    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        capi = api;
        
        // Регистрируем канал для отправки команд на сервер
        clientChannel = api.Network.RegisterChannel("EP")
            .RegisterMessageType<EGliderAfterburnerPacket>();
        
        api.Logger.Notification("[ElectricalProgressive] EGliderFlightPacketHandler client started");
    }
    
    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        sapi = api;
        
        // Регистрируем канал и обработчик команд от клиента
        api.Network.RegisterChannel("EP")
            .RegisterMessageType<EGliderAfterburnerPacket>()
            .SetMessageHandler<EGliderAfterburnerPacket>(OnAfterburnerCommand);
        
        // Регистрируем тик для уменьшения прочности
        api.Event.RegisterGameTickListener(OnDurabilityTick, 1000);
        
        api.Logger.Notification("[ElectricalProgressive] EGliderFlightPacketHandler server started");
    }
    
    #endregion
    
    #region Клиентские методы (отправка команд)
    
    /// <summary>
    /// Проверяет, экипирован ли глайдер у игрока
    /// </summary>
    private bool HasEGliderEquipped(Entity entity)
    {
        if (entity is not EntityPlayer player) return false;
        
        var inventoryManager = player.Player?.InventoryManager;
        if (inventoryManager == null) return false;
        
        var backpackInventory = inventoryManager.GetOwnInventory("backpack");
        if (backpackInventory == null) return false;
        
        foreach (ItemSlot itemSlot in backpackInventory)
        {
            if (itemSlot is ItemSlotBackpack && itemSlot.Itemstack?.Collectible is ItemEGlider)
                return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Получает текущую прочность элитра (клиентская версия)
    /// </summary>
    private int GetEGliderDurability(Entity entity)
    {
        if (entity is not EntityPlayer player) return 0;
        
        var inventoryManager = player.Player?.InventoryManager;
        if (inventoryManager == null) return 0;
        
        var backpackInventory = inventoryManager.GetOwnInventory("backpack");
        if (backpackInventory == null) return 0;
        
        foreach (ItemSlot itemSlot in backpackInventory)
        {
            if (itemSlot is ItemSlotBackpack && itemSlot.Itemstack?.Collectible is ItemEGlider)
            {
                return itemSlot.Itemstack.Attributes.GetInt("durability");
            }
        }
        
        return 0;
    }
    
    /// <summary>
    /// Отправляет команду форсажа на сервер
    /// </summary>
    private void SendAfterburnerCommand(bool isActive)
    {
        if (clientChannel == null || capi?.World?.Player == null) return;
        
        var packet = new EGliderAfterburnerPacket
        {
            PlayerUID = capi.World.Player.PlayerUID,
            IsActive = isActive
        };
        clientChannel.SendPacket(packet);
    }
    
    /// <summary>
    /// Применяет физику полёта с форсажем (вызывается из патча)
    /// </summary>
    public void ApplyFlyingPhysics(float dt, Entity entity, EntityPos pos, EntityControls controls)
    {
        if (controls.Gliding && HasEGliderEquipped(entity))
        {
            double num1 = Math.Cos(pos.Pitch);
            double num2 = Math.Sin(pos.Pitch);
            double num3 = Math.Cos(pos.Yaw);
            double num4 = Math.Sin(pos.Yaw);
            
            // Проверяем наличие элитра и его прочность
            bool hasValidGlider = GetEGliderDurability(entity) > DURABILITY_LOSS_AMOUNT;
            bool wantsAfterburner = controls.Sneak && hasValidGlider;
            
            // Кулдаун отправки команд
            if (isAfterburnerCooldown)
            {
                cooldownTimer += dt;
                if (cooldownTimer >= COOLDOWN_DURATION)
                {
                    isAfterburnerCooldown = false;
                    cooldownTimer = 0f;
                }
            }
            
            // Отправляем команду только при изменении состояния
            if (wantsAfterburner != wasAfterburnerActive && !isAfterburnerCooldown)
            {
                wasAfterburnerActive = wantsAfterburner;
                SendAfterburnerCommand(wasAfterburnerActive);
                isAfterburnerCooldown = true;
                cooldownTimer = 0f;
            }
            
            // Локальное ускорение (только если элитр исправен)
            if (wasAfterburnerActive && hasValidGlider)
            {
                controls.GlideSpeed = Math.Min(MAX_GLIDE_SPEED, controls.GlideSpeed + GLIDE_SPEED_BOOST);
            }
            else if (wasAfterburnerActive && !hasValidGlider)
            {
                // Если элитр сломался во время полёта, отключаем форсаж
                wasAfterburnerActive = false;
                SendAfterburnerCommand(false);
                isAfterburnerCooldown = false;
                cooldownTimer = 0f;
            }

            if (hasValidGlider)
            {
                // Физика полёта
                double max = entity.Stats.GetBlended("gliderSpeedMax") - 0.8;
                double num6 = GameMath.Clamp(controls.GlideSpeed, 0.005f, max);
                float blended = entity.Stats.GetBlended("gliderLiftMax");
                double y = Math.Min(num2 * num6, blended);
                pos.Motion.Add(-num1 * num4 * num6, y, -num1 * num3 * num6);
                pos.Motion.Mul(GameMath.Clamp(1.0 - pos.Motion.Length() * 0.13, 0.0, 1.0));
            }
        }
        else
        {
            if (wasAfterburnerActive)
            {
                wasAfterburnerActive = false;
                SendAfterburnerCommand(false);
                isAfterburnerCooldown = false;
                cooldownTimer = 0f;
            }
        }
    }
    
    #endregion
    
    #region Серверные методы (обработка команд и износ)
    
    /// <summary>
    /// Обработка команды форсажа от клиента
    /// </summary>
    private void OnAfterburnerCommand(IServerPlayer fromPlayer, EGliderAfterburnerPacket packet)
    {
        if (fromPlayer?.PlayerUID != packet.PlayerUID) return;
        
        if (packet.IsActive)
        {
            if (activeAfterburners.ContainsKey(fromPlayer.PlayerUID) && activeAfterburners[fromPlayer.PlayerUID])
                return;
            
            var (hasGlider, gliderSlot) = GetEGliderSlot(fromPlayer.Entity);
            if (hasGlider && gliderSlot?.Itemstack != null)
            {
                int currentDurability = GetCurrentDurability(gliderSlot);
                if (currentDurability >= DURABILITY_LOSS_AMOUNT)
                {
                    activeAfterburners[fromPlayer.PlayerUID] = true;
                    if (!afterburnerTimers.ContainsKey(fromPlayer.PlayerUID))
                        afterburnerTimers[fromPlayer.PlayerUID] = 0f;
                }
            }
        }
        else
        {
            if (activeAfterburners.ContainsKey(fromPlayer.PlayerUID))
            {
                activeAfterburners[fromPlayer.PlayerUID] = false;
            }
        }
    }
    
    /// <summary>
    /// Тик для уменьшения прочности
    /// </summary>
    private void OnDurabilityTick(float dt)
    {
        if (sapi == null) return;
        
        foreach (var player in sapi.World.AllOnlinePlayers)
        {
            var entity = player.Entity;
            if (entity == null) continue;
            
            bool isActive = activeAfterburners.ContainsKey(player.PlayerUID) && 
                            activeAfterburners[player.PlayerUID] &&
                            entity.Controls.Sneak && 
                            entity.Controls.Gliding;
            
            if (isActive)
            {
                var (hasGlider, gliderSlot) = GetEGliderSlot(entity);
                if (hasGlider && gliderSlot?.Itemstack != null)
                {
                    afterburnerTimers[player.PlayerUID] += dt;
                    
                    if (afterburnerTimers[player.PlayerUID] >= DURABILITY_LOSS_INTERVAL)
                    {
                        int currentDurability = GetCurrentDurability(gliderSlot);
                        
                        if (currentDurability >= DURABILITY_LOSS_AMOUNT)
                        {
                            int newDurability = currentDurability - DURABILITY_LOSS_AMOUNT;
                            gliderSlot.Itemstack.Attributes.SetInt("durability", newDurability);
                            gliderSlot.MarkDirty();
                            
                            afterburnerTimers[player.PlayerUID] = 0f;
                            
                            if (newDurability <= DURABILITY_LOSS_AMOUNT)
                            {
                                DisableGlider(player, gliderSlot);
                            }
                        }
                        else
                        {
                            DisableAfterburner(player);
                        }
                    }
                }
                else
                {
                    DisableAfterburner(player);
                }
            }
        }
    }
    
    /// <summary>
    /// Получение слота с элитром
    /// </summary>
    private (bool hasGlider, ItemSlot gliderSlot) GetEGliderSlot(Entity entity)
    {
        if (entity is not EntityPlayer player) return (false, null);
        
        var inventoryManager = player.Player?.InventoryManager;
        if (inventoryManager == null) return (false, null);
        
        var backpackInventory = inventoryManager.GetOwnInventory("backpack");
        if (backpackInventory == null) return (false, null);
        
        foreach (ItemSlot itemSlot in backpackInventory)
        {
            if (itemSlot is ItemSlotBackpack && itemSlot.Itemstack?.Collectible is ItemEGlider)
                return (true, itemSlot);
        }
        
        return (false, null);
    }
    
    /// <summary>
    /// Получение текущей прочности
    /// </summary>
    private int GetCurrentDurability(ItemSlot slot)
    {
        if (slot?.Itemstack == null) return 0;
        return slot.Itemstack.Attributes.GetInt("durability");
    }
    
    /// <summary>
    /// Отключение форсажа у игрока
    /// </summary>
    private void DisableAfterburner(IPlayer player)
    {
        if (activeAfterburners.ContainsKey(player.PlayerUID))
            activeAfterburners[player.PlayerUID] = false;
        if (afterburnerTimers.ContainsKey(player.PlayerUID))
            afterburnerTimers[player.PlayerUID] = 0f;
    }
    
    /// <summary>
    /// Отключение элитра при поломке
    /// </summary>
    private void DisableGlider(IPlayer player, ItemSlot gliderSlot)
    {
        DisableAfterburner(player);
        if (player.Entity != null)
        {
            player.Entity.Controls.Gliding = false;
            player.Entity.Controls.GlideSpeed = 0;
        }
    }
    
    #endregion
}