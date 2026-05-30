using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace ElectricalProgressive.Patch;

public class EGliderPhysicsPatcher
{
    private static FieldInfo physicsModulesField;
    
    public static void PatchPlayerPhysics(Entity entity)
    {
        // Находим EntityBehaviorPlayerPhysics
        var behavior = entity.GetBehavior<EntityBehaviorPlayerPhysics>();
        if (behavior == null) return;
        
        // Получаем приватное поле physicsModules через рефлексию
        if (physicsModulesField == null)
        {
            physicsModulesField = typeof(EntityBehaviorControlledPhysics)
                .GetField("physicsModules", BindingFlags.NonPublic | BindingFlags.Instance);
        }
        
        if (physicsModulesField == null) return;
        
        // Получаем текущий список модулей
        var modules = physicsModulesField.GetValue(behavior) as List<PModule>;
        if (modules == null) return;
        
        // Проверяем, не добавлен ли уже наш модуль
        bool hasCustomModule = false;
        foreach (var module in modules)
        {
            if (module.GetType() == typeof(EGlideringPlayerInAir))
            {
                hasCustomModule = true;
                break;
            }
        }
        
        // Если нет - добавляем
        if (!hasCustomModule)
        {
            // Находим индекс стандартного PModulePlayerInAir
            int insertIndex = -1;
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i] is PModulePlayerInAir && 
                    !(modules[i] is EGlideringPlayerInAir)) // Не наш кастомный
                {
                    insertIndex = i + 1; // Вставляем после стандартного
                    break;
                }
            }
            
            // Создаём экземпляр вашего модуля
            var customModule = new EGlideringPlayerInAir();
            
            // Инициализируем модуль
            var initMethod = typeof(PModule).GetMethod("Initialize", 
                BindingFlags.Public | BindingFlags.Instance);
            initMethod?.Invoke(customModule, new object[] { null, entity });
            
            // Добавляем в список
            if (insertIndex >= 0 && insertIndex <= modules.Count)
                modules.Insert(insertIndex, customModule);
            else
                modules.Add(customModule);
            
            // Обновляем поле (на всякий случай)
            physicsModulesField.SetValue(behavior, modules);
        }
    }
}