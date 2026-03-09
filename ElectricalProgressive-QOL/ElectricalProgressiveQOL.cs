using ElectricalProgressive.Content.Block.EFence;
using ElectricalProgressive.Content.Block.EFonar;
using ElectricalProgressive.Content.Block.EFreezer;
using ElectricalProgressive.Content.Block.EFridge;
using ElectricalProgressive.Content.Block.EFruitPress;
using ElectricalProgressive.Content.Block.EHeatCannon;
using ElectricalProgressive.Content.Block.EHeater;
using ElectricalProgressive.Content.Block.EHorn;
using ElectricalProgressive.Content.Block.ELamp;
using ElectricalProgressive.Content.Block.EOven;
using ElectricalProgressive.Content.Block.ESFonar;
using ElectricalProgressive.Content.Block.EStove;
using ElectricalProgressive.Patch;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;



[assembly: ModDependency("game", "1.22.0-rc.1")]
[assembly: ModDependency("electricalprogressivecore", "3.0.0-rc.3")]
[assembly: ModDependency("electricalprogressivebasics", "3.0.0-rc.3")]
[assembly: ModInfo(
    "Electrical Progressive: QoL",
    "electricalprogressiveqol",
    Website = "https://github.com/tehtelev/ElectricalProgressive",
    Description = "Additional electrical devices.",
    Version = "3.0.0-rc.3",
    Authors =
    [
        "Tehtelev",
        "Kotl"
    ]
)]

namespace ElectricalProgressive;

public class ElectricalProgressiveQOL : ModSystem
{
    private Harmony harmony;
    private ICoreAPI api = null!;
    private ICoreClientAPI capi = null!;

    // xskills ------------------------------------------------------------
    public static bool xskillsEnabled = false;

    // Ссылки на типы и методы XSkills/XLib
    public static Assembly asmXSkills;
    public static Assembly asmXLib;
    public static Type typeXLeveling;
    public static Type typeCooking;
    public static Type typeBlockEntityBehaviorOwnable;

    public static Type typeCookingUtil;
    public static MethodInfo methodGetCookingTimeMultiplier;

    public static object xLevelingInstance;
    public static MethodInfo methodGetSkill;


    // --------------------------------------------------------


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

        api.RegisterBlockClass("BlockEHeatCannon", typeof(BlockEHeatCannon));
        api.RegisterBlockEntityClass("BlockEntityEHeatCannon", typeof(BlockEntityEHeatCannon));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEHeatCannon", typeof(BEBehaviorEHeatCannon));




        api.RegisterBlockClass("BlockEStove", typeof(BlockEStove));
        api.RegisterBlockEntityClass("BlockEntityEStove", typeof(BlockEntityEStove));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEStove", typeof(BEBehaviorEStove));

        //холодильник с анимацией
        api.RegisterBlockClass("BlockEFreezer", typeof(BlockEFreezer));
        api.RegisterBlockEntityClass("BlockEntityEFreezer", typeof(BlockEntityEFreezer));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEFreezer", typeof(BEBehaviorEFreezer));

        api.RegisterBlockClass("BlockEFridge", typeof(BlockEFridge));
        api.RegisterBlockEntityClass("BlockEntityEFridge", typeof(BlockEntityEFridge));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEFridge", typeof(BEBehaviorEFridge));


        api.RegisterBlockClass("BlockEOven", typeof(BlockEOven));
        api.RegisterBlockEntityClass("BlockEntityEOven", typeof(BlockEntityEOven));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEOven", typeof(BEBehaviorEOven));


        api.RegisterBlockClass("BlockEFence", typeof(BlockEFence));
        api.RegisterBlockEntityClass("BlockEntityEFence", typeof(BlockEntityEFence));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEFence", typeof(BEBehaviorEFence));



        api.RegisterBlockClass("BlockEFruitPress", typeof(BlockEFruitPress));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEFruitPress", typeof(BEBehaviorEFruitPress));
        api.RegisterBlockEntityClass("BlockEntityEFruitPress", typeof(BlockEntityEFruitPress));



        // xskills интеграция через рефлексию
        if (api.ModLoader.IsModEnabled("xskillsrabite") || api.ModLoader.IsModEnabled("xskills"))
        {
            try
            {
                // Пытаемся найти сборки XSkills и XLib
                asmXSkills = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name.Equals("xskills", StringComparison.OrdinalIgnoreCase));
                asmXLib = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name.Equals("xlib", StringComparison.OrdinalIgnoreCase));

