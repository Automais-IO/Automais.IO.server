namespace Automais.Core.Entities;

/// <summary>
/// Representa um Router Mikrotik gerenciado pela plataforma.
/// </summary>
public class Router
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public string? Model { get; set; }
    public string? FirmwareVersion { get; set; }

    /// <summary>URL da API RouterOS (ex: IP VPN:8728).</summary>
    public string? RouterOsApiUrl { get; set; }

    /// <summary>Usuário da API RouterOS no MikroTik (coluna RouterOsApiUsername).</summary>
    public string? ApiUsername { get; set; }

    /// <summary>Senha temporária/inicial (provisionamento .rsc ou primeira carga). Depois do primeiro sucesso, limpa-se e usa-se <see cref="ApiPassword"/>.</summary>
    public string? ApiPasswordTemporaria { get; set; }

    /// <summary>Senha definitiva da API após rotação (ou já definida no MK).</summary>
    public string? ApiPassword { get; set; }

    public bool AutomaisApiUserCreated { get; set; } = false;
    public Guid? VpnNetworkId { get; set; }

    public RouterOsApiAuthStatus RouterOsApiAuthStatus { get; set; } = RouterOsApiAuthStatus.Unknown;
    public DateTime? RouterOsApiAuthCheckedAt { get; set; }
    public string? RouterOsApiAuthMessage { get; set; }

    public RouterStatus Status { get; set; } = RouterStatus.Offline;
    public DateTime? LastSeenAt { get; set; }
    public int? Latency { get; set; }
    public string? HardwareInfo { get; set; }
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public VpnNetwork? VpnNetwork { get; set; }
    public ICollection<RouterWireGuardPeer> WireGuardPeers { get; set; } = new List<RouterWireGuardPeer>();
    public ICollection<RouterConfigLog> ConfigLogs { get; set; } = new List<RouterConfigLog>();
    public ICollection<RouterBackup> Backups { get; set; } = new List<RouterBackup>();
    public ICollection<RouterStaticRoute> StaticRoutes { get; set; } = new List<RouterStaticRoute>();
}

public enum RouterStatus
{
    Online = 1,
    Offline = 2,
    Maintenance = 3,
    Error = 4
}

public enum RouterOsApiAuthStatus
{
    Unknown = 0,
    Ok = 1,
    AuthFailed = 2,
    Unreachable = 3
}
