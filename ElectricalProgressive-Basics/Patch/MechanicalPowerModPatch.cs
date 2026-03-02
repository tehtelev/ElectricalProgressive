using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ElectricalProgressive.Patch;

public class MechanicalPowerMod_RebuildNetwork_Patch
{
    private static HarmonyMethod prefixMethod;

    /// <summary>
    /// Метод для регистрации патча
    /// </summary>
    /// <param name="harmony"></param>
    /// <exception cref="Exception"></exception>
    public static void RegisterPatch(Harmony harmony, ICoreAPI api)
    {
        // Ищем метод RebuildNetwork в MechanicalPowerMod
        var originalMethod = typeof(MechanicalPowerMod).GetMethod("RebuildNetwork",
            BindingFlags.Public | BindingFlags.Instance);


        if (originalMethod == null)
        {
            api.Logger.Error("Could not find MechanicalPowerMod.RebuildNetwork method!");
            return;
        }

        prefixMethod = new HarmonyMethod(typeof(MechanicalPowerMod_RebuildNetwork_Patch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic));

        harmony.Patch(originalMethod, prefix: prefixMethod);
    }



    /// <summary>
    /// Метод для отмены патча
    /// </summary>
    /// <param name="harmony"></param>
    public static void UnregisterPatch(Harmony harmony)
    {
        var originalMethod = typeof(MechanicalPowerMod).GetMethod("RebuildNetwork", BindingFlags.Public | BindingFlags.Instance);
        if (originalMethod != null)
        {
            harmony.Unpatch(originalMethod, prefixMethod.method);
        }
    }



    /// <summary>
    /// Метод Prefix для патча RebuildNetwork с имправлением ошибки null reference exception
    /// </summary>
    /// <param name="__instance"></param>
    /// <param name="network"></param>
    /// <param name="nowRemovedNode"></param>
    /// <returns></returns>
    static bool Prefix(MechanicalPowerMod __instance, MechanicalNetwork network, IMechanicalPowerDevice nowRemovedNode = null)
    {
        // Ваш полностью замененный код метода
        try
        {
            network.Valid = false;

            if (__instance.Api.Side == EnumAppSide.Server)
                __instance.DeleteNetwork(network);

            if (network.nodes.Values.Count == 0)
            {
                return false; // Прерываем выполнение оригинального метода
            }

            var nnodes = network.nodes.Values.ToArray();

            foreach (var nnode in nnodes)
            {
                nnode.LeaveNetwork();
            }

            foreach (var nnode in nnodes)
            {
                if (!(nnode is IMechanicalPowerDevice)) continue;

                IMechanicalPowerDevice newnode = __instance.Api.World.BlockAccessor
                    .GetBlockEntity((nnode as IMechanicalPowerDevice).Position)
                    ?.GetBehavior<BEBehaviorMPBase>() as IMechanicalPowerDevice;

                if (newnode == null) continue;

                BlockFacing oldTurnDir = newnode.GetPropagationDirection();

                if (newnode.OutFacingForNetworkDiscovery != null &&
                    (nowRemovedNode == null || newnode.Position != nowRemovedNode.Position))
                {
                    MechanicalNetwork newnetwork = newnode.CreateJoinAndDiscoverNetwork(newnode.OutFacingForNetworkDiscovery);
                    if (newnetwork == null)
                        continue;

                    bool reversed = newnode.GetPropagationDirection() == oldTurnDir.Opposite;
                    newnetwork.Speed = reversed ? -network.Speed : network.Speed;
                    newnetwork.AngleRad = network.AngleRad;
                    newnetwork.TotalAvailableTorque = reversed ? -network.TotalAvailableTorque : network.TotalAvailableTorque;
                    newnetwork.NetworkResistance = network.NetworkResistance;

                    if (__instance.Api.Side == EnumAppSide.Server)
                        newnetwork.broadcastData();
                }
            }

            return false; // Полностью заменяем оригинальный метод, не вызываем его
        }
        catch (Exception ex)
        {
            __instance.Api.Logger.Error($"Error in patched RebuildNetwork: {ex}");
            return false;
        }
    }
}