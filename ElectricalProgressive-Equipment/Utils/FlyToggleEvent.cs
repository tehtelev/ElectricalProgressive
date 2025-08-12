using System;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using ElectricalProgressive.Content.Item.Armor;
using ElectricalProgressive.Net;
using System.Linq;


namespace ElectricalProgressive.Utils;

public class FlyToggleEvent : ModSystem
{
    private ICoreAPI? api;
    private double lastCheckTotalHours;

    private IClientNetworkChannel? clientChannel;
    private IServerNetworkChannel? serverChannel;

    private float SavedSpeedMult = 1f;
    private EnumFreeMovAxisLock SavedAxis = EnumFreeMovAxisLock.None;

    private ICoreClientAPI? capi;

    private ICoreServerAPI? sapi;

    private int consumeFly;
    private float speedFly;

    // Статическое поле для хранения слотов брони
    private static readonly int[] ArmorSlots = ElectricalProgressiveEquipment.combatoverhaul
        ? new[] { 34, 35, 36, 26, 27, 28 }
        : Enumerable.Repeat(13, 6).ToArray();

    int consume = 20; // Количество энергии, потребляемое в секунду при полете

    /// <summary>
    /// Этот метод вызывается при загрузке мода.
    /// </summary>
    /// <param name="forSide"></param>
    /// <returns></returns>
    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return true;
    }

    /// <summary>
    /// Запускает систему на стороне сервера или клиента.
    /// </summary>
    /// <param name="api"></param>
    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        this.api = api;
    }


    /// <summary>
    /// Стартует клиентская сторона мода.
    /// </summary>
    /// <param name="api"></param>
    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        this.capi = api;
        RegisterFlyKeys();

        clientChannel = api.Network.RegisterChannel("EP").RegisterMessageType(typeof
            (FlyToggle)).RegisterMessageType(typeof(FlyResponse)).SetMessageHandler<FlyResponse>(new
            NetworkServerMessageHandler<FlyResponse>(this.OnClientReceived));
    }

    /// <summary>
    /// Стартует серверная сторона мода.
    /// </summary>
    /// <param name="api"></param>
    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        this.sapi = api;
        serverChannel = sapi.Network.RegisterChannel("EP").RegisterMessageType(typeof
            (FlyToggle)). RegisterMessageType(typeof(FlyResponse)).SetMessageHandler<FlyToggle>(new
            NetworkClientMessageHandler<FlyToggle>(this.OnClientSent));
        api.Event.RegisterGameTickListener(new Action<float>(this.onTickItem), 1000);
        api.Event.RegisterGameTickListener(new Action<float>(this.onTickCheckFly), 1000, 200);
    }

    /// <summary>
    /// Тики для проверки и обновления состояния полета.
    /// </summary>
    /// <param name="dt"></param>
    private void onTickItem(float dt)
    {
        double totalHours = this.sapi!.World.Calendar.TotalHours;
        double num = totalHours - this.lastCheckTotalHours;
        if (num <= 0.05)
            return;
        foreach (IPlayer player in this.sapi!.World.AllOnlinePlayers)
        {
            var inventory = player.InventoryManager.GetOwnInventory("character");
            if (inventory == null) continue;

            var (armor, itemSlot) = FindEquippedArmor(inventory);
            if (armor != null)
            {
                consumeFly = armor.consumefly;
                speedFly = armor.flySpeed;
                consume = armor.consume;

                UpdateArmorEnergy(itemSlot, player, num);
            }
        }
        this.lastCheckTotalHours = totalHours;
    }



    /// <summary>
    /// Метод для поиска экипированного нагрудника в инвентаре игрока.
    /// </summary>
    /// <param name="inventory"></param>
    /// <returns></returns>
    private (EArmor?, ItemSlot?) FindEquippedArmor(IInventory inventory)
    {
        foreach (int slot in ArmorSlots)
        {
            var itemSlot = inventory[slot];
            if (itemSlot?.Itemstack?.Collectible is EArmor armor)
            {
                return (armor, itemSlot);
            }
        }
        return (null!, inventory[13]);
    }

    /// <summary>
    /// Обновляет энергию брони и проверяет возможность полета.
    /// </summary>
    /// <param name="itemSlot"></param>
    /// <param name="player"></param>
    /// <param name="timeDelta"></param>
    private void UpdateArmorEnergy(ItemSlot itemSlot, IPlayer player, double timeDelta)
    {
        if (itemSlot?.Itemstack == null) return;

        int energy = itemSlot.Itemstack.Attributes.GetInt("durability") * consume;
        
        if (energy >= consumeFly / timeDelta)
        {
            if (IsValidForFlight(itemSlot, player))
            {
                ApplyEnergyConsumption(itemSlot, timeDelta);
            }
        }
        else
        {
            DisableFlight(itemSlot);
        }
    }

    /// <summary>
    /// Можно ли летать с данным предметом?
    /// </summary>
    /// <param name="itemSlot"></param>
    /// <param name="player"></param>
    /// <returns></returns>
    private bool IsValidForFlight(ItemSlot itemSlot, IPlayer player)
    {
        return itemSlot.Itemstack.Attributes.GetBool("flying") &&
               itemSlot.Inventory.CanPlayerAccess(player, player.Entity.Pos) &&
               !HasAngelBelt(player);
    }

    /// <summary>
    /// Применяет потребление энергии для полета.
    /// </summary>
    /// <param name="itemSlot"></param>
    /// <param name="timeDelta"></param>
    private void ApplyEnergyConsumption(ItemSlot itemSlot, double timeDelta)
    {
        int baseConsume = MyMiniLib.GetAttributeInt(itemSlot.Itemstack.Item, "consume", 20);
        int damage = (int)(consumeFly / timeDelta / baseConsume);
        
        itemSlot.Itemstack.Item.DamageItem(sapi.World, null, itemSlot, damage);
        itemSlot.MarkDirty();
    }

    /// <summary>
    /// Отмена полета для предмета.
    /// </summary>
    /// <param name="itemSlot"></param>
    private void DisableFlight(ItemSlot itemSlot)
    {
        itemSlot.Itemstack.Attributes.SetBool("flying", false);
        itemSlot.MarkDirty();
    }

    /// <summary>
    /// Проверяет, есть ли у игрока пояс ангела.
    /// </summary>
    /// <param name="player"></param>
    /// <returns></returns>
    private bool HasAngelBelt(IPlayer player)
    {
        var inventory = player.InventoryManager.GetOwnInventory("character");
        var waistSlot = inventory[(int)EnumCharacterDressType.Waist];
        return waistSlot?.Itemstack?.Item.FirstCodePart().Contains("angelbelt") == true;
    }


    private void onTickCheckFly(float dt)
    {
        foreach (IPlayer allOnlinePlayer in this.sapi!.World.AllOnlinePlayers)
        {
            IInventory ownInventory = allOnlinePlayer.InventoryManager.GetOwnInventory("character");
            if (ownInventory != null)
            {
                var itemSlot = FindEquippedArmor(ownInventory).Item2;
                if (itemSlot != null)
                {
                    if ((itemSlot.Itemstack?.Attributes.GetBool("flying") ?? false)
                        && itemSlot.Inventory.CanPlayerAccess(allOnlinePlayer, allOnlinePlayer.Entity.Pos))
                    {
                        if (allOnlinePlayer.WorldData.FreeMove != true)
                        {
                            api!.World.PlaySoundAt(new AssetLocation("game:sounds/effect/translocate-active"),
                                allOnlinePlayer);
                            allOnlinePlayer.WorldData.FreeMove = true;
                            allOnlinePlayer.Entity.Properties.FallDamageMultiplier = 0f;
                            allOnlinePlayer.WorldData.MoveSpeedMultiplier = speedFly;
                            allOnlinePlayer.WorldData.EntityControls.MovespeedMultiplier = speedFly;
                            ((IServerPlayer)allOnlinePlayer).BroadcastPlayerData();
                        }
                    }
                    else if (itemSlot.Inventory.CanPlayerAccess(allOnlinePlayer, allOnlinePlayer.Entity.Pos) &&
                        allOnlinePlayer.WorldData.CurrentGameMode != EnumGameMode.Creative &&
                        allOnlinePlayer.WorldData.CurrentGameMode != EnumGameMode.Spectator &&
                        ownInventory[(int)EnumCharacterDressType.Waist]?.Itemstack?.Item.FirstCodePart().Contains("angelbelt") != true)
                    {
                        if (allOnlinePlayer.WorldData.FreeMove)
                        {
                            api!.World.PlaySoundAt(new AssetLocation("game:sounds/effect/translocate-breakdimension"), allOnlinePlayer);
                            allOnlinePlayer.Entity.PositionBeforeFalling = allOnlinePlayer.Entity.Pos.XYZ;
                            allOnlinePlayer.WorldData.FreeMove = false;
                            allOnlinePlayer.Entity.Properties.FallDamageMultiplier = 1.0f;
                            allOnlinePlayer.WorldData.MoveSpeedMultiplier = 1f;
                            allOnlinePlayer.WorldData.EntityControls.MovespeedMultiplier = 1f;
                            allOnlinePlayer.WorldData.FreeMovePlaneLock = EnumFreeMovAxisLock.None;
                            ((IServerPlayer)allOnlinePlayer).BroadcastPlayerData();
                        }
                    }

                }
                else if (itemSlot.Inventory.CanPlayerAccess(allOnlinePlayer, allOnlinePlayer.Entity.Pos) &&
                    allOnlinePlayer.WorldData.CurrentGameMode != EnumGameMode.Creative &&
                    allOnlinePlayer.WorldData.CurrentGameMode != EnumGameMode.Spectator &&
                    ownInventory[(int)EnumCharacterDressType.Waist]?.Itemstack?.Item.FirstCodePart().Contains("angelbelt") != true)
                {
                    if (allOnlinePlayer.WorldData.FreeMove)
                    {
                        api!.World.PlaySoundAt(new AssetLocation("game:sounds/effect/translocate-breakdimension"), allOnlinePlayer);
                        allOnlinePlayer.Entity.PositionBeforeFalling = allOnlinePlayer.Entity.Pos.XYZ;
                        allOnlinePlayer.WorldData.FreeMove = false;
                        allOnlinePlayer.Entity.Properties.FallDamageMultiplier = 1.0f;
                        allOnlinePlayer.WorldData.MoveSpeedMultiplier = 1f;
                        allOnlinePlayer.WorldData.EntityControls.MovespeedMultiplier = 1f;
                        allOnlinePlayer.WorldData.FreeMovePlaneLock = EnumFreeMovAxisLock.None;
                        ((IServerPlayer)allOnlinePlayer).BroadcastPlayerData();
                    }
                }
            }
        }
    }

    private void OnClientSent(IPlayer fromPlayer, FlyToggle bt)
    {
        if (fromPlayer == null || bt == null)
            return;
        bool successful = Toggle(fromPlayer, bt);
        FlyResponse bres = new FlyResponse();
        if (successful)
        {
            bres.response = "success";
            serverChannel!.SendPacket<FlyResponse>(bres, fromPlayer as IServerPlayer);
        }
        else
        {
            bres.response = "fail";
            serverChannel!.SendPacket<FlyResponse>(bres, fromPlayer as IServerPlayer);
        }
    }

    private void OnClientReceived(FlyResponse response)
    {
        if (response.response == "success")
        {
            return;
        }
        else if (response.response == "fail")
        {
            capi!.ShowChatMessage("Не удалось включить режим полета");
        }
        else
        {
            capi!.ShowChatMessage("Ответ переключения режима неизвестен: " + response.response);
        }
    }

    public bool Toggle(IPlayer player, FlyToggle bt)
    {
        var ownInventory = player.InventoryManager.GetOwnInventory("character"); 
        var itemSlot = FindEquippedArmor(ownInventory).Item2;
        if (itemSlot == null)
            return false;
        if (!itemSlot.Itemstack.Attributes.GetBool("flying") &&
            itemSlot.Itemstack.Attributes.GetInt("durability")*consume > consumeFly / 0.05)
        {
            itemSlot.Itemstack.Attributes.SetBool("flying", true);
            itemSlot.MarkDirty();
            api!.World.PlaySoundAt(new AssetLocation("game:sounds/effect/translocate-active"), player);
            player.WorldData.FreeMove = true;
            player.Entity.Properties.FallDamageMultiplier = 0f;
            player.WorldData.MoveSpeedMultiplier = speedFly;
            player.WorldData.EntityControls.MovespeedMultiplier = speedFly;
            ((IServerPlayer)player).BroadcastPlayerData();
        }
        else
        {
            itemSlot.Itemstack.Attributes.SetBool("flying", false);
            itemSlot.MarkDirty();
            api!.World.PlaySoundAt(new AssetLocation("game:sounds/effect/translocate-breakdimension"), player);
            player.Entity.PositionBeforeFalling = player.Entity.Pos.XYZ;
            player.WorldData.FreeMove = false;
            player.Entity.Properties.FallDamageMultiplier = 1.0f;
            player.WorldData.MoveSpeedMultiplier = 1f;
            player.WorldData.EntityControls.MovespeedMultiplier = 1f;
            player.WorldData.FreeMovePlaneLock = EnumFreeMovAxisLock.None;
            ((IServerPlayer)player).BroadcastPlayerData();
        }

        return true;
    }

    private bool OnFlyKeyPressed(KeyCombination comb)
    {
        if (api!.Side != EnumAppSide.Client)
            return false;
        base.Mod.Logger.VerboseDebug("AngelBelt Fly Key Pressed");
        bool hasBelt = PlayerHasBelt();

        if (hasBelt)
        {
            FlyToggle flyToggle = new FlyToggle()
            {
                toggle = capi!.World.Player.PlayerUID,
                savedspeed = this.SavedSpeedMult,
                savedaxis = this.SavedAxis.ToString()
            };
            clientChannel!.SendPacket<FlyToggle>(flyToggle);
            return true;
        }

        return false;
    }

    private void RegisterFlyKeys()
    {
        base.Mod.Logger.VerboseDebug("FlyToggle: flight hotkey handler for R");
        this.capi!.Input.RegisterHotKey("FlyToggle", "Enable Fly mode Armorchest", GlKeys.R, HotkeyType.CharacterControls);
        this.capi.Input.SetHotKeyHandler("FlyToggle", OnFlyKeyPressed);
    }

    private bool PlayerHasBelt()
    {
        if (api!.Side == EnumAppSide.Client)
        {
            if (capi!.World == null)
            {
                return false;
            }

            if (capi!.World.Player == null)
            {
                return false;
            }

            if (capi!.World.Player.InventoryManager == null)
            {
                return false;
            }

            IInventory ownInventory = this.capi.World.Player.InventoryManager.GetOwnInventory("character");
            if (ownInventory == null)
            {
                return false;
            }

            var beltslot = FindEquippedArmor(ownInventory).Item2;
            if (beltslot == null)
            {
                return false;
            }

            if (beltslot.Empty)
            {
                return false;
            }

            if (beltslot.Itemstack.Item.FirstCodePart()
                .Contains("static"))
            {
                return true;
            }
        }

        return false;
    }
}