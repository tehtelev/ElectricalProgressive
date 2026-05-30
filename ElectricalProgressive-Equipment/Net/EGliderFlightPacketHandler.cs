using System;
using System.Collections.Generic;
using ElectricalProgressive.Patch;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Net;

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

    // ── Параметры крена при повороте ───────────────────────────────────────
    /// <summary>Максимальный угол крена (в радианах). ~34°</summary>
    private const float MAX_BANK_ANGLE = 1.5f;
    /// <summary>Коэффициент: угловая скорость рысканья (рад/с) → целевой крен (рад)</summary>
    private const float BANK_SENSITIVITY = 0.7f;
    /// <summary>Скорость нарастания крена (lerp-множитель). Выше = резче</summary>
    private const float BANK_SMOOTHING = 4.0f;
    /// <summary>Скорость сброса крена при приземлении</summary>
    private const float BANK_RESET_SPEED = 5.0f;
    /// <summary>Сглаживание угловой скорости рысканья (EMA). 0 = без памяти, 1 = не меняется</summary>
    private const float YAW_RATE_EMA = 0.82f;

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
    private bool physicsPatched = false;

    // ── Состояние крена ────────────────────────────────────────────────────
    /// <summary>Рысканье на предыдущем кадре. float.NaN = первый кадр полёта</summary>
    private float _prevGlideYaw = float.NaN;
    /// <summary>Сглаженная угловая скорость рысканья (рад/с)</summary>
    private float _smoothedYawRate = 0f;
    /// <summary>Текущий угол крена модели (рад), применяется к pos.Roll</summary>
    private float _bankAngle = 0f;

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

        // Ждём появления игрока и применяем патч физики
        RegisterPhysicsPatch();

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

    #region Клиентские методы (отправка команд и патч физики)

    /// <summary>
    /// Регистрирует патч физики для элитра.
    /// Вызывается при старте клиентской стороны.
    /// Подписывается на событие подключения игрока и применяет патч к существующему игроку.
    /// Также создает повторяющийся таймер на случай задержки инициализации сущности игрока.
    /// </summary>
    private void RegisterPhysicsPatch()
    {
        if (capi == null) return;

        // Патчим при входе игрока
        capi.Event.PlayerJoin += OnPlayerJoin;

        // Если игрок уже существует
        if (capi.World.Player?.Entity != null)
        {
            ApplyPhysicsPatch(capi.World.Player.Entity);
        }

        // Дополнительная проверка через тики (на случай задержки инициализации)
        capi.Event.RegisterGameTickListener(dt =>
        {
            if (!physicsPatched && capi.World.Player?.Entity != null)
            {
                ApplyPhysicsPatch(capi.World.Player.Entity);
            }
        }, 100, 10); // 10 попыток с интервалом 100мс
    }
    /// <summary>
    /// Обработчик события подключения игрока к серверу.
    /// Применяет патч физики к сущности подключившегося игрока.
    /// </summary>
    /// <param name="player">Подключившийся клиентский игрок</param>
    private void OnPlayerJoin(IClientPlayer player)
    {
        if (player?.Entity != null)
        {
            ApplyPhysicsPatch(player.Entity);
        }
    }
    /// <summary>
    /// Применяет патч физики к указанной сущности игрока.
    /// Патч позволяет модифицировать стандартную физику полёта для поддержки форсажа элитра.
    /// </summary>
    /// <param name="entity">Сущность игрока, к которой применяется патч</param>
    private void ApplyPhysicsPatch(Entity entity)
    {
        if (physicsPatched) return;
        if (entity == null) return;

        capi?.Logger.Notification("[ElectricalProgressive] Applying EGlider physics patch...");
        EGliderPhysicsPatcher.PatchPlayerPhysics(entity);
        physicsPatched = true;
    }

    /// <summary>
    /// Проверяет, экипирован ли глайдер у игрока
    /// </summary>
    /// <param name="entity">Сущность игрока для проверки</param>
    /// <returns>true, если у игрока в слоте рюкзака экипирован ItemEGlider</returns>
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
    /// <param name="entity">Сущность игрока, у которого проверяется прочность</param>
    /// <returns>Текущее значение прочности элитра или 0, если элитр не найден</returns>
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
    /// <param name="isActive">true - включить форсаж, false - выключить</param>
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
    /// <param name="dt">Дельта времени в секундах</param>
    /// <param name="entity">Сущность игрока</param>
    /// <param name="pos">Позиция игрока</param>
    /// <param name="controls">Управление игроком</param>
    public void ApplyFlyingPhysics(float dt, Entity entity, EntityPos pos, EntityControls controls)
    {
        bool isGlidingWithEGlider = controls.Gliding && HasEGliderEquipped(entity);

        if (isGlidingWithEGlider)
        {
            double num1 = Math.Cos(pos.Pitch);
            double num2 = Math.Sin(pos.Pitch);
            double num3 = Math.Cos(pos.Yaw);
            double num4 = Math.Sin(pos.Yaw);

            bool hasValidGlider = GetEGliderDurability(entity) > DURABILITY_LOSS_AMOUNT;
            bool wantsAfterburner = controls.Sneak && hasValidGlider;

            if (isAfterburnerCooldown)
            {
                cooldownTimer += dt;
                if (cooldownTimer >= COOLDOWN_DURATION)
                {
                    isAfterburnerCooldown = false;
                    cooldownTimer = 0f;
                }
            }

            if (wantsAfterburner != wasAfterburnerActive && !isAfterburnerCooldown)
            {
                wasAfterburnerActive = wantsAfterburner;
                SendAfterburnerCommand(wasAfterburnerActive);
                isAfterburnerCooldown = true;
                cooldownTimer = 0f;
            }

            if (wasAfterburnerActive && hasValidGlider)
            {
                controls.GlideSpeed = Math.Min(MAX_GLIDE_SPEED, controls.GlideSpeed + GLIDE_SPEED_BOOST);
            }
            else if (wasAfterburnerActive && !hasValidGlider)
            {
                wasAfterburnerActive = false;
                SendAfterburnerCommand(false);
                isAfterburnerCooldown = false;
                cooldownTimer = 0f;
            }

            if (hasValidGlider)
            {
                double max = entity.Stats.GetBlended("gliderSpeedMax") - 0.8;
                double num6 = GameMath.Clamp(controls.GlideSpeed, 0.005f, max);

                float blended = entity.Stats.GetBlended("gliderLiftMax");
                double y = Math.Min(num2 * num6, blended);

                pos.Motion.Add(-num1 * num4 * num6, y, -num1 * num3 * num6);
                pos.Motion.Mul(GameMath.Clamp(1.0 - pos.Motion.Length() * 0.13, 0.0, 1.0));
            }

            UpdateGliderBank(dt, pos);
            ApplyHeadingRelativeBank(pos, true);
        }
        else
        {
            ApplyHeadingRelativeBank(pos, false);

            if (wasAfterburnerActive)
            {
                wasAfterburnerActive = false;
                SendAfterburnerCommand(false);
                isAfterburnerCooldown = false;
                cooldownTimer = 0f;
            }
        }
    }


    private void ApplyHeadingRelativeBank(EntityPos pos, bool gliding)
    {
        if (gliding)
        {
            float yaw = (float)pos.Yaw;

            // Крен сохраняем, но без вмешательства в Pitch
            // На востоке/западе знак будет корректным
            pos.Roll = _bankAngle * MathF.Sin(yaw);
        }
        else
        {
            // Плавный сброс крена
            _bankAngle += -_bankAngle * 0.15f;

            if (Math.Abs(_bankAngle) < 0.001f)
            {
                _bankAngle = 0f;
                _smoothedYawRate = 0f;
                _prevGlideYaw = float.NaN;
            }

            pos.Roll = _bankAngle;
        }
    }

    private float _bankPitchOffset = 0f;

    private void UpdateGliderBank(float dt, EntityPos pos)
    {
        float curYaw = (float)pos.Yaw;

        if (!float.IsNaN(_prevGlideYaw))
        {
            float yawDelta = curYaw - _prevGlideYaw;

            if (yawDelta > MathF.PI) yawDelta -= 2f * MathF.PI;
            if (yawDelta < -MathF.PI) yawDelta += 2f * MathF.PI;

            float instantRate = dt > 0.0001f ? yawDelta / dt : 0f;
            _smoothedYawRate = _smoothedYawRate * YAW_RATE_EMA
                               + instantRate * (1f - YAW_RATE_EMA);
        }

        _prevGlideYaw = curYaw;

        float targetBank = GameMath.Clamp(
            -_smoothedYawRate * BANK_SENSITIVITY,
            -MAX_BANK_ANGLE,
            MAX_BANK_ANGLE
        );

        _bankAngle += (targetBank - _bankAngle) * Math.Min(BANK_SMOOTHING * dt, 1f);
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