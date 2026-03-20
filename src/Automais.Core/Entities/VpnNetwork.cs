namespace Automais.Core.Entities;

/// <summary>
/// Define uma rede lógica utilizada para entrega de VPN (WireGuard sob o capô).
/// </summary>
public class VpnNetwork
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Cidr { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public string? DnsServers { get; set; }
    
    /// <summary>
    /// Chave PRIVADA do servidor WireGuard para esta VPN.
    /// FONTE DE VERDADE: Salva no banco para recuperação de desastres.
    /// Nunca deve ser exposta na API.
    /// </summary>
    public string? ServerPrivateKey { get; set; }
    
    /// <summary>
    /// Chave PÚBLICA do servidor WireGuard para esta VPN.
    /// Derivada da ServerPrivateKey. Usada nos arquivos .conf dos clientes.
    /// </summary>
    public string? ServerPublicKey { get; set; }
    
    /// <summary>
    /// Endpoint do servidor VPN (ex: "automais.io"). 
    /// Identifica qual servidor VPN físico gerencia esta rede.
    /// O serviço Python usa este valor para identificar quais VpnNetworks ele deve gerenciar.
    /// </summary>
    public string? ServerEndpoint { get; set; }

    /// <summary>
    /// Porta UDP da interface WireGuard desta rede no servidor VPN.
    /// Deve ser única entre todas as VpnNetworks com o mesmo ServerEndpoint (mesma máquina).
    /// </summary>
    public int ListenPort { get; set; } = 51820;
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public ICollection<Device> Devices { get; set; } = new List<Device>();
}


