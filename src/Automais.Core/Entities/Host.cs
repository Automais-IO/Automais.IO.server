namespace Automais.Core.Entities;

/// <summary>
/// Host gerenciado (Linux Ubuntu, etc.) acessível via VPN e SSH.
/// </summary>
public class Host
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Tipo de SO / perfil (ex.: Linux Ubuntu).</summary>
    public HostKind HostKind { get; set; } = HostKind.LinuxUbuntu;

    public Guid? VpnNetworkId { get; set; }

    /// <summary>IP ou hostname do host na rede VPN (onde o SSH escuta).</summary>
    public string VpnIp { get; set; } = string.Empty;

    public int SshPort { get; set; } = 22;

    public string SshUsername { get; set; } = string.Empty;

    public string? SshPassword { get; set; }

    public HostStatus Status { get; set; } = HostStatus.Offline;

    public DateTime? LastSeenAt { get; set; }

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public VpnNetwork? VpnNetwork { get; set; }
}

public enum HostKind
{
    LinuxUbuntu = 1
}

public enum HostStatus
{
    Online = 1,
    Offline = 2,
    Maintenance = 3,
    Error = 4
}
