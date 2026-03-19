using Automais.Core.Entities;

namespace Automais.Core.Interfaces;

/// <summary>
/// Interface do repositório de Router Allowed Networks
/// </summary>
public interface IRouterAllowedNetworkRepository
{
    Task<RouterAllowedNetwork?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<RouterAllowedNetwork>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default);
    Task<IEnumerable<RouterAllowedNetwork>> GetAllByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<RouterAllowedNetwork?> GetByRouterIdAndCidrAsync(Guid routerId, string networkCidr, CancellationToken cancellationToken = default);
    Task<RouterAllowedNetwork> CreateAsync(RouterAllowedNetwork network, CancellationToken cancellationToken = default);
    Task<RouterAllowedNetwork> UpdateAsync(RouterAllowedNetwork network, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteByRouterIdAndCidrAsync(Guid routerId, string networkCidr, CancellationToken cancellationToken = default);
}

