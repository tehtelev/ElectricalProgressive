using System;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Server;
using ElectricalProgressive.Content.Item.Armor;
using ElectricalProgressive.Net;


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

    int consume = 20; // Количество энергии, потребляемое в секунду при полете

    public override bool ShouldLoad(EnumAppSide forSide)
    {
        return true;
    }

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        this.api = api;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        this.capi = api;
        RegisterFlyKeys();

        clientChannel = api.Network.RegisterChannel("EP").RegisterMessageType(typeof
            (FlyToggle)).RegisterMessageType(typeof(FlyResponse)).SetMessageHandler<FlyResponse>(new
            NetworkServerMessageHandler<FlyResponse>(this.OnClientReceived));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        this.sapi = api;
        serverChannel = sapi.Network.RegisterChannel("EP").RegisterMessageType(typeof
            (FlyToggle)).RegisterMessageType(typeof(FlyResponse)).SetMessageHandler<FlyToggle>(new
            NetworkClientMessageHandler<FlyToggle>(this.OnClientSent));
        api.Event.RegisterGameTickListener(new Action<float>(this.onTickItem), 1000);
        api.Event.RegisterGameTickListener(new Action<float>(this.onTickCheckFly), 1000, 200);
    }
    private void onTickItem(float dt)
    {
        double totalHours = this.sapi!.World.Calendar.TotalHours;
        double num = totalHours - this.lastCheckTotalHours;
        if (num <= 0.05)
            return;
        foreach (IPlayer allOnlinePlayer in this.sapi.World.AllOnlinePlayers)
        {
            IInventory ownInventory = allOnlinePlayer.InventoryManager.GetOwnInventory("character");
            if (ownInventory != null)
            {
                ItemSlot itemSlot = ownInventory[ElectricalProgressiveEquipment.combatoverhaul ? 34 : 13];
                if (itemSlot.Itemstack?.Collectible is EArmor collectible)
                {
                    consumeFly = collectible.consumefly;
                    speedFly = collectible.flySpeed;
                    consume = collectible.consume;

                    int energy = itemSlot.Itemstack.Attributes.GetInt("durability") * consume;
                    if (energy >= consumeFly / num)
                    {
                        if (itemSlot.Itemstack.Attributes.GetBool("flying") &&
                            itemSlot.Inventory.CanPlayerAccess(allOnlinePlayer, allOnlinePlayer.Entity.Pos) &&
                            ownInventory[(int)EnumCharacterDressType.Waist]?.Itemstack?.Item.FirstCodePart().Contains("angelbelt") != true)
                        {
                            //collectible.receiveEnergy(itemSlot.Itemstack, -(int)(consumeFly / num));

                            int consume = MyMiniLib.GetAttributeInt(itemSlot.Itemstack.Item, "consume", 20);     //размер минимальной порции   
                            int damage = (int)(consumeFly / num/ consume);                         //рассчитываем удар по проч
                            itemSlot.Itemstack.Item.DamageItem(sapi.World, null, itemSlot, damage); // уменьшаем прочность на размер порции


                            itemSlot.MarkDirty();
                        }
                    }
                    else
                    {
                        itemSlot.Itemstack.Attributes.SetBool("flying", false);
                        itemSlot.MarkDirty();
                    }
                }
            }
        }
        this.lastCheckTotalHours = totalHours;
    }



    private void onTickCheckFly(float dt)
    {
        foreach (IPlayer allOnlinePlayer in this.sapi!.World.AllOnlinePlayers)
        {
            IInventory ownInventory = allOnlinePlayer.InventoryManager.GetOwnInventory("character");
            if (ownInventory != null)
            {
                ItemSlot itemSlot = ownInventory[ElectricalProgressiveEquipment.combatoverhaul ? 34 : 13];
                if (itemSlot.Itemstack?.Collectible is EArmor collectible)
                {
                    if (itemSlot.Itemstack.Attributes.GetBool("flying") &&
                        itemSlot.Inventory.CanPlayerAccess(allOnlinePlayer, allOnlinePlayer.Entity.Pos))
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
        ItemSlot itemSlot = player.InventoryManager.GetOwnInventory("character")[ElectricalProgressiveEquipment.combatoverhaul ? 34 : 13];
        if (itemSlot == null) return false;
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

            ItemSlot beltslot = ownInventory[ElectricalProgressiveEquipment.combatoverhaul ? 34 : 13];
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