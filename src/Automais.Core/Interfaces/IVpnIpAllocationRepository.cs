using Automais.Core.Entities;

namespace Automais.Core.Interfaces;

public interface IVpnIpAllocationRepository
{
    Task<IEnumerable<VpnIpAllocation>> GetByNetworkIdAsync(Guid vpnNetworkId, bool onlyActive = true, CancellationToken cancellationToken = default);
    Task<VpnIpAllocation?> GetByResourceAsync(Guid vpnNetworkId, VpnResourceKind kind, Guid resourceId, CancellationToken cancellationToken = default);
    Task<VpnIpAllocation> CreateAsync(VpnIpAllocation allocation, CancellationToken cancellationToken = default);
    Task ReleaseAsync(Guid vpnNetworkId, VpnResourceKind kind, Guid resourceId, CancellationToken cancellationToken = default);
    Task<bool> IsAddressTakenAsync(Guid vpnNetworkId, string address, CancellationToken cancellationToken = default);
}
