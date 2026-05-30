using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace ElectricalProgressive.Patch
{
    [HarmonyPatch(typeof(ModSystemGliding))]
    public static class ModSystemGlidingPatch
    {
        [HarmonyPatch("get_HasGlider")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> HasGliderTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            // Полностью заменяем метод
            return new HarmonyLib.CodeInstruction[]
            {
                new HarmonyLib.CodeInstruction(OpCodes.Ldarg_0),
                new HarmonyLib.CodeInstruction(OpCodes.Call, 
                    typeof(ModSystemGlidingPatch).GetMethod("GetHasGlider", 
                        BindingFlags.NonPublic | BindingFlags.Static)),
                new HarmonyLib.CodeInstruction(OpCodes.Ret)
            };
        }
        
        private static bool GetHasGlider(ModSystemGliding instance)
        {
            var capiField = typeof(ModSystemGliding).GetField("capi", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (capiField == null) return false;
            
            var capi = capiField.GetValue(instance) as ICoreClientAPI;
            if (capi?.World?.Player?.InventoryManager == null) return false;
            
            var backpack = capi.World.Player.InventoryManager.GetOwnInventory("backpack");
            if (backpack == null) return false;
            
            foreach (ItemSlot itemSlot in backpack)
            {
                if (itemSlot is ItemSlotBackpack && itemSlot.Itemstack?.Collectible != null)
                {
                    var collectible = itemSlot.Itemstack.Collectible;
                    if (collectible is ItemEGlider || collectible is ItemGlider)
                        return true;
                }
            }
            return false;
        }
    }
}