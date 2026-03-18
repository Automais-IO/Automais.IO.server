using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Automais.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Automais.Infrastructure.Repositories;

/// <summary>
/// Implementação EF Core para redes VPN e memberships.
/// </summary>
public class VpnNetworkRepository : IVpnNetworkRepository
{
    private readonly ApplicationDbContext _context;

    public VpnNetworkRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Obtém todas as VpnNetworks do sistema (para sincronização WireGuard)
    /// </summary>
    public async Task<IEnumerable<VpnNetwork>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.VpnNetworks
            .OrderBy(n => n.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<VpnNetwork?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.VpnNetworks
            .Include(n => n.Memberships)
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<VpnNetwork>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            return Enumerable.Empty<VpnNetwork>();
        }

        return await _context.VpnNetworks
            .Where(n => idList.Contains(n.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<VpnNetwork>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _context.VpnNetworks
            .Where(n => n.TenantId == tenantId)
            .OrderBy(n => n.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<VpnNetwork> CreateAsync(VpnNetwork network, CancellationToken cancellationToken = default)
    {
        _context.VpnNetworks.Add(network);
        await _context.SaveChangesAsync(cancellationToken);
        return network;
    }

    public async Task<VpnNetwork> UpdateAsync(VpnNetwork network, CancellationToken cancellationToken = default)
    {
        _context.VpnNetworks.Update(network);
        await _context.SaveChangesAsync(cancellationToken);
        return network;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var network = await _context.VpnNetworks.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (network == null)
        {
            return;
        }

        _context.VpnNetworks.Remove(network);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> SlugExistsAsync(Guid tenantId, string slug, CancellationToken cancellationToken = default)
    {
        return await _context.VpnNetworks
            .AnyAsync(n => n.TenantId == tenantId && n.Slug == slug, cancellationToken);
    }

    public async Task<IEnumerable<VpnNetworkMembership>> GetMembershipsByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.VpnNetworkMemberships
            .Where(m => m.TenantUserId == userId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<VpnNetworkMembership>> GetMembershipsByNetworkIdAsync(Guid networkId, CancellationToken cancellationToken = default)
    {
        return await _context.VpnNetworkMemberships
            .Where(m => m.VpnNetworkId == networkId)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountMembershipsByNetworkIdAsync(Guid networkId, CancellationToken cancellationToken = default)
    {
        return await _context.VpnNetworkMemberships
            .CountAsync(m => m.VpnNetworkId == networkId, cancellationToken);
    }

    public async Task ReplaceUserMembershipsAsync(Guid tenantId, Guid userId, IEnumerable<Guid> networkIds, CancellationToken cancellationToken = default)
    {
        var currentMemberships = await _context.VpnNetworkMemberships
            .Where(m => m.TenantUserId == userId)
            .ToListAsync(cancellationToken);

        _context.VpnNetworkMemberships.RemoveRange(currentMemberships);

        foreach (var networkId in networkIds.Distinct())
        {
            _context.VpnNetworkMemberships.Add(new VpnNetworkMembership
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                VpnNetworkId = networkId,
                TenantUserId = userId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveMembershipsByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var memberships = await _context.VpnNetworkMemberships
            .Where(m => m.TenantUserId == userId)
            .ToListAsync(cancellationToken);

        if (memberships.Count == 0)
        {
            return;
        }

        _context.VpnNetworkMemberships.RemoveRange(memberships);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<int>> GetListenPortsForServerEndpointAsync(string? serverEndpoint, Guid? excludeNetworkId, CancellationToken cancellationToken = default)
    {
        var norm = NormalizeServerEndpoint(serverEndpoint);
        var list = await _context.VpnNetworks.AsNoTracking().ToListAsync(cancellationToken);
        return list
            .Where(n => NormalizeServerEndpoint(n.ServerEndpoint) == norm
                && (!excludeNetworkId.HasValue || n.Id != excludeNetworkId.Value))
            .Select(n => n.ListenPort)
            .ToList();
    }

    private static string NormalizeServerEndpoint(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "" : s.Trim().ToLowerInvariant();
}


