using Automais.Core.DTOs;

namespace Automais.Core.Interfaces;

/// <summary>Serviço de peers na tabela <c>vpn_peers</c> (roteadores e hosts).</summary>
public interface IVpnPeerService
{
    Task<IEnumerable<VpnPeerDto>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default);
    Task<VpnPeerDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<VpnPeerDto> CreatePeerAsync(Guid routerId, CreateVpnPeerDto dto, CancellationToken cancellationToken = default);
    Task<VpnPeerDto> UpdatePeerAsync(Guid id, CreateVpnPeerDto dto, CancellationToken cancellationToken = default);
    Task UpdatePeerStatsAsync(Guid id, UpdatePeerStatsDto dto, CancellationToken cancellationToken = default);
    Task DeletePeerAsync(Guid id, CancellationToken cancellationToken = default);
    Task<VpnPeerConfigDto> GetConfigAsync(Guid id, CancellationToken cancellationToken = default);
    Task<VpnPeerDto> RegenerateKeysAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>No-op: o .conf é gerado sob demanda ao baixar/consultar a config do peer.</summary>
    Task RefreshPeerConfigsForNetworkAsync(Guid vpnNetworkId, CancellationToken cancellationToken = default);
}
