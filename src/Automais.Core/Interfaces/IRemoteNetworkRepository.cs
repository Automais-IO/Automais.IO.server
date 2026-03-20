using Automais.Core.Entities;

namespace Automais.Core.Interfaces;

public interface IRemoteNetworkRepository
{
    Task<RemoteNetwork?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<RemoteNetwork>> GetByVpnPeerIdAsync(Guid vpnPeerId, CancellationToken cancellationToken = default);
    Task<IEnumerable<RemoteNetwork>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default);
    Task<RemoteNetwork?> GetByRouterIdAndCidrAsync(Guid routerId, string networkCidr, CancellationToken cancellationToken = default);
    Task<RemoteNetwork> CreateAsync(RemoteNetwork network, CancellationToken cancellationToken = default);
    Task<RemoteNetwork> UpdateAsync(RemoteNetwork network, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
