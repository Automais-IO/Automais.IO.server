using Automais.Core.Entities;

namespace Automais.Core.Interfaces;

/// <summary>Repositório da tabela <c>vpn_peers</c>.</summary>
public interface IVpnPeerRepository
{
    Task<VpnPeer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<VpnPeer?> GetByRouterIdAndNetworkIdAsync(Guid routerId, Guid vpnNetworkId, CancellationToken cancellationToken = default);
    Task<IEnumerable<VpnPeer>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default);
    Task<IEnumerable<VpnPeer>> GetByVpnNetworkIdAsync(Guid vpnNetworkId, CancellationToken cancellationToken = default);
    /// <summary>Todos os peers das redes indicadas (servidor VPN / sync).</summary>
    Task<IEnumerable<VpnPeer>> GetByVpnNetworkIdsAsync(IEnumerable<Guid> vpnNetworkIds, CancellationToken cancellationToken = default);
    Task<IEnumerable<VpnPeer>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<VpnPeer> CreateAsync(VpnPeer peer, CancellationToken cancellationToken = default);
    Task<VpnPeer> UpdateAsync(VpnPeer peer, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>IPs alocados na rede (campo <c>PeerIp</c>, pode conter vírgulas).</summary>
    Task<IEnumerable<string>> GetAllocatedIpsByNetworkAsync(Guid vpnNetworkId, CancellationToken cancellationToken = default);
}
