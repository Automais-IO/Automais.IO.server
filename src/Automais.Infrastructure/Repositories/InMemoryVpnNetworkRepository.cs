using Automais.Core.Entities;
using Automais.Core.Interfaces;

namespace Automais.Infrastructure.Repositories;

public class InMemoryVpnNetworkRepository : IVpnNetworkRepository
{
    private readonly List<VpnNetwork> _networks = new();
    private readonly List<VpnNetworkMembership> _memberships = new();
    private readonly object _lock = new();

    public Task<IEnumerable<VpnNetwork>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IEnumerable<VpnNetwork>>(_networks.OrderBy(n => n.Name).ToList());
        }
    }

    public Task<VpnNetwork?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var network = _networks.FirstOrDefault(n => n.Id == id);
            return Task.FromResult(network);
        }
    }

    public Task<IEnumerable<VpnNetwork>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var idSet = ids.ToHashSet();
            var result = _networks
                .Where(n => idSet.Contains(n.Id))
                .ToList();

            return Task.FromResult<IEnumerable<VpnNetwork>>(result);
        }
    }

    public Task<IEnumerable<VpnNetwork>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var result = _networks
                .Where(n => n.TenantId == tenantId)
                .OrderBy(n => n.Name)
                .ToList();

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
            {
                _networks[index] = network;
            }

            return Task.FromResult(network);
        }
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var network = _networks.FirstOrDefault(n => n.Id == id);
            if (network != null)
            {
                _networks.Remove(network);
            }

            _memberships.RemoveAll(m => m.VpnNetworkId == id);
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

    public Task<IEnumerable<VpnNetworkMembership>> GetMembershipsByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var result = _memberships
                .Where(m => m.TenantUserId == userId)
                .ToList();

            return Task.FromResult<IEnumerable<VpnNetworkMembership>>(result);
        }
    }

    public Task<IEnumerable<VpnNetworkMembership>> GetMembershipsByNetworkIdAsync(Guid networkId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var result = _memberships
                .Where(m => m.VpnNetworkId == networkId)
                .ToList();

            return Task.FromResult<IEnumerable<VpnNetworkMembership>>(result);
        }
    }

    public Task<int> CountMembershipsByNetworkIdAsync(Guid networkId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var count = _memberships.Count(m => m.VpnNetworkId == networkId);
            return Task.FromResult(count);
        }
    }

    public Task ReplaceUserMembershipsAsync(Guid tenantId, Guid userId, IEnumerable<Guid> networkIds, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _memberships.RemoveAll(m => m.TenantUserId == userId);

            foreach (var networkId in networkIds.Distinct())
            {
                _memberships.Add(new VpnNetworkMembership
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    VpnNetworkId = networkId,
                    TenantUserId = userId,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveMembershipsByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _memberships.RemoveAll(m => m.TenantUserId == userId);
        }

        return Task.CompletedTask;
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


