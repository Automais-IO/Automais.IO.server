namespace Automais.Core.Entities;

/// <summary>
/// Rede destino para um Router: tráfego VPN encaminhado via este túnel.
/// Ao adicionar uma rede destino, o PeerIp do peer VPN é atualizado para o encaminhamento correto.
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

