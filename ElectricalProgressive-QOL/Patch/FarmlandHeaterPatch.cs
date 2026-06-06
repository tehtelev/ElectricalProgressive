using ElectricalProgressive.Content.Block.EHeatCannon;
using ElectricalProgressive.Content.Block.EHeater;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;


namespace ElectricalProgressive.Patch;

public class FarmlandHeaterPatch
{
    private static MethodInfo heaterBonusMethod = AccessTools.Method(typeof(FarmlandHeaterPatch), "HeaterBonus");
    private static MethodInfo heaterBonusBerryBushMethod = AccessTools.Method(typeof(FarmlandHeaterPatch), "HeaterBonusBerryBush");
    private static FieldInfo berryBushRoomRegField = typeof(BlockEntityBerryBush).GetField("roomreg", BindingFlags.NonPublic | BindingFlags.Instance);
    private static HarmonyMethod farmlandTranspilerMethod;
    private static HarmonyMethod berryBushTranspilerMethod;
    private static MethodInfo heaterBonusBeehiveMethod = AccessTools.Method(typeof(FarmlandHeaterPatch), "HeaterBonusBeehive");
    private static HarmonyMethod beehiveTranspilerMethod;
    private static FieldInfo beehiveRoomRegField = typeof(BlockEntityBeehive).GetField("roomreg", BindingFlags.NonPublic | BindingFlags.Instance);
    private static MethodInfo heaterBonusFruitTreeMethod = AccessTools.Method(typeof(FarmlandHeaterPatch), "HeaterBonusFruitTree");
    private static HarmonyMethod fruitTreeTranspilerMethod;
    private static FieldInfo fruitTreeRoomRegField = typeof(FruitTreeRootBH).GetField("roomreg", BindingFlags.NonPublic | BindingFlags.Instance);
    private static MethodInfo heaterBonusFruitTreeGrowingMethod = AccessTools.Method(typeof(FarmlandHeaterPatch), "HeaterBonusFruitTreeGrowing");
    private static HarmonyMethod fruitTreeGrowingTranspilerMethod;
    private static MethodInfo heaterBonusSaplingMethod = AccessTools.Method(typeof(FarmlandHeaterPatch), "HeaterBonusSapling");
    private static HarmonyMethod saplingTranspilerMethod;


    private static List<CodeInstruction> codes; // Временное поле для хранения инструкций в транспайлерах
    private static ICoreAPI Api;

    private static bool serverFlag = false;
    private static bool clientFlag = false;


    /// <summary>
    /// Метод для регистрации всех патчей
    /// </summary>
    /// <param name="harmony"></param>
    /// <exception cref="Exception"></exception>
    public static void RegisterPatch(Harmony harmony, ICoreAPI api)
    {
        // Защита от двойной регистрации патча на сервере и клиенте
        if (api.Side == EnumAppSide.Server)
        {
            // Если патч уже зарегистрирован на сервере, пропускаем регистрацию
            if (serverFlag)
                return;

        }
        else
        {
            // Если патч уже зарегистрирован на клиенте, пропускаем регистрацию
            if (clientFlag)
                return;

        }

        Api = api;

        // Патч для грядки
        var farmlandMethod = typeof(BlockEntityFastForwardGrowth).GetMethod("Update",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(float)],
            null);

        if (farmlandMethod == null)
        {
            Api.Logger.Error("Could not find BlockEntityFarmland.Update method!");
        }

