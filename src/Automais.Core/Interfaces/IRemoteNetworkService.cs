using Automais.Core.DTOs;

namespace Automais.Core.Interfaces;

public interface IRemoteNetworkService
{
    Task<IEnumerable<RemoteNetworkDto>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default);
    Task<RemoteNetworkDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RemoteNetworkDto> CreateAsync(Guid routerId, CreateRemoteNetworkDto dto, CancellationToken cancellationToken = default);
    Task<RemoteNetworkDto> UpdateAsync(Guid id, UpdateRemoteNetworkDto dto, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
