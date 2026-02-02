using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

/// <summary>
/// Инвентарь для горна с автозагрузкой/выгрузкой
/// </summary>
public class InventoryEHorn : InventoryGeneric
{
    public InventoryEHorn(string className, string instanceID, ICoreAPI api)
        : base(1, className, instanceID, api)
    {
        // Максимальный размер стека - 4, как в оригинальной логике
        base.slots = GenEmptySlots(1);
        slots[0].MaxSlotStackSize = 4;
    }

    public InventoryEHorn(int slots, string className, string instanceID, ICoreAPI api, NewSlotDelegate onNewSlot)
        : base(slots, className, instanceID, api, onNewSlot)
    {
    }

    /// <summary>
    /// Автозагрузка горна - только предметы, которые можно нагреть
    /// </summary>
    public override ItemSlot GetAutoPushIntoSlot(BlockFacing atBlockFace, ItemSlot fromSlot)
    {
        if (fromSlot.Itemstack == null)
            return null;

        // Проверяем, можно ли нагреть этот предмет
        var firstCodePart = fromSlot.Itemstack.Collectible.FirstCodePart();
        var forgableGeneric = fromSlot.Itemstack.Collectible.Attributes?.IsTrue("forgable") == true;
        var heatable = firstCodePart == "ingot" || firstCodePart == "metalplate" ||
                       firstCodePart == "workitem" || forgableGeneric;

        // нагреть нельзя?
        if (!heatable)
            return null;

        // Устанавливаем максимальный размер стека в зависимости от типа предмета
        if (forgableGeneric)
            slots[0].MaxSlotStackSize = 1;
        else
        {
            slots[0].MaxSlotStackSize = 4;
        }

        // Если слот пустой или можно добавить к существующему стеку
        if (slots[0].Empty ||
            (slots[0].Itemstack.Equals(Api.World, fromSlot.Itemstack, GlobalConstants.IgnoredStackAttributes) &&
             slots[0].Itemstack.StackSize < slots[0].MaxSlotStackSize))
        {
            return slots[0];
        }

        return null;
    }

    /// <summary>
    /// Автовыгрузка из горна - только когда температура выше рабочей на 100 градусов
    /// </summary>
    public override ItemSlot GetAutoPullFromSlot(BlockFacing atBlockFace)
    {
        if (slots[0].Empty)
            return null;

        var stack = slots[0].Itemstack;
        float temperature = stack.Collectible.GetTemperature(Api.World, stack);

        // Получаем рабочую температуру
        float workingTemp = GetWorkingTemperature(stack);

        // если вдруг температура не определилась, ставим дефолт в 800
        if (workingTemp <= 0)
            workingTemp = 800;

        // Выгружаем только если температура >= рабочая + 100
        if (temperature >= workingTemp + 100)
        {
            return slots[0];
        }

        return null;
    }



    /// <summary>
    /// Получает рабочую температуру предмета
    /// </summary>
    private float GetWorkingTemperature(ItemStack stack)
    {
        float meltingpoint = stack.Collectible.GetMeltingPoint(Api.World, null, new DummySlot(stack));

        if (stack.Collectible.Attributes?["workableTemperature"].Exists == true)
        {
            return stack.Collectible.Attributes["workableTemperature"].AsFloat(meltingpoint / 2);
        }

        return meltingpoint / 2;
    }
}