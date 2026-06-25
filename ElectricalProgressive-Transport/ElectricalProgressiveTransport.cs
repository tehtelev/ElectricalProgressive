using ElectricalProgressive.Content;
using ElectricalProgressive.Content.ItemInsertionPipe;
using ElectricalProgressive.Content.LiquidInsertionPipe;
using ElectricalProgressive.Content.NetworkPipe;
using ElectricalProgressive.Content.NormalPipe;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

[assembly: ModDependency("game", "1.22.0")]
[assembly: ModInfo(
    "Electrical Progressive: Transport",
    "electricalprogressivetransport",
    Website = "https://github.com/tehtelev/ElectricalProgressive",
    Description = "Pipeline transport system",
    Version = "1.0.0",
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
    private PipeItemTransitRenderer itemTransitRenderer;

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
        
        // Инициализация менеджера сетей
        networkManager = new PipeNetworkManager();
        networkManager.Initialize(api);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);

        itemTransitRenderer = new PipeItemTransitRenderer(api);
        PipeItemTransitRenderer.Instance = itemTransitRenderer;
        api.Event.RegisterRenderer(itemTransitRenderer, EnumRenderStage.Opaque, "ep-pipe-item-transit");
    }

    public override void Dispose()
    {
        PipeItemTransitRenderer.Instance = null;
        itemTransitRenderer = null;
        base.Dispose();
    }

    public PipeNetworkManager GetNetworkManager()
    {
        return networkManager;
    }
}
    
