using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Utils;

/// <summary>
/// Всякие полезные функции
/// </summary>
public static class MyMiniLib
{
    /// <summary>
    /// Получение аттрибута int
    /// </summary>
    /// <param name="block"></param>
    /// <param name="attrname"></param>
    /// <param name="def"></param>
    /// <returns></returns>
    public static int GetAttributeInt(CollectibleObject block, string attrname, int def = 0)
    {
        if (block is { Attributes: not null } && block.Attributes[attrname] != null)
        {
            return block.Attributes[attrname].AsInt(def);
        }
        return def;
    }

    /// <summary>
    /// Получение аттрибута bool
    /// </summary>
    /// <param name="block"></param>
    /// <param name="attrname"></param>
    /// <param name="def"></param>
    /// <returns></returns>
    public static bool GetAttributeBool(CollectibleObject block, string attrname, bool def = false)
    {
        if (block is { Attributes: not null } && block.Attributes[attrname] != null)
        {
            return block.Attributes[attrname].AsBool(def);
        }
        return def;
    }

    /// <summary>
    /// Получение аттрибута float
    /// </summary>
    /// <param name="block"></param>
    /// <param name="attrname"></param>
    /// <param name="def"></param>
    /// <returns></returns>
    public static float GetAttributeFloat(CollectibleObject block, string attrname, float def = 0F)
    {
        if (block is { Attributes: not null } && block.Attributes[attrname] != null)
        {
            return block.Attributes[attrname].AsFloat(def);
        }
        return def;
    }

    /// <summary>
    /// Получение аттрибута string
    /// </summary>
    /// <param name="block"></param>
    /// <param name="attrname"></param>
    /// <param name="def"></param>
    /// <returns></returns>
    public static string GetAttributeString(CollectibleObject block, string attrname, string def)
    {
        if (block is { Attributes: not null } && block.Attributes[attrname] != null)
        {
            return block.Attributes[attrname].AsString(def);
        }
        return def;
    }

    /// <summary>
    /// Получение аттрибута в виде массива string
    /// </summary>
    /// <param name="block"></param>
    /// <param name="attrname"></param>
    /// <param name="def"></param>
    /// <returns></returns>
    public static string[] GetAttributeArrayString(CollectibleObject block, string attrname, string[] def)
    {
        if (block is { Attributes: not null } && block.Attributes[attrname] != null)
        {
            return block.Attributes[attrname].AsArray<string>(def, "string");
        }
        return def;
    }

    /// <summary>
    /// Получение аттрибута в виде массива int
    /// </summary>
    /// <param name="block"></param>
    /// <param name="attrname"></param>
    /// <param name="def"></param>
    /// <returns></returns>
    public static int[] GetAttributeArrayInt(CollectibleObject block, string attrname, int[] def)
    {
        if (block is { Attributes: not null } && block.Attributes[attrname] != null)
        {
            return block.Attributes[attrname].AsArray<int>(def, "int");
        }
        return def;
    }




    /// <summary>
    /// Получение аттрибута в виде массива float
    /// </summary>
    /// <param name="block"></param>
    /// <param name="attrname"></param>
    /// <param name="def"></param>
    /// <returns></returns>
    public static float[] GetAttributeArrayFloat(CollectibleObject block, string attrname, float[] def)
    {
        if (block is { Attributes: not null } && block.Attributes[attrname] != null)
        {
            return block.Attributes[attrname].AsArray<float>(def, "float");
        }
        return def;
    }

    /// <summary>
    /// Получение аттрибута в виде массива float
    /// </summary>
    /// <param name="block"></param>
    /// <param name="attrname"></param>
    /// <param name="def"></param>
    /// <returns></returns>
    public static float[][] GetAttributeArrayArrayFloat(CollectibleObject block, string attrname, float[][] def)
    {
        if (block is { Attributes: not null } && block.Attributes[attrname] != null)
        {
            try
            {
                // Получаем JSON строку и десериализуем с помощью Newtonsoft.Json
                string json = block.Attributes[attrname].ToString();
                var result = Newtonsoft.Json.JsonConvert.DeserializeObject<float[][]>(json);

                return result ?? def;
            }
            catch
            {
                // Логируем ошибку если нужно
                //Console.WriteLine($"Error parsing array {attrname}: {ex.Message}");
            }
        }
        return def;
    }


    /// <summary>
    /// Получение аттрибута в виде массива int
    /// </summary>
    /// <param name="block"></param>
    /// <param name="attrname"></param>
    /// <param name="def"></param>
    /// <returns></returns>
    public static int[][] GetAttributeArrayArrayInt(CollectibleObject block, string attrname, int[][] def)
    {
        if (block is { Attributes: not null } && block.Attributes[attrname] != null)
        {
            try
            {
                // Получаем JSON строку и десериализуем с помощью Newtonsoft.Json
                string json = block.Attributes[attrname].ToString();
                var result = Newtonsoft.Json.JsonConvert.DeserializeObject<int[][]>(json);

                return result ?? def;
            }
            catch
            {
                // Логируем ошибку если нужно
                //Console.WriteLine($"Error parsing array {attrname}: {ex.Message}");
            }
        }
        return def;
    }


    /// <summary>
    /// Проверка, что грань блока под нужной цела
    /// </summary>
    /// <param name="blockAccessor"></param>
    /// <param name="pos"></param>
    /// <param name="facing"></param>
    /// <returns></returns>
    public static bool CheckSolidFace(IBlockAccessor blockAccessor, BlockPos pos, Facing facing)
    {
        var faces = FacingHelper.Faces(facing).ToList();

        // генератор готов к проверке?
        if (faces == null ||
            faces.Count == 0 ||
            faces.First() is not
            {
            }
                blockFacing)
        {
            return false;
        }

        // соседний блок по направлению грани
        BlockPos neighblock = pos.AddCopy(blockFacing);
        var block = blockAccessor.GetBlock(neighblock);
        var sideIndex = blockFacing.Opposite.Index;

        // целая ли грань?
        // для микроблоков это 14/16 целостности грани
        var solidBlock = block.SideSolid[sideIndex];
        var solidMicroblock = (block as BlockMicroBlock)?.SideIsSolid(neighblock, sideIndex);
        if (solidBlock || solidMicroblock==true) 
        {
            return true;
        }

        return false;

    }
}