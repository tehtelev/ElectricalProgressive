using ElectricalProgressive.Patch;
using System;
using System.Collections.Generic;
using Vintagestory.GameContent;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ElectricalProgressive.Net;

/// <summary>
/// Обработка механики полета и форсажа электрического глайдера (Client + Server).
/// </summary>
public class EGliderFlightPacketHandler : ModSystem
{
    #region Константы: Форсаж и Полет

    private const float GLIDE_SPEED_BOOST = 0.002f;
    private const float MAX_GLIDE_SPEED = 0.75f;
    private const float COOLDOWN_DURATION = 0.5f;

    // Параметры прочности: минимальная прочность для работы, интервал и сумма списания.
    private const int DURABILITY_LOSS_AMOUNT = 10;
    private const float DURABILITY_LOSS_INTERVAL = 1.0f;

    #endregion

    #region Константы: Крен (Banking)

    /// <summary>Максимальный угол крена (рад).</summary>
    private const float MAX_BANK_ANGLE = 1.5f;
    /// <summary>Чувствительность: скорость поворота -> целевой крен.</summary>
    private const float BANK_SENSITIVITY = 0.3f;
    /// <summary>Скорость нарастания крена (lerp-множитель).</summary>
    private const float BANK_SMOOTHING = 4.0f;
    /// <summary>Скорость сброса крена при приземлении.</summary>
    private const float BANK_RESET_SPEED = 5.0f;
    /// <summary>Сглаживание угловой скорости рысканья (EMA).</summary>
    private const float YAW_RATE_EMA = 0.9f;

    #endregion

    #region Поля: Сервер

    private ICoreServerAPI? sapi;
    private Dictionary<string, bool> activeAfterburners = new();
    private Dictionary<string, float> afterburnerTimers = new();

    #endregion

    #region Поля: Клиент

    private ICoreClientAPI? capi;
    private IClientNetworkChannel? clientChannel;

    // Состояние форсажа
    private bool wasAfterburnerActive = false;
    private bool isAfterburnerCooldown = false;
    private float cooldownTimer = 0f;

    // Состояние крена модели
    private float _prevGlideYaw = float.NaN;
    private float _smoothedYawRate = 0f;
    // Было: private float _bankAngle = 0f;
    internal float BankAngle { get; private set; } = 0f;

    private bool physicsPatched = false;
    // --- ЗВУК ФОРСАЖА ---
    private ILoadedSound? _afterburnerSound;

    private static readonly AssetLocation AfterburnerSoundLocation = new AssetLocation("electricalprogressiveequipment:sounds/gliderboost.ogg");

    #endregion

    public override bool ShouldLoad(EnumAppSide forSide) => true;

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        capi = api;

        // Регистрация сети
        clientChannel = api.Network.RegisterChannel("EP")
            .RegisterMessageType<EGliderAfterburnerPacket>();

        RegisterPhysicsPatch();

        // Таймер для сброса крена, когда игрок не парит
        api.Event.RegisterGameTickListener(OnClientBankReset, 20);

        // Регистрируем рендерер крена камеры — работает каждый кадр
        api.Event.RegisterRenderer(
            new GliderCameraRollRenderer(api, this),
            EnumRenderStage.Before   // до отрисовки сцены
        );

        //api.Logger.Notification("[ElectricalProgressive] EGliderFlightPacketHandler client started");
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        sapi = api;
        
        // Регистрация сети и обработчика команд
        var channel = api.Network.RegisterChannel("EP")
            .RegisterMessageType<EGliderAfterburnerPacket>();

        channel.SetMessageHandler<EGliderAfterburnerPacket>(OnAfterburnerCommand);

        // Тик для списания прочности
        sapi.Event.RegisterGameTickListener(OnDurabilityTick, 1000);

