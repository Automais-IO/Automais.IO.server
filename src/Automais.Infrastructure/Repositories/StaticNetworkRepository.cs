using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Automais.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Automais.Infrastructure.Repositories;

public class StaticNetworkRepository : IStaticNetworkRepository
{
    private readonly ApplicationDbContext _context;

    public StaticNetworkRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<StaticNetwork?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Set<StaticNetwork>()
            .Include(r => r.VpnPeer)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<StaticNetwork>> GetByVpnPeerIdAsync(Guid vpnPeerId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<StaticNetwork>()
            .Where(r => r.VpnPeerId == vpnPeerId)
            .OrderBy(r => r.Destination)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<StaticNetwork>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default)
    {
        var peerId = await _context.Set<Router>()
            .AsNoTracking()
            .Where(r => r.Id == routerId)
            .Select(r => r.VpnPeerId)
            .FirstOrDefaultAsync(cancellationToken);
        if (!peerId.HasValue)
            return Array.Empty<StaticNetwork>();

        return await _context.Set<StaticNetwork>()
            .Include(r => r.VpnPeer)
            .Where(r => r.VpnPeerId == peerId.Value)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<StaticNetwork?> GetByRouterIdAndDestinationAsync(Guid routerId, string destination, CancellationToken cancellationToken = default)
    {
        var peerId = await _context.Set<Router>()
            .AsNoTracking()
            .Where(r => r.Id == routerId)
            .Select(r => r.VpnPeerId)
            .FirstOrDefaultAsync(cancellationToken);
        if (!peerId.HasValue)
            return null;

        return await _context.Set<StaticNetwork>()
            .FirstOrDefaultAsync(
                r => r.VpnPeerId == peerId.Value && r.Destination == destination,
                cancellationToken);
    }

    public async Task<StaticNetwork> CreateAsync(StaticNetwork route, CancellationToken cancellationToken = default)
    {
        _context.Set<StaticNetwork>().Add(route);
        await _context.SaveChangesAsync(cancellationToken);
        return route;
    }

    public async Task<StaticNetwork> UpdateAsync(StaticNetwork route, CancellationToken cancellationToken = default)
    {
        _context.Set<StaticNetwork>().Update(route);
        await _context.SaveChangesAsync(cancellationToken);
        return route;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var route = await GetByIdAsync(id, cancellationToken);
        if (route != null)
        {
            _context.Set<StaticNetwork>().Remove(route);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
