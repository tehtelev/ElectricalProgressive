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
    
    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
        WeatherSystemServer = api.ModLoader.GetModSystem<WeatherSystemServer>();
    }
}