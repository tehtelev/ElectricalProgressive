using ElectricalProgressive.Content.Block;
using ElectricalProgressive.Content.Block.EAccumulator;
using ElectricalProgressive.Content.Block.ECable;
using ElectricalProgressive.Content.Block.ECharger;
using ElectricalProgressive.Content.Block.EConnector;
using ElectricalProgressive.Content.Block.EFuelGenerator;
using ElectricalProgressive.Content.Block.EGenerator;
using ElectricalProgressive.Content.Block.EHotSpringsGenerator;
using ElectricalProgressive.Content.Block.EHotSpringsGenerator.EHeatExchanger;
using ElectricalProgressive.Content.Block.EMotor;
using ElectricalProgressive.Content.Block.ESolarGenerator;
using ElectricalProgressive.Content.Block.ESwitch;
using ElectricalProgressive.Content.Block.ETermoGenerator;
using ElectricalProgressive.Content.Block.ETransformator;
using ElectricalProgressive.Content.Block.Termoplastini;
using ElectricalProgressive.Content.Item.Tool;
using ElectricalProgressive.Patch;
using HarmonyLib;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;




[assembly: ModDependency("game", "1.22.0-rc.1")]
[assembly: ModDependency("electricalprogressivecore", "3.0.0-rc.1")]
[assembly: ModInfo(
    "Electrical Progressive: Basics",
    "electricalprogressivebasics",
    Website = "https://github.com/tehtelev/ElectricalProgressive",
    Description = "Basic electrical devices.",
    Version = "3.0.0-rc.2",
    Authors =
    [
        "Tehtelev",
        "Kotl"
    ]
)]

namespace ElectricalProgressive;


public class ElectricalProgressiveBasics : ModSystem
{
    private Harmony harmony;

    private ICoreAPI api = null!;
    private ICoreClientAPI capi = null!;

    public static AssetLocation soundElectricShok;

    /// <summary>
    /// Причины сгорания электрических блоков
    /// </summary>
    public static Dictionary<int, string> causeBurn = new()
    {
        { 1, Lang.Get("electricalprogressivebasics:causeCurrent") },
        { 2, Lang.Get("electricalprogressivebasics:causeVoltage") },
        { 3, Lang.Get("electricalprogressivebasics:causeEnvironment") }
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
        
        api.RegisterBlockClass("BlockESolarGenerator", typeof(BlockESolarGenerator));
        api.RegisterBlockEntityClass("BlockEntityESolarGenerator", typeof(BlockEntityESolarGenerator));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorSolarEGenerator", typeof(BEBehaviorSolarEGenerator));

        api.RegisterBlockClass("BlockEFuelGenerator", typeof(BlockEFuelGenerator));
        api.RegisterBlockEntityClass("BlockEntityEFuelGenerator", typeof(BlockEntityEFuelGenerator));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorFuelEGenerator", typeof(BEBehaviorFuelEGenerator));


        api.RegisterBlockClass("BlockTermoplastini", typeof(BlockTermoplastini));

        api.RegisterItemClass("ESoldering", typeof(ESoldering));

        api.RegisterBlockClass("BlockECharger", typeof(BlockECharger));
        api.RegisterBlockEntityClass("BlockEntityECharger", typeof(BlockEntityECharger));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorECharger", typeof(BEBehaviorECharger));

        api.RegisterBlockClass("BlockEHotSpringsGenerator", typeof(BlockEHotSpringsGenerator));
        api.RegisterBlockEntityClass("BlockEntityEHotSpringsGenerator", typeof(BlockEntityEHotSpringsGenerator));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorHotSpringsEGenerator", typeof(BEBehaviorHotSpringsEGenerator));

        api.RegisterBlockClass("BlockEHeatExchanger", typeof(BlockEHeatExchanger));

        

        // Регистрируем патч для механики
        harmony = new Harmony("electricalprogressive.mechanicalpowermod");
        MechanicalPowerMod_RebuildNetwork_Patch.RegisterPatch(harmony, api);

    }



    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        this.capi = api;
    }


    public override void Dispose()
    {
        // Отменяем все патчи при выгрузке мода
        MechanicalPowerMod_RebuildNetwork_Patch.UnregisterPatch(harmony);
        harmony?.UnpatchAll();
        harmony = null;
    }
}