                // Если сборки найдены, пытаемся получить необходимые типы и методы
                if (asmXSkills != null && asmXLib!= null)
                {
                    typeXLeveling = asmXLib.GetType("XLib.XLeveling.XLeveling");
                    typeCooking = asmXSkills.GetType("XSkills.Cooking");
                    typeBlockEntityBehaviorOwnable = asmXSkills.GetType("XSkills.BlockEntityBehaviorOwnable");

                    // Регистрируем behavior, если он есть
                    if (typeBlockEntityBehaviorOwnable != null)
                    {
                        api.RegisterBlockEntityBehaviorClass("electricityXskillsOwnable", typeBlockEntityBehaviorOwnable);
                    }
                    
                    // Пытаемся получить Instance(XLeveling)
                    if (typeXLeveling != null)
                    {
                        var instMethod = typeXLeveling.GetMethod("Instance", [typeof(ICoreAPI)]);
                        if (instMethod != null)
                        {
                            xLevelingInstance = instMethod.Invoke(null, [api]);
                            methodGetSkill = typeXLeveling.GetMethod("GetSkill", [typeof(string), typeof(bool)]);
                        }
                    }


                    // Инициализация CookingUtil
                    typeCookingUtil = asmXSkills.GetType("XSkills.CookingUtil");
                    if (typeCookingUtil != null)
                    {
                        methodGetCookingTimeMultiplier = typeCookingUtil.GetMethod(
                            "GetCookingTimeMultiplier",
                            [typeof(BlockEntity)]
                        );
                    }

                    // Обновляем условие проверки
                    xskillsEnabled = (typeCooking != null &&
                                      typeXLeveling != null &&
                                      methodGetCookingTimeMultiplier != null);

                    api.Logger.Notification("ElectricalProgressiveQoL: интеграция с XSkills включена");
                }
            }
            catch (Exception ex)
            {
                api.Logger.Warning("Ошибка подключения XSkills: {0}", ex);
                xskillsEnabled = false;
            }
        }


        // применение патчика на механику и вылеты
        //var harmony = new Harmony("electricalprogressive.farmland.patches");
        //harmony.PatchAll(Assembly.GetExecutingAssembly());



        // применение патча для грядки обогревателю
        harmony = new Harmony("electricalprogressive.farmlandheater");
        FarmlandHeaterPatch.RegisterPatch(harmony, api);
    }



    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        this.capi = api;


    }


    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);

        
    }





    public override void AssetsFinalize(ICoreAPI api)
    {
        foreach (CollectibleObject obj in api.World.Collectibles)
        {
            if (HasTag(obj))
                continue;

            if (obj.Code.Path == "rot")
            {
                AddTag(obj, "finished");
                continue;
            }

            if (ResolveBakeables(api, obj))
                continue;
            
        }
    }

    private static bool ResolveBakeables(ICoreAPI api, CollectibleObject obj)
    {
        BakingProperties props = obj?.Attributes?["bakingProperties"]?.AsObject<BakingProperties>();
        if (props == null || props.ResultCode != null)
        {
            return false;
        }

        AddTag(obj, "finished"); // charred

        CollectibleObject perfectItem = GetCollectible(api, props.InitialCode);
        if (perfectItem == null)
            return true;

        AddTag(perfectItem, "finished"); // perfect
        return true;
    }

    public static CollectibleObject GetCollectible(ICoreAPI api, AssetLocation code)
    {
        return api.World.GetItem(code) ?? api.World.GetBlock(code) as CollectibleObject;
    }

    public static void AddTag(CollectibleObject obj, string tag)
    {
        obj.Attributes.Token["foodtag"] = JToken.FromObject(tag);
    }

    public static bool HasTag(CollectibleObject obj)
    {
        return obj?.Attributes?.KeyExists("foodtag") == true;
    }

    public static bool IsFinished(CollectibleObject obj)
    {
        return HasTag(obj) && obj.Attributes["foodtag"].AsString() == "finished";
    }



    public override void Dispose()
    {
        // Отменяем патч при выгрузке мода
        if (harmony != null)
        {
            FarmlandHeaterPatch.UnregisterPatch(harmony);
            harmony?.UnpatchAll();
            harmony = null;
        }
    }
}




