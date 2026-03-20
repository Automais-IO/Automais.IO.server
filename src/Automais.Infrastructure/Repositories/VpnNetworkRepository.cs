using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Automais.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Automais.Infrastructure.Repositories;

/// <summary>
/// Implementação EF Core para redes VPN.
/// </summary>
public class VpnNetworkRepository : IVpnNetworkRepository
{
    private readonly ApplicationDbContext _context;

    public VpnNetworkRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<VpnNetwork>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.VpnNetworks
            .OrderBy(n => n.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<VpnNetwork?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.VpnNetworks
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<VpnNetwork>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0)
            return Enumerable.Empty<VpnNetwork>();

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
            return;

        _context.VpnNetworks.Remove(network);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> SlugExistsAsync(Guid tenantId, string slug, CancellationToken cancellationToken = default)
    {
        return await _context.VpnNetworks
            .AnyAsync(n => n.TenantId == tenantId && n.Slug == slug, cancellationToken);
    }

    public async Task<VpnNetwork?> GetDefaultOrFirstForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _context.VpnNetworks
            .Where(n => n.TenantId == tenantId)
            .OrderByDescending(n => n.IsDefault)
            .ThenBy(n => n.Name)
            .FirstOrDefaultAsync(cancellationToken);
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
