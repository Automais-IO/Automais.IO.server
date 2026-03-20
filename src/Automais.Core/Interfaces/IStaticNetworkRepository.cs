using Automais.Core.Entities;

namespace Automais.Core.Interfaces;

public interface IStaticNetworkRepository
{
    Task<StaticNetwork?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<StaticNetwork>> GetByVpnPeerIdAsync(Guid vpnPeerId, CancellationToken cancellationToken = default);
    Task<IEnumerable<StaticNetwork>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default);
    Task<StaticNetwork?> GetByRouterIdAndDestinationAsync(Guid routerId, string destination, CancellationToken cancellationToken = default);
    Task<StaticNetwork> CreateAsync(StaticNetwork route, CancellationToken cancellationToken = default);
    Task<StaticNetwork> UpdateAsync(StaticNetwork route, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
