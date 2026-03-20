using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Automais.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Automais.Infrastructure.Repositories;

public class AllowedNetworkRepository : IAllowedNetworkRepository
{
    private readonly ApplicationDbContext _context;

    public AllowedNetworkRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AllowedNetwork?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Set<AllowedNetwork>()
            .Include(n => n.VpnPeer)
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<AllowedNetwork>> GetByVpnPeerIdAsync(Guid vpnPeerId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<AllowedNetwork>()
            .Where(n => n.VpnPeerId == vpnPeerId)
            .OrderBy(n => n.NetworkCidr)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AllowedNetwork>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default)
    {
        var peerId = await _context.Set<Router>()
            .AsNoTracking()
            .Where(r => r.Id == routerId)
            .Select(r => r.VpnPeerId)
            .FirstOrDefaultAsync(cancellationToken);
        if (!peerId.HasValue)
            return Array.Empty<AllowedNetwork>();

        return await _context.Set<AllowedNetwork>()
            .Include(n => n.VpnPeer)
            .Where(n => n.VpnPeerId == peerId.Value)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AllowedNetwork>> GetAllByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var peerIds = await _context.Set<Router>()
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.VpnPeerId != null)
            .Select(r => r.VpnPeerId!.Value)
            .ToListAsync(cancellationToken);

        if (peerIds.Count == 0)
            return Array.Empty<AllowedNetwork>();

        return await _context.Set<AllowedNetwork>()
            .Include(n => n.VpnPeer)
            .Where(n => peerIds.Contains(n.VpnPeerId))
            .OrderBy(n => n.VpnPeerId)
            .ThenBy(n => n.NetworkCidr)
            .ToListAsync(cancellationToken);
    }

    public async Task<AllowedNetwork?> GetByRouterIdAndCidrAsync(Guid routerId, string networkCidr, CancellationToken cancellationToken = default)
    {
        var peerId = await _context.Set<Router>()
            .AsNoTracking()
            .Where(r => r.Id == routerId)
            .Select(r => r.VpnPeerId)
            .FirstOrDefaultAsync(cancellationToken);
        if (!peerId.HasValue)
            return null;

        return await _context.Set<AllowedNetwork>()
            .FirstOrDefaultAsync(
                n => n.VpnPeerId == peerId.Value && n.NetworkCidr == networkCidr,
                cancellationToken);
    }

    public async Task<AllowedNetwork> CreateAsync(AllowedNetwork network, CancellationToken cancellationToken = default)
    {
        _context.Set<AllowedNetwork>().Add(network);
        await _context.SaveChangesAsync(cancellationToken);
        return network;
    }

    public async Task<AllowedNetwork> UpdateAsync(AllowedNetwork network, CancellationToken cancellationToken = default)
    {
        _context.Set<AllowedNetwork>().Update(network);
        await _context.SaveChangesAsync(cancellationToken);
        return network;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var network = await GetByIdAsync(id, cancellationToken);
        if (network != null)
        {
            _context.Set<AllowedNetwork>().Remove(network);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteByRouterIdAndCidrAsync(Guid routerId, string networkCidr, CancellationToken cancellationToken = default)
    {
        var network = await GetByRouterIdAndCidrAsync(routerId, networkCidr, cancellationToken);
        if (network != null)
        {
            _context.Set<AllowedNetwork>().Remove(network);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
