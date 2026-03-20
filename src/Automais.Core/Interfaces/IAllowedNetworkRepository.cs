using Automais.Core.Entities;

namespace Automais.Core.Interfaces;

public interface IAllowedNetworkRepository
{
    Task<AllowedNetwork?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<AllowedNetwork>> GetByVpnPeerIdAsync(Guid vpnPeerId, CancellationToken cancellationToken = default);
    Task<IEnumerable<AllowedNetwork>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default);
    Task<IEnumerable<AllowedNetwork>> GetAllByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<AllowedNetwork?> GetByRouterIdAndCidrAsync(Guid routerId, string networkCidr, CancellationToken cancellationToken = default);
    Task<AllowedNetwork> CreateAsync(AllowedNetwork network, CancellationToken cancellationToken = default);
    Task<AllowedNetwork> UpdateAsync(AllowedNetwork network, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteByRouterIdAndCidrAsync(Guid routerId, string networkCidr, CancellationToken cancellationToken = default);
}
