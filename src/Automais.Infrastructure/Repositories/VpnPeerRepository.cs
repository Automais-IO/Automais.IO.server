using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Automais.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Automais.Infrastructure.Repositories;

/// <summary>Repositório de <c>vpn_peers</c>.</summary>
public class VpnPeerRepository : IVpnPeerRepository
{
    private readonly ApplicationDbContext _context;

    public VpnPeerRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<VpnPeer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Set<VpnPeer>()
            .Include(p => p.VpnNetwork)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<VpnPeer?> GetByRouterIdAndNetworkIdAsync(Guid routerId, Guid vpnNetworkId, CancellationToken cancellationToken = default)
    {
        var router = await _context.Set<Router>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == routerId, cancellationToken);
        if (router?.VpnPeerId == null)
            return null;

        return await _context.Set<VpnPeer>()
            .Include(p => p.VpnNetwork)
            .FirstOrDefaultAsync(
                p => p.Id == router.VpnPeerId.Value && p.VpnNetworkId == vpnNetworkId,
                cancellationToken);
    }

    public async Task<IEnumerable<VpnPeer>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default)
    {
        var router = await _context.Set<Router>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == routerId, cancellationToken);
        if (router?.VpnPeerId == null)
            return Array.Empty<VpnPeer>();

        var peer = await _context.Set<VpnPeer>()
            .Include(p => p.VpnNetwork)
            .FirstOrDefaultAsync(p => p.Id == router.VpnPeerId.Value, cancellationToken);

        return peer == null ? Array.Empty<VpnPeer>() : new[] { peer };
    }

    public async Task<IEnumerable<VpnPeer>> GetByVpnNetworkIdAsync(Guid vpnNetworkId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<VpnPeer>()
            .Include(p => p.VpnNetwork)
            .Where(p => p.VpnNetworkId == vpnNetworkId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<VpnPeer> CreateAsync(VpnPeer peer, CancellationToken cancellationToken = default)
    {
        _context.Set<VpnPeer>().Add(peer);
        await _context.SaveChangesAsync(cancellationToken);
        return peer;
    }

    public async Task<VpnPeer> UpdateAsync(VpnPeer peer, CancellationToken cancellationToken = default)
    {
        _context.Set<VpnPeer>().Update(peer);
        await _context.SaveChangesAsync(cancellationToken);
        return peer;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var peer = await _context.Set<VpnPeer>()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (peer == null)
            return;

        await _context.Set<Router>()
            .Where(r => r.VpnPeerId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.VpnPeerId, (Guid?)null), cancellationToken);

        await _context.Set<Host>()
            .Where(h => h.VpnPeerId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.VpnPeerId, (Guid?)null), cancellationToken);

        _context.Set<VpnPeer>().Remove(peer);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<VpnPeer>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Set<VpnPeer>()
            .Include(p => p.VpnNetwork)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<string>> GetAllocatedIpsByNetworkAsync(Guid vpnNetworkId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<VpnPeer>()
            .Where(p => p.VpnNetworkId == vpnNetworkId && !string.IsNullOrEmpty(p.PeerIp))
            .Select(p => p.PeerIp)
            .ToListAsync(cancellationToken);
    }
}
