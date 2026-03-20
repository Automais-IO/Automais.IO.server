using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Automais.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Automais.Infrastructure.Repositories;

public class RemoteNetworkRepository : IRemoteNetworkRepository
{
    private readonly ApplicationDbContext _context;

    public RemoteNetworkRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<RemoteNetwork?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Set<RemoteNetwork>()
            .Include(n => n.VpnPeer)
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<RemoteNetwork>> GetByVpnPeerIdAsync(Guid vpnPeerId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<RemoteNetwork>()
            .Where(n => n.VpnPeerId == vpnPeerId)
            .OrderBy(n => n.NetworkCidr)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<RemoteNetwork>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default)
    {
        var peerId = await _context.Set<Router>()
            .AsNoTracking()
            .Where(r => r.Id == routerId)
            .Select(r => r.VpnPeerId)
            .FirstOrDefaultAsync(cancellationToken);
        if (!peerId.HasValue)
            return Array.Empty<RemoteNetwork>();

        return await _context.Set<RemoteNetwork>()
            .Where(n => n.VpnPeerId == peerId.Value)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<RemoteNetwork?> GetByRouterIdAndCidrAsync(Guid routerId, string networkCidr, CancellationToken cancellationToken = default)
    {
        var peerId = await _context.Set<Router>()
            .AsNoTracking()
            .Where(r => r.Id == routerId)
            .Select(r => r.VpnPeerId)
            .FirstOrDefaultAsync(cancellationToken);
        if (!peerId.HasValue)
            return null;

        return await _context.Set<RemoteNetwork>()
            .FirstOrDefaultAsync(
                n => n.VpnPeerId == peerId.Value && n.NetworkCidr == networkCidr,
                cancellationToken);
    }

    public async Task<RemoteNetwork> CreateAsync(RemoteNetwork network, CancellationToken cancellationToken = default)
    {
        _context.Set<RemoteNetwork>().Add(network);
        await _context.SaveChangesAsync(cancellationToken);
        return network;
    }

    public async Task<RemoteNetwork> UpdateAsync(RemoteNetwork network, CancellationToken cancellationToken = default)
    {
        _context.Set<RemoteNetwork>().Update(network);
        await _context.SaveChangesAsync(cancellationToken);
        return network;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var n = await GetByIdAsync(id, cancellationToken);
        if (n != null)
        {
            _context.Set<RemoteNetwork>().Remove(n);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
