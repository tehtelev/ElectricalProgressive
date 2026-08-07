using Vintagestory.API.Client;
using Vintagestory.API.Common;
using MachineConstruct = global::ElectricalProgressive.Construction.BEBehaviorMachineConstruct;

namespace ElectricalProgressive.Content.Block.Termoplastini;

/// <summary>
/// BE для MachineConstruct (incomplete blueprint / stage mesh).
/// </summary>
public class BlockEntityTermoplastini : BlockEntity
{
    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        var construct = GetBehavior<MachineConstruct>();
        if (construct is { IsRenderingBlueprint: true })
            return base.OnTesselation(mesher, tesselator);

        return base.OnTesselation(mesher, tesselator);
    }
}
