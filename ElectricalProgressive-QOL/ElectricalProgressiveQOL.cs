using ElectricalProgressive.Content.Block.ECharger;
using ElectricalProgressive.Content.Block.EFence;
using ElectricalProgressive.Content.Block.EFonar;
using ElectricalProgressive.Content.Block.EFreezer2;
using ElectricalProgressive.Content.Block.EHeater;
using ElectricalProgressive.Content.Block.EHorn;
using ElectricalProgressive.Content.Block.ELamp;
using ElectricalProgressive.Content.Block.EOven;
using ElectricalProgressive.Content.Block.ESFonar;
using ElectricalProgressive.Content.Block.EStove;
using System;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;



[assembly: ModDependency("game", "1.21.0-rc.4")]
[assembly: ModDependency("electricalprogressivecore", "2.1.0-rc.4")]
[assembly: ModDependency("electricalprogressivebasics", "2.1.0-rc.4")]
[assembly: ModInfo(
    "Electrical Progressive: QoL",
    "electricalprogressiveqol",
    Website = "https://github.com/tehtelev/ElectricalProgressive",
    Description = "Additional electrical devices.",
    Version = "2.1.0-rc.4",
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


        api.RegisterBlockClass("BlockEFence", typeof(BlockEFence));
        api.RegisterBlockEntityClass("BlockEntityEFence", typeof(BlockEntityEFence));
        api.RegisterBlockEntityBehaviorClass("BEBehaviorEFence", typeof(BEBehaviorEFence));

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
                        var instMethod = typeXLeveling.GetMethod("Instance", new[] { typeof(ICoreAPI) });
                        if (instMethod != null)
                        {
                            xLevelingInstance = instMethod.Invoke(null, new object[] { api });
                            methodGetSkill = typeXLeveling.GetMethod("GetSkill", new[] { typeof(string), typeof(bool) });
                        }
                    }


                    // Инициализация CookingUtil
                    typeCookingUtil = asmXSkills.GetType("XSkills.CookingUtil");
                    if (typeCookingUtil != null)
                    {
                        methodGetCookingTimeMultiplier = typeCookingUtil.GetMethod(
                            "GetCookingTimeMultiplier",
                            new[] { typeof(BlockEntity) }
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




    }



    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        this.capi = api;


    }

}




