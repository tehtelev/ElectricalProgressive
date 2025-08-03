using Vintagestory.API.Common;

namespace ElectricalProgressive.Utils;

public static class MyMiniLib
{
    public static int GetAttributeInt(CollectibleObject block, string attrname, int def = 0)
    {
        if (block != null && block.Attributes != null && block.Attributes[attrname] != null)
        {
            return block.Attributes[attrname].AsInt(def);
        }
        return def;
    }

    public static bool GetAttributeBool(CollectibleObject block, string attrname, bool def = false)
    {
        if (block != null && block.Attributes != null && block.Attributes[attrname] != null)
        {
            return block.Attributes[attrname].AsBool(def);
        }
        return def;
    }

    public static float GetAttributeFloat(CollectibleObject block, string attrname, float def = 0F)
    {
        if (block != null && block.Attributes != null && block.Attributes[attrname] != null)
        {
            return block.Attributes[attrname].AsFloat(def);
        }
        return def;
    }
    
    public static string GetAttributeString(CollectibleObject block, string attrname, string def)
    {
        if (block != null && block.Attributes != null && block.Attributes[attrname] != null)
        {
            return block.Attributes[attrname].AsString(def);
        }
        return def;
    }

    public static int[] GetAttributeArrayInt(CollectibleObject block, string attrname, int[] def)
    {
        if (block != null && block.Attributes != null && block.Attributes[attrname] != null)
        {
            return block.Attributes[attrname].AsArray<int>(def,"int");
        }
        return def;
    }

    public static float[] GetAttributeArrayFloat(CollectibleObject block, string attrname, float[] def)
    {
        if (block != null && block.Attributes != null && block.Attributes[attrname] != null)
        {
            return block.Attributes[attrname].AsArray<float>(def, "float");
        }
        return def;
    }
}