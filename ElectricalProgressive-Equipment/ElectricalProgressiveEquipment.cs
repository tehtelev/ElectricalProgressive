using Vintagestory.API.Common;
using Vintagestory.API.Client;
using ElectricalProgressive.Content.Item.Armor;
using ElectricalProgressive.Content.Item.Weapon;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using ElectricalProgressive.Content.Item.Tool;
using HarmonyLib;
using Vintagestory.API.Common.Entities;

[assembly: ModDependency("game", "1.22.0")]
[assembly: ModDependency("electricalprogressivecore", "3.0.0")]
[assembly: ModDependency("electricalprogressivebasics", "3.0.0")]
[assembly: ModDependency("electricalprogressiveqol", "3.0.0")]
[assembly: ModInfo(
    "Electrical Progressive: Equipment",
    "electricalprogressiveequipment",
    Website = "https://github.com/tehtelev/ElectricalProgressive",
    Description = "Electric weapons, armor and tools",
    Version = "3.1.0",
    Authors =
    [
        "Tehtelev",
        "Kotl"
    ]
)]

namespace ElectricalProgressive;

public class ElectricalProgressiveEquipment : ModSystem
{
    public static bool combatoverhaul = false;
    private ICoreAPI api = null!;
    public static ICoreClientAPI capi = null!;
    public static WeatherSystemServer? WeatherSystemServer;
    private bool physicsPatched = false;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        this.api = api;

        api.RegisterCollectibleBehaviorClass("EArmor", typeof(EArmor));

        api.RegisterItemClass("EWeapon", typeof(EWeapon));
        api.RegisterItemClass("ESpear", typeof(ESpear));
        api.RegisterItemClass("EShield", typeof(EShield));

        api.RegisterItemClass("EChisel", typeof(EChisel));
        api.RegisterItemClass("EAxe", typeof(EAxe));
        api.RegisterItemClass("EDrill", typeof(EDrill));
        api.RegisterItemClass("ItemEGlider", typeof(ItemEGlider));

        api.RegisterEntity("EntityESpear", typeof(EntityESpear));

        if (api.ModLoader.IsModEnabled("combatoverhaul"))
            combatoverhaul = true;

        Harmony harmony = new Harmony("electricalprogressive.equipment");
        harmony.PatchAll();
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        capi = api;

        // Ждём появления игрока и применяем патч физики
        RegisterPhysicsPatch();
    }

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

    private void OnPlayerJoin(IClientPlayer player)
    {
        if (player?.Entity != null)
        {
            ApplyPhysicsPatch(player.Entity);
        }
    }

    private void ApplyPhysicsPatch(Entity entity)
    {
        if (physicsPatched) return;
        if (entity == null) return;

        capi.Logger.Notification("[ElectricalProgressive] Applying EGlider physics patch...");
        EGliderPhysicsPatcher.PatchPlayerPhysics(entity);
        physicsPatched = true;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        WeatherSystemServer = api.ModLoader.GetModSystem<WeatherSystemServer>();
    }
}