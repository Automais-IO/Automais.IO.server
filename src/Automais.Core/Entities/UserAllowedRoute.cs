namespace Automais.Core.Entities;

/// <summary>
/// Representa uma rota (rede) permitida para um usuário acessar via VPN.
/// Quando um usuário se conecta à VPN, essas rotas são adicionadas temporariamente
/// ao sistema operacional para rotear o tráfego através do gateway VPN.
/// </summary>
public class UserAllowedRoute
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// ID do usuário
    /// </summary>
    public Guid UserId { get; set; }
    
    /// <summary>
    /// ID do router que possui esta rota
    /// </summary>
    public Guid RouterId { get; set; }
    
    /// <summary>
    /// ID da rede permitida (<see cref="AllowedNetwork"/>).
    /// </summary>
    public Guid AllowedNetworkId { get; set; }
    
    /// <summary>
    /// CIDR da rede permitida (ex: "10.0.1.0/24")
    /// </summary>
    public string NetworkCidr { get; set; } = string.Empty;
    
    /// <summary>
    /// Descrição da rota (opcional)
    /// </summary>
    public string? Description { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Navigation Properties
    public TenantUser User { get; set; } = null!;
    public Router Router { get; set; } = null!;
    public AllowedNetwork AllowedNetwork { get; set; } = null!;
}

