using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ElectricalProgressive.Content.Storage;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class EStorageOrderReply
{
    public bool NoCpu;
    public bool Started;
    public bool Failed;
    public int Bytes;
    public byte[] Cells = [];
    public string Text = "";
}

public static class EStorageOrderSync
{
    public const string Channel = "estorageorder";

    public static EStorageOrderPane? Current;

    public static void Handle(EStorageOrderReply reply)
    {
        Current?.Apply(reply);
    }

    public static void Send(IPlayer player, EStorageOrderReply reply)
    {
        if (player is not IServerPlayer server)
            return;
        if (server.Entity?.World?.Api is not ICoreServerAPI api)
            return;

        api.Network.GetChannel(Channel).SendPacket(reply, server);
    }
}