        try
        {
            farmlandTranspilerMethod = new HarmonyMethod(typeof(FarmlandHeaterPatch).GetMethod("TranspilerFarmland", BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(farmlandMethod, transpiler: farmlandTranspilerMethod);
        }
        catch
        {
            Api.Logger.Error("The following method failed to patch: BlockEntityFarmland.Update");
        }


        // Патч для куста ягод
        var berryBushMethod = typeof(BlockEntityBerryBush).GetMethod("CheckGrow",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(float)],
            null);

        if (berryBushMethod == null)
        {
            Api.Logger.Error("Could not find BlockEntityBerryBush.CheckGrow method!");
        }


        try
        {
            berryBushTranspilerMethod = new HarmonyMethod(typeof(FarmlandHeaterPatch).GetMethod("TranspilerBerryBush", BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(berryBushMethod, transpiler: berryBushTranspilerMethod);
        }
        catch
        {
            Api.Logger.Error("The following method failed to patch: BlockEntityBerryBush.CheckGrow");
        }


        // Патч для улья
        var beehiveMethod = typeof(BlockEntityBeehive).GetMethod("TestHarvestable",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(float)],
            null);

        if (beehiveMethod == null)
        {
            Api.Logger.Error("Could not find BlockEntityBeehive.TestHarvestable method!");
        }


        try
        {
            beehiveTranspilerMethod = new HarmonyMethod(typeof(FarmlandHeaterPatch).GetMethod("TranspilerBeehive", BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(beehiveMethod, transpiler: beehiveTranspilerMethod);
        }
        catch
        {
            Api.Logger.Error("The following method failed to patch: BlockEntityBeehive.TestHarvestable");
        }


        // Патч для фруктового дерева
        var fruitTreeMethod = typeof(FruitTreeRootBH).GetMethod("getGreenhouseTempBonus",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [],
            null);

        if (fruitTreeMethod == null)
        {
            Api.Logger.Error("Could not find FruitTreeRootBH.getGreenhouseTempBonus method!");
        }


        try
        {
            fruitTreeTranspilerMethod = new HarmonyMethod(typeof(FarmlandHeaterPatch).GetMethod("TranspilerFruitTree", BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(fruitTreeMethod, transpiler: fruitTreeTranspilerMethod);
        }
        catch
        {
            Api.Logger.Error("The following method failed to patch: FruitTreeRootBH.getGreenhouseTempBonus");
        }



        // патч для растущего фруктового дерева
        var fruitTreeGrowingMethod = typeof(FruitTreeGrowingBranchBH).GetMethod("OnTick",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(float)],
            null);

        if (fruitTreeGrowingMethod == null)
        {
            Api.Logger.Error("Could not find FruitTreeGrowingBranchBH.OnTick method!");
        }


        try
        {
            fruitTreeGrowingTranspilerMethod = new HarmonyMethod(typeof(FarmlandHeaterPatch).GetMethod("TranspilerFruitTreeGrowing", BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(fruitTreeGrowingMethod, transpiler: fruitTreeGrowingTranspilerMethod);
        }
        catch
        {
            Api.Logger.Error("The following method failed to patch: FruitTreeGrowingBranchBH.OnTick");
        }
        

        // Патч для саженца (обычные деревья)
        var saplingMethod = typeof(BlockEntitySapling).GetMethod("CheckGrow",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(float)],
            null);

        if (saplingMethod == null)
        {
            Api.Logger.Error("Could not find BESapling.CheckGrow method!");
        }


        try
        {
            saplingTranspilerMethod = new HarmonyMethod(typeof(FarmlandHeaterPatch).GetMethod("TranspilerSapling", BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(saplingMethod, transpiler: saplingTranspilerMethod);
        }
        catch
        {
            Api.Logger.Error("The following method failed to patch: BESapling.CheckGrow");
        }




        // Защита от двойной регистрации патча на сервере и клиенте
        if (api.Side == EnumAppSide.Server)
        {
            serverFlag = true;
        }
        else
        {
            clientFlag = true;
        }
    }



    /// <summary>
    /// Метод для отмены всех патчей
    /// </summary>
    /// <param name="harmony"></param>
    public static void UnregisterPatch(Harmony harmony)
    {
        // Сбрасываем флаги при отмене патча
        if (Api.Side == EnumAppSide.Server)
        {
            serverFlag = false;
        }
        else
        {
            clientFlag = false;
        }

        var farmlandMethod = typeof(BlockEntityFarmland).GetMethod("Update",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(float)],
            null);

        if (farmlandMethod != null)
        {
            harmony?.Unpatch(farmlandMethod, farmlandTranspilerMethod.method);
        }

        var berryBushMethod = typeof(BlockEntityBerryBush).GetMethod("CheckGrow",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(float)],
            null);

        if (berryBushMethod != null)
        {
            harmony?.Unpatch(berryBushMethod, berryBushTranspilerMethod.method);
        }

        var beehiveMethod = typeof(BlockEntityBeehive).GetMethod("TestHarvestable",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(float)],
            null);

        if (beehiveMethod != null)
        {
            harmony?.Unpatch(beehiveMethod, beehiveTranspilerMethod.method);
        }

        var fruitTreeMethod = typeof(FruitTreeRootBH).GetMethod("getGreenhouseTempBonus",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [],
            null);

        if (fruitTreeMethod != null)
        {
            harmony?.Unpatch(fruitTreeMethod, fruitTreeTranspilerMethod.method);
        }

        var fruitTreeGrowingMethod = typeof(FruitTreeGrowingBranchBH).GetMethod("OnTick",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(float)],
            null);

        if (fruitTreeGrowingMethod != null)
        {
            harmony?.Unpatch(fruitTreeGrowingMethod, fruitTreeGrowingTranspilerMethod.method);
        }


        var saplingMethod = typeof(BlockEntitySapling).GetMethod("CheckGrow",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            [typeof(float)],
            null);

        if (saplingMethod != null)
        {
            harmony?.Unpatch(saplingMethod, saplingTranspilerMethod.method);
        }
    }

    /// <summary>
    /// Транспайлер для BlockEntityFarmland.Update
    /// </summary>
    static List<CodeInstruction> TranspilerFarmland(IEnumerable<CodeInstruction> instructions)
    {
        codes = [..instructions];
        var found = false;
        for (int i = 0; i < codes.Count - 2; i++)
        {
            // Ищем последовательность: roomness > 0 -> baseClimate.Temperature += 5f
            if (codes[i].opcode == OpCodes.Ldarg_0 &&
                codes[i + 1].opcode == OpCodes.Ldfld &&
                (codes[i + 1].operand as FieldInfo)?.Name == "roomness" &&
                codes[i + 2].opcode == OpCodes.Ldc_I4_0 &&
                i + 10 < codes.Count)
            {
                // Пропускаем проверку roomness > 0
                int j = i + 3;
                while (j < codes.Count - 5)
                {
                    // Ищем baseClimate.Temperature += 5f
                    if (codes[j].opcode == OpCodes.Ldloc_S &&
                        codes[j + 1].opcode == OpCodes.Dup &&
                        codes[j + 2].opcode == OpCodes.Ldfld &&
                        (codes[j + 2].operand as FieldInfo)?.Name == "Temperature" &&
                        codes[j + 3].opcode == OpCodes.Ldc_R4 &&
                        (float)codes[j + 3].operand > 0f &&
                        codes[j + 4].opcode == OpCodes.Add &&
                        codes[j + 5].opcode == OpCodes.Stfld)
                    {
                        // Вставляем вызов HeaterBonus после baseClimate.Temperature += 5f
                        var newCodes = new List<CodeInstruction>(9)
                        {
                            // Загружаем baseClimate в стек
                            new (OpCodes.Ldloc_S, codes[j].operand), // Дублируем для последующего использования
                            new (OpCodes.Dup),
                            // Загружаем текущее значение Temperature
                            new (OpCodes.Ldfld,
                                typeof(ClimateCondition).GetField("Temperature")),
                            // Вызываем HeaterBonus
                            new (OpCodes.Ldarg_0), // this
                            new (OpCodes.Ldarg_0), // this для upPos
                            new (OpCodes.Ldfld,
                                typeof(BlockEntityFarmland).GetField("upPos", BindingFlags.NonPublic | BindingFlags.Instance)),
                            new (OpCodes.Call, heaterBonusMethod),
                            // Складываем результат
                            new (OpCodes.Add),
                            // Сохраняем обратно в Temperature
                            new (OpCodes.Stfld,
                                typeof(ClimateCondition).GetField("Temperature"))
                        };

                        // Вставляем новые инструкции
                        codes.InsertRange(j + 6, newCodes);
                        found = true;
                        break;
                    }
                    j++;
                }
                if (found)
                    break;
            }
        }

        if (!found)
        {
            Api.Logger.Error("FarmlandHeaterPatch: Could not find injection point for farmland!");
            return [..instructions];
        }

        return codes;
    }

    /// <summary>
    /// Транспайлер для BlockEntityBerryBush.CheckGrow
    /// </summary>
    static List<CodeInstruction> TranspilerBerryBush(IEnumerable<CodeInstruction> instructions)
    {
        codes = [..instructions];
        var found = false;

        for (int i = 0; i < codes.Count - 2; i++)
        {
            // Ищем последовательность: roomness > 0 -> temperature += 5
            if (codes[i].opcode == OpCodes.Ldarg_0 &&
                codes[i + 1].opcode == OpCodes.Ldfld &&
                (codes[i + 1].operand as FieldInfo)?.Name == "roomness" &&
                codes[i + 2].opcode == OpCodes.Ldc_I4_0 &&
                i + 10 < codes.Count)
            {
                // Пропускаем проверку roomness > 0
                int j = i + 3;
                while (j < codes.Count - 5)
                {
                    // Ищем temperature += 5
                    if (codes[j].opcode == OpCodes.Ldloc_S &&
                        codes[j + 1].opcode == OpCodes.Ldc_R4 &&
                        (float)codes[j + 1].operand > 0f &&
                        codes[j + 2].opcode == OpCodes.Add &&
                        codes[j + 3].opcode == OpCodes.Stloc_S)
                    {
                        // Вставляем вызов HeaterBonusBerryBush после temperature += 5
                        var newCodes = new List<CodeInstruction>(5)
                        {
                            // Загружаем temperature в стек
                            new (OpCodes.Ldloc_S, codes[j].operand),
                            // Вызываем HeaterBonusBerryBush
                            new (OpCodes.Ldarg_0), // this
                            new (OpCodes.Call, heaterBonusBerryBushMethod),
                            // Складываем результат
                            new (OpCodes.Add),
                            // Сохраняем обратно в temperature
                            new (OpCodes.Stloc_S, codes[j + 3].operand)
                        };

                        // Вставляем новые инструкции
                        codes.InsertRange(j + 4, newCodes);
                        found = true;
                        break;
                    }
                    j++;
                }
                if (found)
                    break;
            }
        }

        if (!found)
        {
            Api.Logger.Error("FarmlandHeaterPatch: Could not find injection point for berry bush!");
            return [..instructions];
        }

        return codes;
    }



    static List<CodeInstruction> TranspilerBeehive(IEnumerable<CodeInstruction> instructions)
    {
        codes = [.. instructions];
        var found = false;

        for (int i = 0; i < codes.Count - 2; i++)
        {
            // Ищем последовательность: roomness > 0 -> temp += 5
            if (codes[i].opcode == OpCodes.Ldarg_0 &&
                codes[i + 1].opcode == OpCodes.Ldfld &&
                (codes[i + 1].operand as FieldInfo)?.Name == "roomness" &&
                codes[i + 2].opcode == OpCodes.Ldc_R4 &&
                (float)codes[i + 2].operand == 0f &&
                i + 10 < codes.Count)
            {
                // Пропускаем проверку roomness > 0
                int j = i + 3;
                while (j < codes.Count - 5)
                {
                    // Ищем temp += 5
                    if (codes[j].opcode == OpCodes.Ldloc_1 &&
                        codes[j + 1].opcode == OpCodes.Ldc_R4 &&
                        (float)codes[j + 1].operand > 0f &&
                        codes[j + 2].opcode == OpCodes.Add &&
                        codes[j + 3].opcode == OpCodes.Stloc_1)
                    {
                        // Вставляем вызов HeaterBonusBeehive после temp += 5
                        var newCodes = new List<CodeInstruction>(5)
                        {
                            // Загружаем temp в стек
                            new (OpCodes.Ldloc_1, codes[j].operand),
                            // Вызываем HeaterBonusBeehive
                            new (OpCodes.Ldarg_0), // this
                            new (OpCodes.Call, heaterBonusBeehiveMethod),
                            // Складываем результат
                            new (OpCodes.Add),
                            // Сохраняем обратно в temp
                            new (OpCodes.Stloc_1, codes[j + 3].operand)
                        };

                        // Вставляем новые инструкции
                        codes.InsertRange(j + 4, newCodes);
                        found = true;
                        break;
                    }
                    j++;
                }
                if (found)
                    break;
            }
        }

        if (!found)
        {
            Api.Logger.Error("FarmlandHeaterPatch: Could not find injection point for beehive!");
            return [..instructions];
        }

        return codes;
    }

    // Добавить новый транспайлер для FruitTreeRootBH с проверкой roomness
    static List<CodeInstruction> TranspilerFruitTree(IEnumerable<CodeInstruction> instructions)
    {
        codes = [.. instructions];
        var found = false;

        for (int i = 0; i < codes.Count - 4; i++)
        {
            // Ищем последовательность: roomness > 0 -> return 5;
            // IL_006d: ldc.i4.0
            // IL_006e: ble.s IL_0076
            // IL_0070: ldc.r4 5
            // IL_0075: ret
            if (codes[i].opcode == OpCodes.Ldc_I4_0 &&
                codes[i + 1].opcode == OpCodes.Ble_S &&
                codes[i + 2].opcode == OpCodes.Ldc_R4 &&
                (float)codes[i + 2].operand > 0f &&
                codes[i + 3].opcode == OpCodes.Ret)
            {
                // Вставляем вызов HeaterBonusFruitTree перед возвратом
                var newCodes = new List<CodeInstruction>(5)
                {
                    // Загружаем текущее значение 5
                    new (OpCodes.Ldc_R4, codes[i + 2].operand), // Вызываем HeaterBonusFruitTree
                    new (OpCodes.Ldarg_0), // this
                    new (OpCodes.Call, heaterBonusFruitTreeMethod),
                    // Складываем результат (5 + бонус от обогревателей)
                    new (OpCodes.Add),
                    // Возвращаем результат
                    new (OpCodes.Ret)
                };

                // Заменяем старый return 5 на новую последовательность
                codes.RemoveRange(i + 2, 2); // Удаляем ldc.r4 5 и ret
                codes.InsertRange(i + 2, newCodes);
                found = true;
                break;
            }
        }

        if (!found)
        {
            Api.Logger.Error("FarmlandHeaterPatch: Could not find injection point for fruit tree!");
            return [..instructions];
        }

        return codes;
    }



    // Добавить новый транспайлер для FruitTreeGrowingBranchBH.OnTick
    static List<CodeInstruction> TranspilerFruitTreeGrowing(IEnumerable<CodeInstruction> instructions)
    {
        codes = [..instructions];
        var found = false;

        for (int i = 0; i < codes.Count - 2; i++)
        {
            // Ищем последовательность: 
            // ldc.r4 12
            // bge.un.s IL_0255
            if (codes[i].opcode == OpCodes.Ldc_R4 &&
                (float)codes[i].operand == 12f &&
                codes[i + 1].opcode == OpCodes.Bge_Un_S)
            {
                // Нашли сравнение температуры с 12
                // Вставляем вызов HeaterBonusFruitTreeGrowing перед сравнением

                // Создаем новые инструкции для вставки перед сравнением
                var newCodes = new List<CodeInstruction>(3)
                {
                    // Вызываем HeaterBonusFruitTreeGrowing (результат будет в стеке)
                    new (OpCodes.Ldarg_0), // this
                    new (OpCodes.Call, heaterBonusFruitTreeGrowingMethod),
                    // Складываем температуру (уже в стеке) с бонусом
                    new (OpCodes.Add)
                };

                // Вставляем новые инструкции перед сравнением
                codes.InsertRange(i, newCodes);
                found = true;
                break;
            }
        }

        if (!found)
        {
            Api.Logger.Error("FarmlandHeaterPatch: Could not find injection point for fruit tree growing!");
            return [..instructions];
        }

        return codes;
    }


    static List<CodeInstruction> TranspilerSapling(IEnumerable<CodeInstruction> instructions)
    {
        codes = [..instructions];
        var found = false;

        for (int i = 0; i < codes.Count - 1; i++)
        {
            // Ищем последовательность: 
            // ldc.r4 5
            // bge.un.s
            if (codes[i].opcode == OpCodes.Ldc_R4 &&
                (float)codes[i].operand == 5f &&
                codes[i + 1].opcode == OpCodes.Bge_Un_S)
            {
                // Нашли сравнение температуры с 5
                // Вставляем вызов HeaterBonusSapling перед сравнением

                // Создаем новые инструкции для вставки перед сравнением
                var newCodes = new List<CodeInstruction>(3)
                {
                    // Вызываем HeaterBonusSapling (результат будет в стеке)
                    new (OpCodes.Ldarg_0), // this
                    new (OpCodes.Call, heaterBonusSaplingMethod),
                    // Складываем температуру (уже в стеке) с бонусом
                    new (OpCodes.Add)
                };

                // Вставляем новые инструкции перед сравнением
                codes.InsertRange(i, newCodes);
                found = true;
                break;
            }
        }

        if (!found)
        {
            Api.Logger.Error("FarmlandHeaterPatch: Could not find injection point for sapling!");
            return [..instructions];
        }

        return codes;
    }




    /// <summary>
    /// Универсальный метод для расчета бонуса от обогревателей
    /// </summary>
    /// <param name="api"></param>
    /// <param name="targetPos"></param>
    /// <param name="roomForPosition"></param>
    /// <returns></returns>
    public static float CalculateHeaterBonus(
        ICoreAPI api,
        BlockPos targetPos,
        Room roomForPosition)
    {
        if (api == null || roomForPosition == null)
            return 0;

        // Проверяем условия комнаты (как в оригинальном коде)
        if (roomForPosition.SkylightCount <= roomForPosition.NonSkylightCount || roomForPosition.ExitCount != 0)
            return 0;

        var electricalMod = api.ModLoader.GetModSystem<ElectricalProgressive>();
        if (electricalMod == null)
            return 0;

        int roomVolume = CalculateRoomVolume(roomForPosition);
        if (roomVolume <= 0)
            return 0;

        float totalBonus = 0f;
        var heatersInRoom = new List<dynamic>();

        // Ищем обогреватели в пределах 20 блоков и в той же комнате
        dynamic heater;
        foreach (var part in electricalMod.Parts.Values)
        {
            // определяем какой из обогревателей
            heater = part.Consumer as BEBehaviorEHeater;
            if (heater == null)
                heater = part.Consumer as BEBehaviorEHeatCannon;

            // не обогреватель, пропускаем
            if (heater == null)
                continue;

            // обогреватель не запитан, пропускаем
            if (heater.getPowerRequest() <= 0)
                continue;

            // проверяем расстояние до обогревателя
            if (Math.Abs(heater.Pos.X - targetPos.X) <= 20 &&
                Math.Abs(heater.Pos.Y - targetPos.Y) <= 20 &&
                Math.Abs(heater.Pos.Z - targetPos.Z) <= 20)
            {
                if (roomForPosition.Contains(heater.Pos))
                {
                    heatersInRoom.Add(heater);

                    float heaterBonus = (1.0f * heater.getPowerReceive() / heater.getPowerRequest()) * (heater.TempKoeff*100.0f/ roomVolume);

                    totalBonus += heaterBonus;
                }
            }
        }

        // сообщаем ообогревателям бонус
        foreach (var heater2 in heatersInRoom)
        {
            if (heater2.HeatLevel>0)
                heater2.GreenhouseBonus = totalBonus;
            else
            {
                heater2.GreenhouseBonus = 0;
            }
        }

        return totalBonus;
    }

    /// <summary>
    /// Модифицированные методы расчета бонуса
    /// </summary>
    /// <param name="farmland"></param>
    /// <param name="upPos"></param>
    /// <returns></returns>

    public static float HeaterBonus(BlockEntityFarmland farmland, BlockPos upPos)
    {
        try
        {
            BlockFarmland blockFarmland = farmland.Block as BlockFarmland;
            if (blockFarmland == null)
                return 0f;


            RoomRegistry roomreg = Api.ModLoader.GetModSystem<RoomRegistry>();
            if (roomreg == null)
                return 0f;
            
            Room roomForPosition = roomreg.GetRoomForPosition(upPos);

            int roomness = roomForPosition == null || roomForPosition.SkylightCount <= roomForPosition.NonSkylightCount || roomForPosition.ExitCount != 0 ? 0 : 1;
            if (roomness <= 0)
                return 0f;

            return CalculateHeaterBonus(farmland.Api, upPos, roomForPosition);
        }
        catch (Exception ex)
        {
            farmland.Api?.Logger.Error($"Error in HeaterBonus: {ex}");
            return 0f;
        }
    }

    public static float HeaterBonusBerryBush(BlockEntityBerryBush berryBush)
    {
        try
        {
            RoomRegistry roomreg = berryBushRoomRegField?.GetValue(berryBush) as RoomRegistry;
            if (roomreg == null)
            {
                roomreg = berryBush.Api.ModLoader.GetModSystem<RoomRegistry>();
                if (roomreg == null)
                    return 0f;
            }

            Room roomForPosition = roomreg.GetRoomForPosition(berryBush.Pos);
            if (roomForPosition == null)
                return 0f;

            return CalculateHeaterBonus(berryBush.Api, berryBush.Pos, roomForPosition);
        }
        catch (Exception ex)
        {
            berryBush.Api?.Logger.Error($"Error in HeaterBonusBerryBush: {ex}");
            return 0f;
        }
    }

    public static float HeaterBonusBeehive(BlockEntityBeehive beehive)
    {
        try
        {
            RoomRegistry roomreg = beehiveRoomRegField?.GetValue(beehive) as RoomRegistry;
            if (roomreg == null)
            {
                roomreg = beehive.Api.ModLoader.GetModSystem<RoomRegistry>();
                if (roomreg == null)
                    return 0;
            }

            Room roomForPosition = roomreg.GetRoomForPosition(beehive.Pos);
            if (roomForPosition == null)
                return 0;

            return CalculateHeaterBonus(beehive.Api, beehive.Pos, roomForPosition);
        }
        catch (Exception ex)
        {
            beehive.Api?.Logger.Error($"Error in HeaterBonusBeehive: {ex}");
            return 0;
        }
    }

    public static float HeaterBonusFruitTree(FruitTreeRootBH fruitTree)
    {
        try
        {
            RoomRegistry roomreg = fruitTreeRoomRegField?.GetValue(fruitTree) as RoomRegistry;
            if (roomreg == null)
            {
                roomreg = fruitTree.Api.ModLoader.GetModSystem<RoomRegistry>();
                if (roomreg == null)
                    return 0f;
            }

            Room roomForPosition = roomreg.GetRoomForPosition(fruitTree.Blockentity.Pos);
            if (roomForPosition == null)
                return 0f;

            return CalculateHeaterBonus(fruitTree.Api, fruitTree.Blockentity.Pos, roomForPosition);
        }
        catch (Exception ex)
        {
            fruitTree.Api?.Logger.Error($"Error in HeaterBonusFruitTree: {ex}");
            return 0f;
        }
    }

    public static float HeaterBonusFruitTreeGrowing(FruitTreeGrowingBranchBH fruitTreeGrowing)
    {
        try
        {
            var roomreg = fruitTreeGrowing.Api.ModLoader.GetModSystem<RoomRegistry>();
            if (roomreg == null)
                return 0;

            Room roomForPosition = roomreg.GetRoomForPosition(fruitTreeGrowing.Blockentity.Pos);
            if (roomForPosition == null)
                return 0;

            return CalculateHeaterBonus(fruitTreeGrowing.Api, fruitTreeGrowing.Blockentity.Pos, roomForPosition);
        }
        catch (Exception ex)
        {
            fruitTreeGrowing.Api?.Logger.Error($"Error in HeaterBonusFruitTreeGrowing: {ex}");
            return 0;
        }
    }


    
    public static float HeaterBonusSapling(BlockEntitySapling sapling)
    {
        try
        {
            var roomreg = sapling.Api.ModLoader.GetModSystem<RoomRegistry>();
            if (roomreg == null)
                return 0;

            Room roomForPosition = roomreg.GetRoomForPosition(sapling.Pos);
            if (roomForPosition == null)
                return 0;

            return CalculateHeaterBonus(sapling.Api, sapling.Pos, roomForPosition);
        }
        catch (Exception ex)
        {
            sapling.Api?.Logger.Error($"Error in HeaterBonusSapling: {ex}");
            return 0;
        }
    }



    /// <summary>
    /// Считаем объем комнаты по битовой маске PosInRoom
    /// </summary>
    public static int CalculateRoomVolume(Room room)
    {
        if (room?.PosInRoom == null)
            return 0;

        int sizex = room.Location.X2 - room.Location.X1 + 1;
        int sizey = room.Location.Y2 - room.Location.Y1 + 1;
        int sizez = room.Location.Z2 - room.Location.Z1 + 1;
        int volume = 0;

        for (int dx = 0; dx < sizex; dx++)
        {
            for (int dy = 0; dy < sizey; dy++)
            {
                for (int dz = 0; dz < sizez; dz++)
                {
                    int pindex = (dy * sizez + dz) * sizex + dx;
                    int byteIndex = pindex / 8;
                    int bitIndex = pindex % 8;

                    if (byteIndex < room.PosInRoom.Length &&
                        (room.PosInRoom[byteIndex] & (1 << bitIndex)) != 0)
                    {
                        volume++;
                    }
                }
            }
        }

        return volume;
    }
}