using Vintagestory.API.Common;

namespace ElectricalProgressive.Content.Block;

public abstract class BlockEntityEBase : BlockEntity
{
    public BEBehaviorElectricalProgressive? ElectricalProgressive => GetBehavior<BEBehaviorElectricalProgressive>();


    public const string AllEparamsKey = "electricalprogressive:allEparams";


    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        this.ElectricalProgressive?.OnBlockUnloaded(); // вызываем метод OnBlockUnloaded у BEBehaviorElectricalProgressive
    }


 
}