using ElectricalProgressive.Net;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace Vintagestory.API.Common.Entities;

/// <summary>
/// Модуль физики полёта на элитре (обёртка для вызова методов из EGliderFlightPacketHandler)
/// </summary>
public class EGlideringPlayerInAir : PModuleInAir
{
    private float airMovingStrengthFalling;
    private EGliderFlightPacketHandler? handler;

    public override void Initialize(JsonObject config, Entity entity)
    {
        base.Initialize(config, entity);
        this.airMovingStrengthFalling = this.AirMovingStrength / 4f;
        
        // Получаем экземпляр обработчика
        handler = entity.World.Api.ModLoader.GetModSystem<EGliderFlightPacketHandler>();
    }

    public override void ApplyFlying(float dt, Entity entity, EntityPos pos, EntityControls controls)
    {
        if (handler != null)
        {
            handler.ApplyFlyingPhysics(dt, entity, pos, controls);
        }
        else
        {
            // Fallback: стандартная физика если обработчик не найден
            base.ApplyFlying(dt, entity, pos, controls);
        }
    }
}