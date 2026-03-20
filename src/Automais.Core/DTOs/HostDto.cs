using Automais.Core.Entities;

namespace Automais.Core.DTOs;

public class HostDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public HostKind HostKind { get; set; }
    public Guid? VpnNetworkId { get; set; }
    /// <summary>Endpoint do servidor VPN (para WebSocket dinâmico).</summary>
    public string? VpnNetworkServerEndpoint { get; set; }
    public string VpnIp { get; set; } = string.Empty;
    public int SshPort { get; set; }
    public string SshUsername { get; set; } = string.Empty;
    public string? SshPassword { get; set; }
    public HostStatus Status { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateHostDto
{
    public string Name { get; set; } = string.Empty;
    public HostKind HostKind { get; set; } = HostKind.LinuxUbuntu;
    public Guid? VpnNetworkId { get; set; }
    public string VpnIp { get; set; } = string.Empty;
    public int SshPort { get; set; } = 22;
    public string SshUsername { get; set; } = string.Empty;
    public string? SshPassword { get; set; }
    public string? Description { get; set; }
}

public class UpdateHostDto
{
    public string? Name { get; set; }
    public HostKind? HostKind { get; set; }
    public Guid? VpnNetworkId { get; set; }
    public string? VpnIp { get; set; }
    public int? SshPort { get; set; }
    public string? SshUsername { get; set; }
    public string? SshPassword { get; set; }
    public HostStatus? Status { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string? Description { get; set; }
}
