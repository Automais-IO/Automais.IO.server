namespace Automais.Core.DTOs;

/// <summary>DTO de linha em <c>vpn_peers</c>.</summary>
public class VpnPeerDto
{
    public Guid Id { get; set; }
    /// <summary>Null quando o peer é só de host Linux (sem Mikrotik).</summary>
    public Guid? RouterId { get; set; }
    public Guid VpnNetworkId { get; set; }
    public string PublicKey { get; set; } = string.Empty;
    /// <summary>IP do peer na VPN (ex: 10.100.1.50/32). Endereço usado para conectar ao router.</summary>
    public string PeerIp { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
    /// <summary>Porta UDP do servidor VPN (da VpnNetwork associada).</summary>
    public int ListenPort { get; set; }
    public DateTime? LastHandshake { get; set; }
    public long? BytesReceived { get; set; }
    public long? BytesSent { get; set; }
    public bool? PingSuccess { get; set; }
    public double? PingAvgTimeMs { get; set; }
    public double? PingPacketLoss { get; set; }
    /// <summary>Último ciclo do monitor VPN: link considerado ativo (handshake recente ou ICMP).</summary>
    public bool? ReachableViaVpn { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Criação/atualização de peer em <c>vpn_peers</c>.</summary>
public class CreateVpnPeerDto
{
    public Guid VpnNetworkId { get; set; }
    /// <summary>IP do peer na VPN (ex: 10.100.1.50/32). Pode incluir redes adicionais separadas por vírgula.</summary>
    public string PeerIp { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
}

/// <summary>Arquivo .conf montado sob demanda (não persiste em <c>vpn_peers</c>).</summary>
public class VpnPeerConfigDto
{
    public string ConfigContent { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
}

/// <summary>Atualização de estatísticas do peer (monitoramento).</summary>
public class UpdatePeerStatsDto
{
    public DateTime? LastHandshake { get; set; }
    public long? BytesReceived { get; set; }
    public long? BytesSent { get; set; }
    public bool? PingSuccess { get; set; }
    public double? PingAvgTimeMs { get; set; }
    public double? PingPacketLoss { get; set; }
    public bool? ReachableViaVpn { get; set; }
}
