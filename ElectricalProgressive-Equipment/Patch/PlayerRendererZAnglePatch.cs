using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Patch;

[HarmonyPatch(typeof(EntityPlayerShapeRenderer), "loadModelMatrixForPlayer")]

public static class PlayerRendererZAnglePatch
{
    public static void ApplyCustomAngles(EntityPlayerShapeRenderer renderer, float[] modelMat)
    {
        Mat4f.RotateX(modelMat, modelMat, renderer.xangle);
        Mat4f.RotateY(modelMat, modelMat, renderer.yangle);
        Mat4f.RotateZ(modelMat, modelMat, renderer.zangle);
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator gen)
    {
        var code = new List<CodeInstruction>(instructions);

        for (int i = 0; i < code.Count - 4; i++)
        {
            if (code[i].opcode == OpCodes.Callvirt &&
                code[i].operand?.ToString().Contains("get_Properties") == true &&
                code[i + 1].opcode == OpCodes.Ldfld &&
                code[i + 1].operand?.ToString().Contains("Client") == true &&
                code[i + 2].opcode == OpCodes.Ldfld &&
                code[i + 2].operand?.ToString().Contains("Size") == true &&
                code[i + 3].opcode == OpCodes.Stloc_S)
            {
                var insertAt = i + 4;
                var toInsert = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),  // this (renderer)
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldfld,
                        AccessTools.Field(typeof(EntityShapeRenderer), "ModelMat")),
                    new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(PlayerRendererZAnglePatch), "ApplyCustomAngles"))
                };
                code.InsertRange(insertAt, toInsert);
                break;
            }
        }
        return code;
    }
}