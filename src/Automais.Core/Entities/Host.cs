namespace Automais.Core.Entities;

/// <summary>
/// Host gerenciado (Linux Ubuntu, etc.) acessível via VPN e SSH.
/// </summary>
public class Host
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public string Name { get; set; } = string.Empty;

    public HostKind HostKind { get; set; } = HostKind.LinuxUbuntu;

    public Guid? VpnNetworkId { get; set; }

    /// <summary>Peer WireGuard na tabela unificada <c>vpn_peers</c> (peer sem router, só rede VPN).</summary>
    public Guid? VpnPeerId { get; set; }

    /// <summary>IP do host na rede VPN (sem máscara, ex.: "10.100.1.50").</summary>
    public string VpnIp { get; set; } = string.Empty;

    public int SshPort { get; set; } = 22;

    /// <summary>Usuário SSH criado pelo bootstrap (padrão: automais-io).</summary>
    public string SshUsername { get; set; } = "automais-io";

    /// <summary>Chave privada SSH Ed25519 gerada no servidor (usada pelo serviço hosts para autenticar).</summary>
    public string? SshPrivateKey { get; set; }

    /// <summary>Chave pública SSH correspondente (copiada no bootstrap para authorized_keys).</summary>
    public string? SshPublicKey { get; set; }

    /// <summary>Senha (plaintext efêmero ou hash) — só usada durante bootstrap / sudo.</summary>
    public string? SshPassword { get; set; }

    public HostProvisioningStatus ProvisioningStatus { get; set; } = HostProvisioningStatus.PendingInstall;
    public HostStatus Status { get; set; } = HostStatus.Offline;

    public DateTime? LastSeenAt { get; set; }
    public string? Description { get; set; }

    /// <summary>Métricas coletadas periodicamente via SSH (JSON).</summary>
    public string? MetricsJson { get; set; }
    public DateTime? LastMetricsAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public VpnNetwork? VpnNetwork { get; set; }
    public VpnPeer? VpnPeer { get; set; }
}

public enum HostKind
{
    LinuxUbuntu = 1
}

public enum HostProvisioningStatus
{
    PendingInstall = 0,
    Installing = 1,
    Ready = 2,
    Error = 3
}

public enum HostStatus
{
    Online = 1,
    Offline = 2,
    Maintenance = 3,
    Error = 4
}
