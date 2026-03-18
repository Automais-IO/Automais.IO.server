namespace Automais.Core.Entities;

/// <summary>
/// Representa um Router Mikrotik gerenciado pela plataforma.
/// </summary>
public class Router
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// ID do tenant proprietário
    /// </summary>
    public Guid TenantId { get; set; }
    
    /// <summary>
    /// Nome descritivo do router
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Número de série do router
    /// </summary>
    public string? SerialNumber { get; set; }
    
    /// <summary>
    /// Modelo do router (ex: RB750, RB4011, etc)
    /// </summary>
    public string? Model { get; set; }
    
    /// <summary>
    /// Versão do firmware RouterOS
    /// </summary>
    public string? FirmwareVersion { get; set; }
    
    /// <summary>
    /// URL da API RouterOS (ex: "192.168.1.1:8728" - via WireGuard)
    /// </summary>
    public string? RouterOsApiUrl { get; set; }
    
    /// <summary>
    /// Usuário da API RouterOS
    /// </summary>
    public string? RouterOsApiUsername { get; set; }
    
    /// <summary>
    /// Senha da API RouterOS (criptografada no banco)
    /// Usada inicialmente para testar conexão e criar usuário automais-io-api
    /// </summary>
    public string? RouterOsApiPassword { get; set; }
    
    /// <summary>
    /// Senha do usuário automais-io-api criado automaticamente (texto plano inicialmente)
    /// </summary>
    public string? AutomaisApiPassword { get; set; }
    
    /// <summary>
    /// Indica se o usuário automais-io-api foi criado no router
    /// </summary>
    public bool AutomaisApiUserCreated { get; set; } = false;
    
    /// <summary>
    /// ID da rede VPN WireGuard associada
    /// </summary>
    public Guid? VpnNetworkId { get; set; }
    
    /// <summary>
    /// Último teste da API RouterOS (8728), independente do status VPN.
    /// </summary>
    public RouterOsApiAuthStatus RouterOsApiAuthStatus { get; set; } = RouterOsApiAuthStatus.Unknown;

    /// <summary>
    /// Quando o status da API RouterOS foi verificado pela última vez.
    /// </summary>
    public DateTime? RouterOsApiAuthCheckedAt { get; set; }

    /// <summary>
    /// Mensagem curta do último erro (auth, timeout, firewall, etc.).
    /// </summary>
    public string? RouterOsApiAuthMessage { get; set; }

    /// <summary>
    /// Status atual do router (VPN / disponibilidade geral)
    /// </summary>
    public RouterStatus Status { get; set; } = RouterStatus.Offline;
    
    /// <summary>
    /// Última vez que o router foi visto online
    /// </summary>
    public DateTime? LastSeenAt { get; set; }
    
    /// <summary>
    /// Latência do ping em milissegundos (última medição)
    /// </summary>
    public int? Latency { get; set; }
    
    /// <summary>
    /// Informações de hardware (JSON): CPU, RAM, temperatura, etc
    /// </summary>
    public string? HardwareInfo { get; set; }
    
    /// <summary>
    /// Descrição opcional
    /// </summary>
    public string? Description { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Navigation Properties
    public Tenant Tenant { get; set; } = null!;
    public VpnNetwork? VpnNetwork { get; set; }
    public ICollection<RouterWireGuardPeer> WireGuardPeers { get; set; } = new List<RouterWireGuardPeer>();
    public ICollection<RouterConfigLog> ConfigLogs { get; set; } = new List<RouterConfigLog>();
    public ICollection<RouterBackup> Backups { get; set; } = new List<RouterBackup>();
    public ICollection<RouterStaticRoute> StaticRoutes { get; set; } = new List<RouterStaticRoute>();
}

public enum RouterStatus
{
    Online = 1,        // Router online e operacional
    Offline = 2,        // Router offline
    Maintenance = 3,    // Router em manutenção
    Error = 4           // Erro de conexão
}

/// <summary>
/// Resultado do acesso à API RouterOS (usuário/senha + rede até :8728).
/// </summary>
public enum RouterOsApiAuthStatus
{
    Unknown = 0,
    Ok = 1,
    AuthFailed = 2,
    Unreachable = 3
}

