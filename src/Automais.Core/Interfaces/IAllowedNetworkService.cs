using Automais.Core.DTOs;

namespace Automais.Core.Interfaces;

public interface IAllowedNetworkService
{
    Task<IEnumerable<AllowedNetworkDto>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default);
    Task<AllowedNetworkDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AllowedNetworkDto> CreateAsync(Guid routerId, CreateAllowedNetworkDto dto, CancellationToken cancellationToken = default);
    Task<AllowedNetworkDto> UpdateAsync(Guid id, UpdateAllowedNetworkDto dto, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
