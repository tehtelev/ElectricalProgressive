using ElectricalProgressive.Content.Block.ECentrifuge;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using ElectricalProgressive.Content.Block.EWoodcutter;
using ElectricalProgressive.Patches;


[assembly: ModDependency("game", "1.21.0-rc.3")]
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
        api.RegisterBlockClass("BlockECentrifuge", typeof(BlockECentrifuge));
        api.RegisterBlockEntityClass("BlockEntityECentrifuge", typeof(BlockEntityECentrifuge));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorECentrifuge", typeof(BEBehaviorECentrifuge));
        
        api.RegisterBlockClass("BlockEHammer", typeof(BlockEHammer));
        api.RegisterBlockEntityClass("BlockEntityEHammer", typeof(BlockEntityEHammer));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEHammer", typeof(BEBehaviorEHammer));

        api.RegisterBlockClass("BlockEWoodcutter", typeof(BlockEWoodcutter));
        api.RegisterBlockEntityClass("BlockEntityEWoodcutter", typeof(BlockEntityEWoodcutter));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEWoodcutter", typeof(BEBehaviorEWoodcutter));
        
    }        
    
    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        this.capi = api;
        HandbookPatch.ApplyPatches(api);


    }

}