        //api.Logger.Notification("[ElectricalProgressive] EGliderFlightPacketHandler server started");
    }

    #region Частицы форсажа

    // Статические свойства частиц создаются один раз. Позиции обновляются динамически перед спавном.
    private static readonly SimpleParticleProperties LeftWingProps = new(
        minQuantity: 2, maxQuantity: 10,
        color: ColorUtil.ToRgba(220, 255, 255, 255),
        minPos: Vec3d.Zero, maxPos: Vec3d.Zero,
        minVelocity: new Vec3f(-0.2f, -0.2f, -0.2f),
        maxVelocity: new Vec3f(0.2f, 0.2f, 0.2f),
        lifeLength: 1.0f, gravityEffect: 0f,
        minSize: 0.2f, maxSize: 1.0f,
        model: EnumParticleModel.Quad
    );

    private static readonly SimpleParticleProperties RightWingProps = new(
        minQuantity: 2, maxQuantity: 10,
        color: ColorUtil.ToRgba(220, 255, 255, 255),
        minPos: Vec3d.Zero, maxPos: Vec3d.Zero,
        minVelocity: new Vec3f(-0.2f, -0.2f, -0.2f),
        maxVelocity: new Vec3f(0.2f, 0.2f, 0.2f),
        lifeLength: 1.0f, gravityEffect: 0f,
        minSize: 0.2f, maxSize: 1.0f,
        model: EnumParticleModel.Quad
    );

    /// <summary>
    /// Спавнит частицы на краях крыльев с учётом крена.
    /// </summary>
    private void SpawnAfterburnerParticles(Entity entity)
    {
        if (capi == null || entity == null)
            return;

        Vec3d pos = entity.Pos.XYZ;
        Vec3f viewVec = entity.Pos.GetViewVector();

        // 1. Нормализуем вектор направления
        Vec3d forward = new Vec3d(viewVec.X, viewVec.Y, viewVec.Z).Normalize();

        // 2. Вычисляем базовые локальные оси (до учёта крена)
        // Защита от деления на ноль при полёте строго вверх/вниз
        Vec3d right = forward.Cross(new Vec3d(0, 1, 0)).Normalize();
        if (right.LengthSq() < 0.0001f)
        {
            // Фоллбэк: берём вектор вперёд-вправо, если forward параллелен Y
            right = forward.Cross(new Vec3d(1, 0, 0)).Normalize();
        }
        Vec3d up = right.Cross(forward).Normalize();

        // 3. Поворачиваем оси 'right' и 'up' вокруг 'forward' на угол крена
        if (Math.Abs(BankAngle) > 0.0001f)
        {
            Vec3d forwardCrossRight = forward.Cross(right);
            right = right * MathF.Cos(-BankAngle) + forwardCrossRight * MathF.Sin(-BankAngle);
            up = right.Cross(forward).Normalize();
        }

        // 4. Координаты крыльев и размеры бокса частиц
        float wingOffsetX = 2.0f;
        float wingOffsetY = 0.7f;
        float wingOffsetZ = -0.2f;
        float boxHalfSize = 0.4f;

        // Обновляем статические свойства частиц динамическими координатами
        // Левое крыло
        LeftWingProps.MinPos = pos + right * (-wingOffsetX - boxHalfSize) + up * (wingOffsetY - boxHalfSize) + forward * (wingOffsetZ - boxHalfSize);
        LeftWingProps.AddPos = pos + right * (-wingOffsetX + boxHalfSize) + up * (wingOffsetY + boxHalfSize) + forward * (wingOffsetZ + boxHalfSize) - LeftWingProps.MinPos;

        // Правое крыло
        RightWingProps.MinPos = pos + right * (wingOffsetX - boxHalfSize) + up * (wingOffsetY - boxHalfSize) + forward * (wingOffsetZ - boxHalfSize);
        RightWingProps.AddPos = pos + right * (wingOffsetX + boxHalfSize) + up * (wingOffsetY + boxHalfSize) + forward * (wingOffsetZ + boxHalfSize) - RightWingProps.MinPos;

        // 5. Спавним частицы (API клонирует свойства внутри, но создание объекта класса экономит GC)
        capi.World.SpawnParticles(LeftWingProps);
        capi.World.SpawnParticles(RightWingProps);
        
    }

    #endregion


    private void StartAfterburnerSound(Entity entity)
    {
        if (_afterburnerSound != null || capi == null)
            return;
        _afterburnerSound = capi.World.LoadSound(new SoundParams()
        {
            Location = AfterburnerSoundLocation,
            ShouldLoop = true,
            Position = new Vec3f([(float)entity.Pos.X, (float)entity.Pos.Y, (float)entity.Pos.Z]),
            DisposeOnFinish = false,
            Volume = 0.25f,
        });
        _afterburnerSound.Start();
    }

    private void StopAfterburnerSound()
    {
        if (_afterburnerSound == null)
            return;
        _afterburnerSound.Stop();
        _afterburnerSound.Dispose();
        _afterburnerSound = null;
    }


    #region Методы инициализации патча

    private void RegisterPhysicsPatch()
    {
        if (capi == null) return;

        capi.Event.PlayerJoin += OnPlayerJoin;

        // Применяем сразу, если игрок уже есть
        if (capi.World.Player?.Entity != null) ApplyPhysicsPatch(capi.World.Player.Entity);

        // Фоллбэк на случай задержки инициализации
        capi.Event.RegisterGameTickListener(dt =>
        {
            if (!physicsPatched && capi.World.Player?.Entity != null)
                ApplyPhysicsPatch(capi.World.Player.Entity);
        }, 100, 10);
    }

    private void OnPlayerJoin(IClientPlayer player)
    {
        if (player?.Entity != null)
        {
            ApplyPhysicsPatch(player.Entity);
        }
    }

    private void ApplyPhysicsPatch(Entity entity)
    {
        if (physicsPatched || entity == null) return;

        capi?.Logger.Notification("[ElectricalProgressive] Applying EGlider physics patch...");
        EGliderPhysicsPatcher.PatchPlayerPhysics(entity);
        physicsPatched = true;
    }

    #endregion

    #region Клиентская логика (Физика и Сеть)

    /// <summary>
    /// Основной метод физики, вызываемый патчем.
    /// </summary>
    public void ApplyFlyingPhysics(float dt, Entity entity, EntityPos pos, EntityControls controls)
    {
        if (!controls.Gliding || !TryFindGlider(entity, out _))
            return;

        double num1 = Math.Cos(pos.Pitch);
        double num2 = Math.Sin(pos.Pitch);
        double num3 = Math.Cos(pos.Yaw);
        double num4 = Math.Sin(pos.Yaw);

        bool hasValidGlider = GetEGliderDurability(entity) > DURABILITY_LOSS_AMOUNT;
        bool wantsAfterburner = controls.Sneak && hasValidGlider;

        // Обработка кулдауна
        if (isAfterburnerCooldown)
        {
            cooldownTimer += dt;
            if (cooldownTimer >= COOLDOWN_DURATION)
            {
                isAfterburnerCooldown = false;
                cooldownTimer = 0f;
            }
        }
        
        // Переключение форсажа
        if (wantsAfterburner != wasAfterburnerActive && !isAfterburnerCooldown)
        {
            wasAfterburnerActive = wantsAfterburner;
            SendAfterburnerCommand(wasAfterburnerActive);
            isAfterburnerCooldown = true;
            cooldownTimer = 0f;

            if (wasAfterburnerActive) StartAfterburnerSound(entity);
            else StopAfterburnerSound();
        }

        // Применение скорости форсажа
        if (wasAfterburnerActive && hasValidGlider)
        {
            controls.GlideSpeed = Math.Min(MAX_GLIDE_SPEED, controls.GlideSpeed + GLIDE_SPEED_BOOST);
           
            SpawnAfterburnerParticles(entity);
        }
        else if (wasAfterburnerActive && !hasValidGlider)
        {
            // Сломался - выключаем
            wasAfterburnerActive = false;
            StopAfterburnerSound();
            SendAfterburnerCommand(false);
            isAfterburnerCooldown = false;
            cooldownTimer = 0f;
        }

        // Базовая физика глайдера (если прочность позволяет)
        if (hasValidGlider)
        {
            double maxSpeed = entity.Stats.GetBlended("gliderSpeedMax") - 0.8;
            double speed = GameMath.Clamp(controls.GlideSpeed, 0.005f, maxSpeed);
            float lift = entity.Stats.GetBlended("gliderLiftMax");

            pos.Motion.Add(-num1 * num4 * speed, Math.Min(num2 * speed, lift), -num1 * num3 * speed);
            pos.Motion.Mul(GameMath.Clamp(1.0 - pos.Motion.Length() * 0.13, 0.0, 1.0));
        }

        // Обновление и применение крена
        UpdateGliderBank(entity, dt, pos);
        ApplyHeadingRelativeBank(entity, pos, true);
    }

    private void OnClientBankReset(float dt)
    {
        EntityPlayer? entity = capi?.World?.Player?.Entity;
        if (entity == null || capi == null)
            return;

        bool isGlidingWithEGlider = entity.Controls.Gliding && TryFindGlider(entity, out _);

        if (!isGlidingWithEGlider)
        {
            // Сброс крена и форсажа при выключении глайдера
            ApplyHeadingRelativeBank(entity, entity.Pos, false);

            if (wasAfterburnerActive)
            {
                wasAfterburnerActive = false;
                StopAfterburnerSound();
                SendAfterburnerCommand(false);
                isAfterburnerCooldown = false;
                cooldownTimer = 0f;
            }
        }
    }

    /// <summary>
    /// Отправка команды на сервер.
    /// </summary>
    private void SendAfterburnerCommand(bool isActive)
    {
        if (clientChannel == null || capi?.World?.Player == null)
            return;

        clientChannel.SendPacket(new EGliderAfterburnerPacket
        {
            PlayerUID = capi.World.Player.PlayerUID,
            IsActive = isActive
        });
    }

    #endregion

    #region Клиентская логика: Крен (Banking)

    private void ApplyHeadingRelativeBank(Entity entity, EntityPos pos, bool gliding)
    {
        var renderer = entity.Properties.Client.Renderer as EntityPlayerShapeRenderer;
        if (renderer == null)
            return;

        if (gliding)
        {
            // Применяем крен через xangle и обнуляем ванильный Roll
            renderer.xangle = BankAngle;
            pos.Roll = 0;
        }
        else
        {
            // Плавный сброс угла крена к нулю
            BankAngle += -BankAngle * Math.Min(BANK_RESET_SPEED * 0.016f, 1f);

            if (Math.Abs(BankAngle) < 0.001f)
            {
                BankAngle = 0f;
                _smoothedYawRate = 0f;
                _prevGlideYaw = float.NaN;
            }

            renderer.xangle = BankAngle;
            pos.Roll = 0;
        }
    }

    private void UpdateGliderBank(Entity entity, float dt, EntityPos pos)
    {
        float curYaw = (float)pos.Yaw;

        if (!float.IsNaN(_prevGlideYaw))
        {
            float yawDelta = curYaw - _prevGlideYaw;

            // Обход резких скачков при переключении управления мышью
            var isMouseControlActive = (entity.Api as ICoreClientAPI)?.Input.IsHotKeyPressed("togglemousecontrol") == true;

            if (isMouseControlActive)
            {
                yawDelta = 0;
            }

            // Нормализация дельты угла в пределах [-PI, PI]
            if (yawDelta > MathF.PI)
                yawDelta -= 2f * MathF.PI;
            if (yawDelta < -MathF.PI)
                yawDelta += 2f * MathF.PI;

            float instantRate = dt > 0.0001f ? yawDelta / dt : 0f;

            // Сглаживание скорости рысканья (Exponential Moving Average)
            _smoothedYawRate = _smoothedYawRate * YAW_RATE_EMA + instantRate * (1f - YAW_RATE_EMA);
            
        }

        _prevGlideYaw = curYaw;

        // Расчет целевого крена и интерполяция текущего угла
        float targetBank = GameMath.Clamp(_smoothedYawRate * BANK_SENSITIVITY, -MAX_BANK_ANGLE, MAX_BANK_ANGLE);
        BankAngle += (targetBank - BankAngle) * Math.Min(BANK_SMOOTHING * dt, 1f);
    }

    #endregion

    #region Серверная логика: Обработка пакетов и Прочность

    private void OnAfterburnerCommand(IServerPlayer fromPlayer, EGliderAfterburnerPacket packet)
    {
        if (fromPlayer?.PlayerUID != packet.PlayerUID)
            return;

        if (packet.IsActive)
        {
            // Если уже активен - игнорируем
            if (activeAfterburners.GetValueOrDefault(fromPlayer.PlayerUID)) return;

            var (_, gliderSlot) = TryFindGliderServer(fromPlayer.Entity);

            if (gliderSlot != null && GetCurrentDurability(gliderSlot) >= DURABILITY_LOSS_AMOUNT)
            {
                activeAfterburners[fromPlayer.PlayerUID] = true;
                afterburnerTimers[fromPlayer.PlayerUID] = 0f;
            }
        }
        else
        {
            // Принудительное выключение
            if (activeAfterburners.ContainsKey(fromPlayer.PlayerUID))
                activeAfterburners[fromPlayer.PlayerUID] = false;
        }
    }

    private void OnDurabilityTick(float dt)
    {
        if (sapi == null)
            return;

        foreach (var player in sapi.World.AllOnlinePlayers)
        {
            var entity = player.Entity;
            if (entity == null || !activeAfterburners.GetValueOrDefault(player.PlayerUID))
                continue;

            // Проверяем, активен ли форсаж в данный момент (Sneak + Gliding)
            if (!entity.Controls.Sneak || !entity.Controls.Gliding)
            {
                DisableAfterburner(player);
                continue;
            }

            var (_, gliderSlot) = TryFindGliderServer(entity);

            // Если элитр слетел или отсутствует
            if (gliderSlot == null)
            {
                DisableAfterburner(player);
                continue;
            }

            afterburnerTimers[player.PlayerUID] += dt;

            if (afterburnerTimers[player.PlayerUID] >= DURABILITY_LOSS_INTERVAL)
            {
                int currentDurability = GetCurrentDurability(gliderSlot);

                if (currentDurability >= DURABILITY_LOSS_AMOUNT)
                {
                    // Списываем прочность
                    gliderSlot.Itemstack.Attributes.SetInt("durability", currentDurability - DURABILITY_LOSS_AMOUNT);
                    gliderSlot.MarkDirty();

                    afterburnerTimers[player.PlayerUID] = 0f;

                    // Если элитр сломался
                    if (GetCurrentDurability(gliderSlot) <= DURABILITY_LOSS_AMOUNT)
                        DisableGlider(player, gliderSlot);
                }
                else
                {
                    // Недостаточно прочности для следующего тика
                    DisableAfterburner(player);
                }
            }
        }
    }

    #endregion

    #region Вспомогательные методы (Инвентарь)

    /// <summary>Ищет электрический глайдер в рюкзаке игрока. Возвращает слот или null.</summary>
    private static bool TryFindGlider(Entity entity, out ItemSlot slot)
    {
        if (entity is not EntityPlayer player || player.Player == null)
        {
            slot = null;
            return false;
        }

        var backpackInv = player.Player.InventoryManager.GetOwnInventory("backpack");
        if (backpackInv == null)
        {
            slot = null;
            return false;
        }

        foreach (ItemSlot itemSlot in backpackInv)
        {
            if (itemSlot is ItemSlotBackpack && itemSlot.Itemstack?.Collectible is ItemEGlider)
            {
                slot = itemSlot;
                return true;
            }
        }

        slot = null;
        return false;
    }

    /// <summary>Серверная версия поиска слота (возвращает кортеж для удобства).</summary>
    private static (bool hasGlider, ItemSlot? gliderSlot) TryFindGliderServer(Entity entity)
    {
        if (TryFindGlider(entity, out var slot))
            return (true, slot);
        return (false, null);
    }

    /// <summary>Получает текущую прочность элитра.</summary>
    private static int GetEGliderDurability(Entity entity) => TryFindGlider(entity, out var s) ? GetCurrentDurability(s) : 0;

    /// <summary>Безопасное получение атрибута прочности.</summary>
    private static int GetCurrentDurability(ItemSlot slot)
        => (slot?.Itemstack != null) ? slot.Itemstack.Attributes.GetInt("durability") : 0;

    #endregion

    #region Вспомогательные методы: Отключение

    private void DisableAfterburner(IPlayer player)
    {
        activeAfterburners[player.PlayerUID] = false;
        afterburnerTimers[player.PlayerUID] = 0f;
    }

    private void DisableGlider(IPlayer player, ItemSlot gliderSlot)
    {
        DisableAfterburner(player);

        // Принудительный выход из режима планирования
        if (player.Entity != null)
        {
            player.Entity.Controls.Gliding = false;
            player.Entity.Controls.GlideSpeed = 0;
        }
    }

    #endregion
}
