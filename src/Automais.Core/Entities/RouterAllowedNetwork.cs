namespace Automais.Core.Entities;

/// <summary>
/// Representa uma rede destino para um Router: rede para a qual o tráfego VPN é encaminhado via este tunnel (iptables/WireGuard).
/// Ao adicionar uma rede destino, o PeerIp do peer WireGuard é atualizado para que o sistema saiba para qual interface encaminhar o tráfego.
/// </summary>
public class RouterAllowedNetwork
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// ID do router
    /// </summary>
    public Guid RouterId { get; set; }
    
    /// <summary>
    /// CIDR da rede destino (ex: "10.0.1.0/24", "192.168.100.0/24")
    /// </summary>
    public string NetworkCidr { get; set; } = string.Empty;
    
    /// <summary>
    /// Descrição opcional da rede
    /// </summary>
    public string? Description { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    // Navigation Properties
    public Router Router { get; set; } = null!;
}

