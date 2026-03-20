using Automais.Core.Entities;

namespace Automais.Core.Interfaces;

public interface IVpnNetworkRepository
{
    /// <summary>
    /// Obtém todas as VpnNetworks do sistema (para sincronização com servidor VPN).
    /// </summary>
    Task<IEnumerable<VpnNetwork>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<VpnNetwork?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<VpnNetwork>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IEnumerable<VpnNetwork>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);
    Task<VpnNetwork> CreateAsync(VpnNetwork network, CancellationToken cancellationToken = default);
    Task<VpnNetwork> UpdateAsync(VpnNetwork network, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> SlugExistsAsync(Guid tenantId, string slug, CancellationToken cancellationToken = default);

    /// <summary>Rede padrão do tenant; se não houver, a primeira por nome (VPN de usuário / provisionamento).</summary>
    Task<VpnNetwork?> GetDefaultOrFirstForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Portas UDP já usadas por redes com o mesmo ServerEndpoint (case-insensitive).</summary>
    Task<IReadOnlyList<int>> GetListenPortsForServerEndpointAsync(string? serverEndpoint, Guid? excludeNetworkId, CancellationToken cancellationToken = default);
}
