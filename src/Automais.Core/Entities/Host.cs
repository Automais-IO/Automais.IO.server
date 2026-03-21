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

    /// <summary>Peer VPN na tabela <c>vpn_peers</c> (host sem router MikroTik).</summary>
    public Guid? VpnPeerId { get; set; }

    public int SshPort { get; set; } = 22;

    /// <summary>Usuário SSH criado pelo bootstrap (padrão: automais-io).</summary>
    public string SshUsername { get; set; } = "automais-io";

    /// <summary>Chave privada SSH Ed25519 gerada no servidor (usada pelo serviço hosts para autenticar).</summary>
    public string? SshPrivateKey { get; set; }

    /// <summary>Chave pública SSH correspondente (copiada no bootstrap para authorized_keys).</summary>
    public string? SshPublicKey { get; set; }

    /// <summary>Senha (plaintext efêmero) — usada durante bootstrap / sudo e pelo serviço Python para SSH.</summary>
    public string? SshPassword { get; set; }

    /// <summary>Hash bcrypt da senha SSH (para validação futura sem expor plaintext).</summary>
    public string? SshPasswordHash { get; set; }

    /// <summary>Timestamp de quando o usuário clicou em "Conectar-se"; o script expira após 10 min.</summary>
    public DateTime? SetupRequestedAt { get; set; }

    /// <summary>Token efêmero (só no servidor) para o host confirmar fim do setup via POST público; gerado ao solicitar setup (Conectar-se).</summary>
    public string? SetupCompletionToken { get; set; }

    public HostProvisioningStatus ProvisioningStatus { get; set; } = HostProvisioningStatus.PendingInstall;
    public HostStatus Status { get; set; } = HostStatus.Offline;

    public DateTime? LastSeenAt { get; set; }
    public string? Description { get; set; }

    /// <summary>Métricas coletadas periodicamente via SSH (JSON).</summary>
    public string? MetricsJson { get; set; }
    public DateTime? LastMetricsAt { get; set; }

    /// <summary>Relatado pelo serviço hosts (Python): há sessão interativa (PTY) ativa ou destacada.</summary>
    public bool SshInteractiveSessionOpen { get; set; }

    /// <summary>Início UTC da sessão interativa mais antiga ainda ativa neste host (painel web).</summary>
    public DateTime? SshInteractiveSessionSince { get; set; }

    /// <summary>Última vez que o serviço hosts atualizou os campos de sessão interativa.</summary>
    public DateTime? LastSshInteractiveReportAt { get; set; }

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
