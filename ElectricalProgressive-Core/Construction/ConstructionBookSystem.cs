using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ElectricalProgressive.Construction;

public class ConstructionBookSystem : ModSystem
{
    public const string Channel = "epconstructbook";

    public override void Start(ICoreAPI api)
    {
        api.RegisterItemClass("EConstructionBook", typeof(ItemEConstructionBook));
        api.Network.RegisterChannel(Channel)
            .RegisterMessageType<ConstructionBookSelectPacket>();
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Network.GetChannel(Channel)
            .SetMessageHandler<ConstructionBookSelectPacket>(OnServerSelect);
    }

    private static void OnServerSelect(IServerPlayer player, ConstructionBookSelectPacket packet)
    {
        var slot = player.InventoryManager?.ActiveHotbarSlot;
        if (slot?.Itemstack?.Item is not ItemEConstructionBook)
            return;
        if (string.IsNullOrEmpty(packet.Code))
            return;

        var block = player.Entity.World.GetBlock(new AssetLocation(packet.Code));
        if (block == null || !MachineConstructSystem.HasConstructionLevels(block))
            return;

        ConstructionCatalog.SetSelected(slot.Itemstack, packet.Code);
        slot.MarkDirty();
    }

    public static void SendSelect(ICoreClientAPI capi, string code)
    {
        capi.Network.GetChannel(Channel)
            .SendPacket(new ConstructionBookSelectPacket { Code = code });
    }


}
