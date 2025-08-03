namespace ElectricalProgressive.Utils;

public static class FloatHelper
{
    /// <summary>
    /// Перемещает значение self из диапазона [fromSource, toSource] в диапазон [fromTarget, toTarget].
    /// </summary>
    /// <param name="self"></param>
    /// <param name="fromSource"></param>
    /// <param name="toSource"></param>
    /// <param name="fromTarget"></param>
    /// <param name="toTarget"></param>
    /// <returns></returns>
    public static float Remap(float self, float fromSource, float toSource, float fromTarget, float toTarget)
    {
        return (self - fromSource) / (toSource - fromSource) * (toTarget - fromTarget) + fromTarget;
    }
}
