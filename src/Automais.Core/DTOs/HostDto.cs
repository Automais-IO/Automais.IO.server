using Automais.Core.Entities;

namespace Automais.Core.DTOs;

public class HostDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public HostKind HostKind { get; set; }
    public Guid? VpnNetworkId { get; set; }
    public string? VpnNetworkServerEndpoint { get; set; }
    /// <summary>IP do túnel na VPN (derivado de <c>vpn_peers.PeerIp</c>).</summary>
    public string VpnIp { get; set; } = string.Empty;
    public int SshPort { get; set; }
    public string SshUsername { get; set; } = string.Empty;
    public HostProvisioningStatus ProvisioningStatus { get; set; }
    public HostStatus Status { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string? Description { get; set; }
    public string? MetricsJson { get; set; }
    public DateTime? LastMetricsAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>FK para <c>vpn_peers</c>.</summary>
    public Guid? VpnPeerId { get; set; }
    public bool VpnPeerKeysConfigured { get; set; }

    /// <summary>Timestamp de quando o setup foi solicitado (para controle de expiração).</summary>
    public DateTime? SetupRequestedAt { get; set; }

    /// <summary>Estatísticas do peer VPN (<c>vpn_peers</c>) — atualizadas pelo serviço vpnserver.</summary>
    public double? VpnPeerPingAvgTimeMs { get; set; }
    public bool? VpnPeerPingSuccess { get; set; }
    public double? VpnPeerPingPacketLoss { get; set; }
    public DateTime? VpnPeerLastHandshake { get; set; }

    /// <summary>Último ciclo do monitor VPN no servidor (campo <c>ReachableViaVpn</c> em <c>vpn_peers</c>).</summary>
    public bool? VpnPeerReachableViaVpn { get; set; }
}

public class CreateHostDto
{
    public string Name { get; set; } = string.Empty;
    public HostKind HostKind { get; set; } = HostKind.LinuxUbuntu;
    public Guid VpnNetworkId { get; set; }
    /// <summary>IP manual (opcional); se vazio, aloca automaticamente.</summary>
    public string? VpnIp { get; set; }
    public int SshPort { get; set; } = 22;
    public string? Description { get; set; }
}

/// <summary>
/// DTO estendido retornado apenas para requests internos (serviço Python).
/// Inclui credenciais SSH que nunca devem ser expostas ao browser.
/// </summary>
public class InternalHostDto : HostDto
{
    public string? SshPrivateKey { get; set; }
    public string? SshPassword { get; set; }
}

public class UpdateHostDto
{
    public string? Name { get; set; }
    public HostKind? HostKind { get; set; }
    public Guid? VpnNetworkId { get; set; }
    public string? VpnIp { get; set; }
    public HostProvisioningStatus? ProvisioningStatus { get; set; }
    public HostStatus? Status { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string? Description { get; set; }
    public string? MetricsJson { get; set; }
    public DateTime? LastMetricsAt { get; set; }
}
