using Automais.Core.DTOs;

namespace Automais.Core.Interfaces;

/// <summary>
/// Serviço para gerenciamento de redes destino dos routers (redes para as quais o tráfego VPN é encaminhado).
/// </summary>
public interface IRouterAllowedNetworkService
{
    Task<IEnumerable<RouterAllowedNetworkDto>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default);
    Task<RouterAllowedNetworkDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RouterAllowedNetworkDto> CreateAsync(Guid routerId, CreateRouterAllowedNetworkDto dto, CancellationToken cancellationToken = default);
    Task<RouterAllowedNetworkDto> UpdateAsync(Guid id, UpdateRouterAllowedNetworkDto dto, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
