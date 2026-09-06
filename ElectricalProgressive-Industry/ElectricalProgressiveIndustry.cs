using ElectricalProgressive.Content.Block.EBlastFurnace;
using ElectricalProgressive.Content.Block.ECrusher;
using ElectricalProgressive.Content.Block.ERecycler;
using ElectricalProgressive.Content.Block.EExtruder;
using ElectricalProgressive.Content.Block.EHammer;
using ElectricalProgressive.Content.Block.EMetalForming;
using ElectricalProgressive.Content.Block.EPress;
using ElectricalProgressive.Content.Block.ESieve;
using ElectricalProgressive.Content.Block.EWoodcutter;
using ElectricalProgressive.Content.Block.Gauge;
using ElectricalProgressive.Content.Block.PressForm;
using ElectricalProgressive.Content.EAquaAccum;
using ElectricalProgressive.Patch;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;


[assembly: ModDependency("game", "1.22.0")]
[assembly: ModDependency("electricalprogressivecore", "3.1.0")]
[assembly: ModDependency("electricalprogressivebasics", "3.1.0")]
[assembly: ModInfo(
    "Electrical Progressive: Industry",
    "electricalprogressiveindustry",
    Website = "https://github.com/tehtelev/ElectricalProgressive",
    Description = "Additional electrical devices.",
    Version = "0.7.0",
    Authors =
    [
        "Tehtelev",
        "Kotl"
    ]
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
        api.RegisterBlockClass("BlockERecycler", typeof(BlockERecycler));
        api.RegisterBlockEntityClass("BlockEntityERecycler", typeof(BlockEntityERecycler));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorERecycler", typeof(BEBehaviorERecycler));
        
        api.RegisterBlockClass("BlockEHammer", typeof(BlockEHammer));
        api.RegisterBlockEntityClass("BlockEntityEHammer", typeof(BlockEntityEHammer));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEHammer", typeof(BEBehaviorEHammer));

        api.RegisterBlockClass("BlockEMetalForming", typeof(BlockEMetalForming));
        api.RegisterBlockEntityClass("BlockEntityEMetalForming", typeof(BlockEntityEMetalForming));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEMetalForming", typeof(BEBehaviorEMetalForming));
        
        api.RegisterBlockClass("BlockEPress", typeof(BlockEPress));
        api.RegisterBlockEntityClass("BlockEntityEPress", typeof(BlockEntityEPress));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEPress", typeof(BEBehaviorEPress));

        api.RegisterBlockClass("BlockEExtruder", typeof(BlockEExtruder));
        api.RegisterBlockEntityClass("BlockEntityEExtruder", typeof(BlockEntityEExtruder));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEExtruder", typeof(BEBehaviorEExtruder));

        api.RegisterBlockClass("BlockEWoodcutter", typeof(BlockEWoodcutter));
        api.RegisterBlockEntityClass("BlockEntityEWoodcutter", typeof(BlockEntityEWoodcutter));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEWoodcutter", typeof(BEBehaviorEWoodcutter));
        
        api.RegisterBlockClass("BlockEAquaAccum", typeof(BlockEAquaAccum));
        api.RegisterBlockEntityClass("BlockEntityEAquaAccum", typeof(BlockEntityEAquaAccum));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEAquaAccum", typeof(BEBehaviorEAquaAccum));
        
        api.RegisterBlockClass("BlockECrusher", typeof(BlockECrusher));
        api.RegisterBlockEntityClass("BlockEntityECrusher", typeof(BlockEntityECrusher));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorECrusher", typeof(BEBehaviorECrusher));
        
        api.RegisterBlockClass("BlockEBlastFurnace", typeof(BlockEBlastFurnace));
        api.RegisterBlockEntityClass("BlockEntityEBlastFurnace", typeof(BlockEntityEBlastFurnace));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEBlastFurnace", typeof(BEBehaviorEBlastFurnace));
        
        api.RegisterBlockClass("BlockESieve", typeof(BlockESieve));
        api.RegisterBlockEntityClass("BlockEntityESieve", typeof(BlockEntityESieve));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorESieve", typeof(BEBehaviorESieve));

        api.RegisterBlockClass("BlockPressForm", typeof(BlockPressForm));

        api.RegisterBlockClass("BlockGauge", typeof(BlockGauge));

    }        
    
    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        this.capi = api;
        HandbookPatch.ApplyPatches(api);
    }

}




