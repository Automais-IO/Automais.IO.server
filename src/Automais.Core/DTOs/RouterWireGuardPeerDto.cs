namespace Automais.Core.DTOs;

/// <summary>
/// DTO para Router WireGuard Peer
/// </summary>
public class RouterWireGuardPeerDto
{
    public Guid Id { get; set; }
    public Guid RouterId { get; set; }
    public Guid VpnNetworkId { get; set; }
    public string PublicKey { get; set; } = string.Empty;
    public string AllowedIps { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
    /// <summary>Porta UDP do servidor WireGuard (da VpnNetwork associada).</summary>
    public int ListenPort { get; set; }
    public DateTime? LastHandshake { get; set; }
    public long? BytesReceived { get; set; }
    public long? BytesSent { get; set; }
    public bool? PingSuccess { get; set; }
    public double? PingAvgTimeMs { get; set; }
    public double? PingPacketLoss { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// DTO para criação de Router WireGuard Peer
/// </summary>
public class CreateRouterWireGuardPeerDto
{
    public Guid VpnNetworkId { get; set; }
    public string AllowedIps { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
}

/// <summary>
/// DTO para download de configuração WireGuard (.conf)
/// </summary>
public class RouterWireGuardConfigDto
{
    public string ConfigContent { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
}

/// <summary>
/// DTO para atualização de estatísticas do peer
/// </summary>
public class UpdatePeerStatsDto
{
    public DateTime? LastHandshake { get; set; }
    public long? BytesReceived { get; set; }
    public long? BytesSent { get; set; }
    public bool? PingSuccess { get; set; }
    public double? PingAvgTimeMs { get; set; }
    public double? PingPacketLoss { get; set; }
}

