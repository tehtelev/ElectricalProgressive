using System.Collections.Generic;
using ProtoBuf;

namespace ElectricalProgressive.Net;

public static class ClaimChunkCellState
{
    public const int Free = 0;
    public const int Own = 1;
    public const int Other = 2;
    public const int OutOfWorld = 3;
}

[ProtoContract]
public class ClaimMapRequestPacket
{
    [ProtoMember(1)]
    public int CenterChunkX { get; set; }

    [ProtoMember(2)]
    public int CenterChunkZ { get; set; }

    [ProtoMember(3)]
    public int Radius { get; set; }
}

[ProtoContract]
public class ClaimChunkActionPacket
{
    [ProtoMember(1)]
    public int ChunkX { get; set; }

    [ProtoMember(2)]
    public int ChunkZ { get; set; }

    [ProtoMember(3)]
    public int CenterChunkX { get; set; }

    [ProtoMember(4)]
    public int CenterChunkZ { get; set; }

    [ProtoMember(5)]
    public int Radius { get; set; }
}

[ProtoContract]
public class ClaimChunksBatchActionPacket
{
    [ProtoMember(1)]
    public List<ClaimChunkCoordPacket> Chunks { get; set; } = [];

    [ProtoMember(2)]
    public int CenterChunkX { get; set; }

    [ProtoMember(3)]
    public int CenterChunkZ { get; set; }

    [ProtoMember(4)]
    public int Radius { get; set; }
}

[ProtoContract]
public class ClaimChunkCoordPacket
{
    [ProtoMember(1)]
    public int ChunkX { get; set; }

    [ProtoMember(2)]
    public int ChunkZ { get; set; }
}

[ProtoContract]
public class ClaimMapStatePacket
{
    [ProtoMember(1)]
    public int CenterChunkX { get; set; }

    [ProtoMember(2)]
    public int CenterChunkZ { get; set; }

    [ProtoMember(3)]
    public int PlayerChunkX { get; set; }

    [ProtoMember(4)]
    public int PlayerChunkZ { get; set; }

    [ProtoMember(5)]
    public int Radius { get; set; }

    [ProtoMember(6)]
    public int ChunkSize { get; set; }

    [ProtoMember(7)]
    public int MapSizeX { get; set; }

    [ProtoMember(8)]
    public int MapSizeZ { get; set; }

    [ProtoMember(9)]
    public long UsedVolume { get; set; }

    [ProtoMember(10)]
    public long MaxVolume { get; set; }

    [ProtoMember(11)]
    public int UsedAreas { get; set; }

    [ProtoMember(12)]
    public int MaxAreas { get; set; }

    [ProtoMember(13)]
    public string Message { get; set; } = "";

    [ProtoMember(14)]
    public int MessageType { get; set; }

    [ProtoMember(15)]
    public List<ClaimChunkCellPacket> Chunks { get; set; } = [];
}

[ProtoContract]
public class ClaimChunkCellPacket
{
    [ProtoMember(1)]
    public int ChunkX { get; set; }

    [ProtoMember(2)]
    public int ChunkZ { get; set; }

    [ProtoMember(3)]
    public int State { get; set; }

    [ProtoMember(4)]
    public string OwnerName { get; set; } = "";
}