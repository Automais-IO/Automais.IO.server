namespace Automais.Core.DTOs;

/// <summary>
/// DTO para rede destino de um router (rede para a qual o tráfego VPN é encaminhado via este tunnel WG/iptables).
/// </summary>
public class RouterAllowedNetworkDto
{
    public Guid Id { get; set; }
    public Guid RouterId { get; set; }
    public string NetworkCidr { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// DTO para criação de rede destino.
/// </summary>
public class CreateRouterAllowedNetworkDto
{
    public string NetworkCidr { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>
/// DTO para atualização de rede destino.
/// </summary>
public class UpdateRouterAllowedNetworkDto
{
    public string? NetworkCidr { get; set; }
    public string? Description { get; set; }
}
