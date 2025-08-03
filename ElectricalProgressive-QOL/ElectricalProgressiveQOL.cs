using ElectricalProgressive.Content.Block.ECharger;
using ElectricalProgressive.Content.Block.EHorn;
using ElectricalProgressive.Content.Block.EStove;
using ElectricalProgressive.Content.Block.ELamp;
using ElectricalProgressive.Content.Block.EOven;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using ElectricalProgressive.Content.Block.EHeater;
using ElectricalProgressive.Content.Block.EFonar;
using ElectricalProgressive.Content.Block.ESFonar;
using ElectricalProgressive.Content.Block.EWoodcutter;
using ElectricalProgressive.Content.Block.EFreezer2;


[assembly: ModDependency("game", "1.21.0-rc.2")]
[assembly: ModDependency("electricalprogressivecore", "2.1.0-rc.2")]
[assembly: ModDependency("electricalprogressivebasics", "2.1.0-rc.2")]
[assembly: ModInfo(
    "Electrical Progressive: QoL",
    "electricalprogressiveqol",
    Website = "https://github.com/tehtelev/ElectricalProgressiveQOL",
    Description = "Additional electrical devices.",
    Version = "2.1.0-rc.2",
    Authors = new[] {
        "Tehtelev",
        "Kotl"
    }
)]

namespace ElectricalProgressive;

public class ElectricalProgressiveQOL : ModSystem
{

    private ICoreAPI api = null!;
    private ICoreClientAPI capi = null!;


    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        this.api = api;

        api.RegisterBlockClass("BlockEHorn", typeof(BlockEHorn));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEHorn", typeof(BEBehaviorEHorn));
        api.RegisterBlockEntityClass("BlockEntityEHorn", typeof(BlockEntityEHorn));

        api.RegisterBlockClass("BlockELamp", typeof(BlockELamp));
        api.RegisterBlockClass("BlockESmallLamp", typeof(BlockESmallLamp));

        api.RegisterBlockEntityClass("BlockEntityELamp", typeof(BlockEntityELamp));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorELamp", typeof(BEBehaviorELamp));


        api.RegisterBlockClass("BlockEFonar", typeof(BlockEFonar));
        api.RegisterBlockEntityClass("BlockEntityEFonar", typeof(BlockEntityEFonar));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEFonar", typeof(BEBehaviorEFonar));

        api.RegisterBlockClass("BlockESFonar", typeof(BlockESFonar));
        api.RegisterBlockEntityClass("BlockEntityESFonar", typeof(BlockEntityESFonar));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorESFonar", typeof(BEBehaviorESFonar));

        api.RegisterBlockClass("BlockEHeater", typeof(BlockEHeater));
        api.RegisterBlockEntityClass("BlockEntityEHeater", typeof(BlockEntityEHeater));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEHeater", typeof(BEBehaviorEHeater));

        api.RegisterBlockClass("BlockECharger", typeof(BlockECharger));
        api.RegisterBlockEntityClass("BlockEntityECharger", typeof(BlockEntityECharger));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorECharger", typeof(BEBehaviorECharger));


        api.RegisterBlockClass("BlockEStove", typeof(BlockEStove));
        api.RegisterBlockEntityClass("BlockEntityEStove", typeof(BlockEntityEStove));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEStove", typeof(BEBehaviorEStove));

        //холодильник с анимацией
        api.RegisterBlockClass("BlockEFreezer2", typeof(BlockEFreezer2));
        api.RegisterBlockEntityClass("BlockEntityEFreezer2", typeof(BlockEntityEFreezer2));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEFreezer2", typeof(BEBehaviorEFreezer2));


        api.RegisterBlockClass("BlockEOven", typeof(BlockEOven));
        api.RegisterBlockEntityClass("BlockEntityEOven", typeof(BlockEntityEOven));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEOven", typeof(BEBehaviorEOven));

        api.RegisterBlockClass("BlockEWoodcutter", typeof(BlockEWoodcutter));
        api.RegisterBlockEntityClass("BlockEntityEWoodcutter", typeof(BlockEntityEWoodcutter));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEWoodcutter", typeof(BEBehaviorEWoodcutter));



    }



    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        this.capi = api;


    }

}




