namespace Automais.Core.DTOs;

/// <summary>
/// Rede permitida ao usuário (catálogo por tenant ou vínculo existente).
/// </summary>
public class AllowedNetworkForUserDto
{
    public Guid AllowedNetworkId { get; set; }
    public Guid RouterId { get; set; }
    public string RouterName { get; set; } = string.Empty;
    public string NetworkCidr { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>
/// Atualiza quais <see cref="AllowedNetwork"/> o usuário pode usar na VPN.
/// </summary>
public class UpdateUserAllowedNetworksDto
{
    public IEnumerable<Guid> AllowedNetworkIds { get; set; } = Enumerable.Empty<Guid>();
}
