using ElectricalProgressive.Content.Storage;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

[assembly: ModDependency("game", "1.22.0")]
[assembly: ModDependency("electricalprogressivecore", "3.3.0")]
[assembly: ModDependency("electricalprogressivebasics", "3.3.0")]
[assembly: ModInfo(
    "Electrical Progressive: Storage",
    "electricalprogressivestorage",
    Website = "https://github.com/tehtelev/ElectricalProgressive",
    Description = "Network storage: controller, drive, disks, liquid cells, terminals, a pattern encoder and an interface.",
    Version = "1.0.0",
    Authors = ["Tehtelev", "Kotl"]
)]

namespace ElectricalProgressive;

public class ElectricalProgressiveStorage : ModSystem
{
    private const string HarmonyId = "electricalprogressive.storage.stacksize";
    private Harmony? _harmony;

    public override void StartClientSide(ICoreClientAPI api)
    {
        _harmony = new Harmony(HarmonyId);
        StackSizeTextPatch.Apply(_harmony);
    }

    public override void Dispose()
    {
        _harmony?.UnpatchAll(HarmonyId);
    }

    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockClass("BlockEStorageCable", typeof(BlockEStorageCable));
        api.RegisterBlockClass("BlockEStorageController", typeof(BlockEStorageController));
        api.RegisterBlockClass("BlockEStorageDrive", typeof(BlockEStorageDrive));
        api.RegisterBlockClass("BlockEStorageTerminal", typeof(BlockEStorageTerminal));
        api.RegisterBlockClass("BlockEStorageInterface", typeof(BlockEStorageInterface));

        api.RegisterBlockEntityClass("BlockEntityEStorageCable", typeof(BlockEntityEStorageCable));
        api.RegisterBlockEntityClass("BlockEntityEStorageController", typeof(BlockEntityEStorageController));
        api.RegisterBlockEntityClass("BlockEntityEStorageDrive", typeof(BlockEntityEStorageDrive));
        api.RegisterBlockEntityClass("BlockEntityEStorageTerminal", typeof(BlockEntityEStorageTerminal));
        api.RegisterBlockEntityClass("BlockEntityEStorageLiquidTerminal", typeof(BlockEntityEStorageLiquidTerminal));
        api.RegisterBlockEntityClass("BlockEntityEStoragePatternTerminal", typeof(BlockEntityEStoragePatternTerminal));
        api.RegisterBlockEntityClass("BlockEntityEStorageInterface", typeof(BlockEntityEStorageInterface));

        api.RegisterBlockEntityBehaviorClass("BEBehaviorEStorageController", typeof(BEBehaviorEStorageController));
        api.RegisterItemClass("ItemEStorageDisk", typeof(ItemEStorageDisk));
        api.RegisterItemClass("ItemEStorageCell", typeof(ItemEStorageCell));
        api.RegisterItemClass("ItemEStoragePattern", typeof(ItemEStoragePattern));

        _harmony ??= new Harmony(HarmonyId);
        var merge = AccessTools.Method(typeof(CollectibleObject), nameof(CollectibleObject.TryMergeStacks));
        _harmony.Patch(merge, prefix: new HarmonyMethod(typeof(InventoryEStorageTerminal), nameof(InventoryEStorageTerminal.MergeIntoTerminal)));
    }
}
