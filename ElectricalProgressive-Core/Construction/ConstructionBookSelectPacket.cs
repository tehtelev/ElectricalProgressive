using ProtoBuf;

namespace ElectricalProgressive.Construction;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ConstructionBookSelectPacket
{
    public string? Code { get; set; }
}
