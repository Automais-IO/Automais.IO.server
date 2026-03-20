using Automais.Core.DTOs;

namespace Automais.Core.Interfaces;

public interface IStaticNetworkService
{
    Task<IEnumerable<StaticNetworkDto>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default);
    Task<StaticNetworkDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<StaticNetworkDto> CreateAsync(Guid routerId, CreateStaticNetworkDto dto, CancellationToken cancellationToken = default);
    Task<StaticNetworkDto> UpdateAsync(Guid id, UpdateStaticNetworkDto dto, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task BatchUpdateStatusAsync(Guid routerId, BatchUpdateStaticNetworksDto dto, CancellationToken cancellationToken = default);
    Task UpdateStaticNetworkStatusAsync(UpdateStaticNetworkStatusDto dto, CancellationToken cancellationToken = default);
}
