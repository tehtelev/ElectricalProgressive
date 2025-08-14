using ElectricalProgressive.Content.Block.EAccumulator;
using ElectricalProgressive.Content.Block.EConnector;
using ElectricalProgressive.Content.Block.EGenerator;
using ElectricalProgressive.Content.Block.EMotor;
using Vintagestory.API.Common;
using ElectricalProgressive.Content.Block;
using ElectricalProgressive.Content.Block.ESwitch;
using Vintagestory.API.Client;
using ElectricalProgressive.Content.Block.ECable;
using ElectricalProgressive.Content.Block.ETransformator;
using ElectricalProgressive.Content.Block.ETermoGenerator;
using ElectricalProgressive.Content.Block.Termoplastini;
using Vintagestory.API.Config;
using System.Collections.Generic;




[assembly: ModDependency("game", "1.21.0-rc.4")]
[assembly: ModDependency("electricalprogressivecore", "2.1.0-rc.4")]
[assembly: ModInfo(
    "Electrical Progressive: Basics",
    "electricalprogressivebasics",
    Website = "https://github.com/tehtelev/ElectricalProgressive",
    Description = "Basic electrical devices.",
    Version = "2.1.0-rc.4",
    Authors = new[] {
        "Tehtelev",
        "Kotl"
    }
)]

namespace ElectricalProgressive;


public class ElectricalProgressiveBasics : ModSystem
{

    private ICoreAPI api = null!;
    private ICoreClientAPI capi = null!;

    /// <summary>
    /// Причины сгорания электрических блоков
    /// </summary>
    public static Dictionary<int, string> causeBurn = new Dictionary<int, string>
    {
        { 1, Lang.Get("causeCurrent") },
        { 2, Lang.Get("causeVoltage") },
        { 3, Lang.Get("causeEnvironment") }
    };



    /// <summary>
    /// Старт общего потока
    /// </summary>
    /// <param name="api"></param>
    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        this.api = api;

        api.RegisterBlockClass("BlockECable", typeof(BlockECable));
        api.RegisterBlockEntityClass("BlockEntityECable", typeof(BlockEntityECable));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorECable", typeof(BEBehaviorECable));

        api.RegisterBlockClass("BlockESwitch", typeof(BlockESwitch));

        api.RegisterBlockClass("BlockEAccumulator", typeof(BlockEAccumulator));
        api.RegisterBlockEntityClass("BlockEntityEAccumulator", typeof(BlockEntityEAccumulator));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEAccumulator", typeof(BEBehaviorEAccumulator));

        api.RegisterBlockClass("BlockConnector", typeof(BlockConnector));
        api.RegisterBlockEntityClass("BlockEntityEConnector", typeof(BlockEntityEConnector));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEConnector", typeof(BEBehaviorEConnector));


        api.RegisterBlockClass("BlockETransformator", typeof(BlockETransformator));
        api.RegisterBlockEntityClass("BlockEntityETransformator", typeof(BlockEntityETransformator));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorETransformator", typeof(BEBehaviorETransformator));



        api.RegisterBlockClass("BlockEMotor", typeof(BlockEMotor));
        api.RegisterBlockEntityClass("BlockEntityEMotor", typeof(BlockEntityEMotor));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEMotor", typeof(BEBehaviorEMotor));


        api.RegisterBlockClass("BlockEGenerator", typeof(BlockEGenerator));
        api.RegisterBlockEntityClass("BlockEntityEGenerator", typeof(BlockEntityEGenerator));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEGenerator", typeof(BEBehaviorEGenerator));


        api.RegisterBlockEntityBehaviorClass("ElectricalProgressive", typeof(BEBehaviorElectricalProgressive));


        api.RegisterBlockClass("BlockETermoGenerator", typeof(BlockETermoGenerator));
        api.RegisterBlockEntityClass("BlockEntityETermoGenerator", typeof(BlockEntityETermoGenerator));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorTermoEGenerator", typeof(BEBehaviorTermoEGenerator));

        api.RegisterBlockClass("BlockTermoplastini", typeof(BlockTermoplastini));


    }






    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        this.capi = api;
    }

}