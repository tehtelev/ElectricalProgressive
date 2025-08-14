using Vintagestory.API.Common;
using Vintagestory.API.Client;
using ElectricalProgressive.Content.Item.Armor;
using ElectricalProgressive.Content.Item.Weapon;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using ElectricalProgressive.Content.Item.Tool;



[assembly: ModDependency("game", "1.20.0")]
[assembly: ModDependency("electricalprogressivecore", "2.0.1")]
[assembly: ModDependency("electricalprogressivebasics", "2.0.1")]
[assembly: ModDependency("electricalprogressiveqol", "2.0.1")]
[assembly: ModInfo(
    "Electrical Progressive: Equipment",
    "electricalprogressiveequipment",
    Website = "https://github.com/tehtelev/ElectricalProgressive",
    Description = "Brings electricity into the game!",
    Version = "2.0.1",
    Authors = new[] {
        "Tehtelev",
        "Kotl"
    }
)]

namespace ElectricalProgressive;

public class ElectricalProgressiveEquipment : ModSystem
{

    public static bool combatoverhaul = false;                        //установлен ли combatoverhaul
    private ICoreAPI api = null!;
    private ICoreClientAPI capi = null!;
    public static WeatherSystemServer? WeatherSystemServer;



    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        this.api = api;

        api.RegisterItemClass("EArmor", typeof(EArmor));
        api.RegisterItemClass("EWeapon", typeof(EWeapon));
        api.RegisterItemClass("ESpear", typeof(ESpear));
        api.RegisterItemClass("EShield", typeof(EShield));

        api.RegisterItemClass("EChisel", typeof(EChisel));
        api.RegisterItemClass("EAxe", typeof(EAxe));
        api.RegisterItemClass("EDrill", typeof(EDrill));


        api.RegisterEntity("EntityESpear", typeof(EntityESpear));

        if (api.ModLoader.IsModEnabled("combatoverhaul"))
            combatoverhaul = true;



    }



    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        this.capi = api;
    }


    /// <summary>
    /// Серверная сторона
    /// </summary>
    /// <param name="api"></param>
    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);
                    
        WeatherSystemServer = api.ModLoader.GetModSystem<WeatherSystemServer>();

        }
}