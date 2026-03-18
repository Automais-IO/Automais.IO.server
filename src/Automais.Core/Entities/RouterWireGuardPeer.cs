namespace Automais.Core.Entities;

/// <summary>
/// Representa um peer WireGuard de um Router.
/// Cada router pode ter um ou mais peers em diferentes redes VPN.
/// </summary>
public class RouterWireGuardPeer
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// ID do router
    /// </summary>
    public Guid RouterId { get; set; }
    
    /// <summary>
    /// ID da rede VPN
    /// </summary>
    public Guid VpnNetworkId { get; set; }
    
    /// <summary>
    /// Chave pública WireGuard
    /// </summary>
    public string PublicKey { get; set; } = string.Empty;
    
    /// <summary>
    /// Chave privada WireGuard (criptografada no banco)
    /// </summary>
    public string PrivateKey { get; set; } = string.Empty;
    
    /// <summary>
    /// IPs permitidos (ex: "10.100.1.50/32")
    /// </summary>
    public string AllowedIps { get; set; } = string.Empty;
    
    /// <summary>
    /// Endpoint do portal (IP público)
    /// </summary>
    public string? Endpoint { get; set; }
    
    /// <summary>
    /// Último handshake WireGuard
    /// </summary>
    public DateTime? LastHandshake { get; set; }
    
    /// <summary>
    /// Bytes recebidos
    /// </summary>
    public long? BytesReceived { get; set; }
    
    /// <summary>
    /// Bytes enviados
    /// </summary>
    public long? BytesSent { get; set; }
    
    /// <summary>
    /// Último ping bem-sucedido
    /// </summary>
    public bool? PingSuccess { get; set; }
    
    /// <summary>
    /// Latência média do ping em milissegundos
    /// </summary>
    public double? PingAvgTimeMs { get; set; }
    
    /// <summary>
    /// Perda de pacotes do ping em porcentagem
    /// </summary>
    public double? PingPacketLoss { get; set; }
    
    /// <summary>
    /// Peer habilitado
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// Conteúdo completo do arquivo de configuração WireGuard (.conf)
    /// Guardado no banco para download a qualquer momento
    /// </summary>
    public string? ConfigContent { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Navigation Properties
    public Router Router { get; set; } = null!;
    public VpnNetwork VpnNetwork { get; set; } = null!;
}

