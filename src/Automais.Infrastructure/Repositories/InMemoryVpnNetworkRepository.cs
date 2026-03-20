using Automais.Core.Entities;
using Automais.Core.Interfaces;

namespace Automais.Infrastructure.Repositories;

public class InMemoryVpnNetworkRepository : IVpnNetworkRepository
{
    private readonly List<VpnNetwork> _networks = new();
    private readonly object _lock = new();

    public Task<IEnumerable<VpnNetwork>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
            return Task.FromResult<IEnumerable<VpnNetwork>>(_networks.OrderBy(n => n.Name).ToList());
    }

    public Task<VpnNetwork?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
            return Task.FromResult(_networks.FirstOrDefault(n => n.Id == id));
    }

    public Task<IEnumerable<VpnNetwork>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var idSet = ids.ToHashSet();
            var result = _networks.Where(n => idSet.Contains(n.Id)).ToList();
            return Task.FromResult<IEnumerable<VpnNetwork>>(result);
        }
    }

    public Task<IEnumerable<VpnNetwork>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var result = _networks.Where(n => n.TenantId == tenantId).OrderBy(n => n.Name).ToList();
            return Task.FromResult<IEnumerable<VpnNetwork>>(result);
        }
    }

    public Task<VpnNetwork> CreateAsync(VpnNetwork network, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _networks.Add(network);
            return Task.FromResult(network);
        }
    }

    public Task<VpnNetwork> UpdateAsync(VpnNetwork network, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var index = _networks.FindIndex(n => n.Id == network.Id);
            if (index >= 0)
                _networks[index] = network;
            return Task.FromResult(network);
        }
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var network = _networks.FirstOrDefault(n => n.Id == id);
            if (network != null)
                _networks.Remove(network);
        }

        return Task.CompletedTask;
    }

    public Task<bool> SlugExistsAsync(Guid tenantId, string slug, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var exists = _networks.Any(n =>
                n.TenantId == tenantId &&
                n.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(exists);
        }
    }

    public Task<VpnNetwork?> GetDefaultOrFirstForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var x = _networks
                .Where(n => n.TenantId == tenantId)
                .OrderByDescending(n => n.IsDefault)
                .ThenBy(n => n.Name)
                .FirstOrDefault();
            return Task.FromResult(x);
        }
    }

    public Task<IReadOnlyList<int>> GetListenPortsForServerEndpointAsync(string? serverEndpoint, Guid? excludeNetworkId, CancellationToken cancellationToken = default)
    {
        var norm = string.IsNullOrWhiteSpace(serverEndpoint) ? "" : serverEndpoint.Trim().ToLowerInvariant();
        lock (_lock)
        {
            var ports = _networks
                .Where(n =>
                    (string.IsNullOrWhiteSpace(n.ServerEndpoint) ? "" : n.ServerEndpoint.Trim().ToLowerInvariant()) == norm
                    && (!excludeNetworkId.HasValue || n.Id != excludeNetworkId.Value))
                .Select(n => n.ListenPort > 0 ? n.ListenPort : 51820)
                .ToList();
            return Task.FromResult<IReadOnlyList<int>>(ports);
        }
    }
}
