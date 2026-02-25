namespace ElectricalProgressive.Utils;


public static class StringHelper
{
    /// <summary>
    /// Рисует прогресс-бар в виде строки, состоящей из 16 символов, где заполненные символы (■) представляют процент выполнения
    /// </summary>
    /// <param name="percentage"></param>
    /// <returns></returns>
    public static string Progressbar(float percentage)
    {
        var temp = "";

        for (var index = 0; index < 16; ++index)
        {
            temp += index >= percentage * 16.0f / 100.0f
                ? '□'
                : '■';
        }

        return temp.Insert(8, " " + ((int)percentage).ToString().PadLeft(3, ' ') + "% ");
    }
}
