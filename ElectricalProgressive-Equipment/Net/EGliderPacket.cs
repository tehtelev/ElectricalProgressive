using ProtoBuf;

namespace ElectricalProgressive.Net;

/// <summary>
/// Пакет для включения/выключения форсажа элитра
/// </summary>
[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class EGliderAfterburnerPacket
{
    /// <summary>UID игрока, отправляющего пакет</summary>
    public string PlayerUID { get; set; }
    
    /// <summary>Активен ли форсаж</summary>
    public bool IsActive { get; set; }
}