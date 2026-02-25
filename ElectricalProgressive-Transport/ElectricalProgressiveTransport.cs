using ElectricalProgressive.Content;
using ElectricalProgressive.Content.EWaterPump;
using ElectricalProgressive.Content.ItemInsertionPipe;
using ElectricalProgressive.Content.LiquidInsertionPipe;
using ElectricalProgressive.Content.NetworkPipe;
using ElectricalProgressive.Content.NormalPipe;
using Vintagestory.API.Common;

[assembly: ModDependency("game", "1.21.6")]
[assembly: ModInfo(
    "Electrical Progressive: Transport",
    "electricalprogressivetransport",
    Website = "https://github.com/tehtelev/ElectricalProgressive",
    Description = "Additional electrical devices.",
    Version = "3.0.0-pre.1",
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

        // Регистрация труб
        api.RegisterBlockClass("BlockPipeBase", typeof(BlockPipeBase));
        api.RegisterBlockClass("BlockPipe", typeof(BlockPipe));
        api.RegisterBlockClass("BlockInsertionPipe", typeof(BlockItemInsertionPipe));
        api.RegisterBlockClass("BlockLiquidInsertionPipe", typeof(BlockLiquidInsertionPipe)); // НОВОЕ
        api.RegisterBlockEntityClass("BEPipe", typeof(BEPipe));
        api.RegisterBlockEntityClass("BEInsertionPipe", typeof(BEItemInsertionPipe));
        api.RegisterBlockEntityClass("BELiquidInsertionPipe", typeof(BELiquidInsertionPipe)); // НОВОЕ
        
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
    