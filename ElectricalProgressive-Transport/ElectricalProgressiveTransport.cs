using ElectricalProgressive.Content;
using ElectricalProgressive.Content.EWaterPump;
using ElectricalProgressive.Content.ItemInsertionPipe;
using ElectricalProgressive.Content.LiquidInsertionPipe;
using ElectricalProgressive.Content.NetworkPipe;
using ElectricalProgressive.Content.NormalPipe;
using Vintagestory.API.Common;

[assembly: ModDependency("game", "1.22.0-rc.1")]
[assembly: ModDependency("electricalprogressivecore", "3.0.0-rc.3")]
[assembly: ModDependency("electricalprogressivebasics", "3.0.0-rc.3")]
[assembly: ModInfo(
    "Electrical Progressive: Transport",
    "electricalprogressivetransport",
    Website = "https://github.com/tehtelev/ElectricalProgressive",
    Description = "Pipeline transport system",
    Version = "1.0.0-rc.1",
    Authors =
    [
        "Tehtelev",
        "Kotl"
    ]
)]


namespace ElectricalProgressive;

public class ElectricalProgressiveTransport : ModSystem
{
    private PipeNetworkManager networkManager;
    private static ElectricalProgressiveTransport instance;

    public static ElectricalProgressiveTransport Instance => instance;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        instance = this;
        
        // Регистрация 
        api.RegisterBlockClass("BlockPipeBase", typeof(BlockPipeBase));
        api.RegisterBlockClass("BlockPipe", typeof(BlockPipe));
        api.RegisterBlockClass("BlockInsertionPipe", typeof(BlockItemInsertionPipe));
        api.RegisterBlockClass("BlockLiquidInsertionPipe", typeof(BlockLiquidInsertionPipe)); 

        api.RegisterBlockEntityClass("BEPipe", typeof(BEPipe));
        api.RegisterBlockEntityClass("BEInsertionPipe", typeof(BEItemInsertionPipe));
        api.RegisterBlockEntityClass("BELiquidInsertionPipe", typeof(BELiquidInsertionPipe)); 
        
        api.RegisterBlockClass("BlockEWaterPump", typeof(BlockEWaterPump));
        api.RegisterBlockEntityClass("BlockEntityEWaterPump", typeof(BlockEntityEWaterPump));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEWaterPump", typeof(BEBehaviorEWaterPump));

        // Инициализация менеджера сетей
        networkManager = new PipeNetworkManager();
        networkManager.Initialize(api);
    }

    public PipeNetworkManager GetNetworkManager()
    {
        return networkManager;
    }
}
    