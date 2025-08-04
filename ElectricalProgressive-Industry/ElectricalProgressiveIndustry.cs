using Vintagestory.API.Common;
using Vintagestory.API.Client;


[assembly: ModDependency("game", "1.21.0-rc.2")]
[assembly: ModDependency("electricalprogressivecore", "2.1.0-rc.2")]
[assembly: ModDependency("electricalprogressivebasics", "2.1.0-rc.2")]
[assembly: ModInfo(
    "Electrical Progressive: Industry",
    "electricalprogressiveindustry",
    Website = "https://github.com/tehtelev/ElectricalProgressive",
    Description = "Additional electrical devices.",
    Version = "2.1.0-rc.2",
    Authors = new[] {
        "Tehtelev",
        "Kotl"
    }
)]

namespace ElectricalProgressive;

public class ElectricalProgressiveIndustry : ModSystem
{

    private ICoreAPI api = null!;
    private ICoreClientAPI capi = null!;


    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        this.api = api;


    }



    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        this.capi = api;


    }

}




