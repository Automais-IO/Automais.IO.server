using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Automais.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Automais.Infrastructure.Repositories;

/// <summary>
/// Implementação do repositório de Router WireGuard Peers com EF Core
/// </summary>
public class RouterWireGuardPeerRepository : IRouterWireGuardPeerRepository
{
    private readonly ApplicationDbContext _context;

    public RouterWireGuardPeerRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<RouterWireGuardPeer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Set<RouterWireGuardPeer>()
            .Include(p => p.Router)
            .Include(p => p.VpnNetwork)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<RouterWireGuardPeer?> GetByRouterIdAndNetworkIdAsync(Guid routerId, Guid vpnNetworkId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<RouterWireGuardPeer>()
            .Include(p => p.Router)
            .Include(p => p.VpnNetwork)
            .FirstOrDefaultAsync(p => p.RouterId == routerId && p.VpnNetworkId == vpnNetworkId, cancellationToken);
    }

    public async Task<IEnumerable<RouterWireGuardPeer>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<RouterWireGuardPeer>()
            .Include(p => p.VpnNetwork)
            .Where(p => p.RouterId == routerId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<RouterWireGuardPeer>> GetByVpnNetworkIdAsync(Guid vpnNetworkId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<RouterWireGuardPeer>()
            .Include(p => p.Router)
            .Where(p => p.VpnNetworkId == vpnNetworkId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<RouterWireGuardPeer> CreateAsync(RouterWireGuardPeer peer, CancellationToken cancellationToken = default)
    {
        _context.Set<RouterWireGuardPeer>().Add(peer);
        await _context.SaveChangesAsync(cancellationToken);
        return peer;
    }

    public async Task<RouterWireGuardPeer> UpdateAsync(RouterWireGuardPeer peer, CancellationToken cancellationToken = default)
    {
        _context.Set<RouterWireGuardPeer>().Update(peer);
        await _context.SaveChangesAsync(cancellationToken);
        return peer;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var peer = await GetByIdAsync(id, cancellationToken);
        if (peer != null)
        {
            _context.Set<RouterWireGuardPeer>().Remove(peer);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IEnumerable<RouterWireGuardPeer>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Set<RouterWireGuardPeer>()
            .Include(p => p.Router)
            .Include(p => p.VpnNetwork)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<string>> GetAllocatedIpsByNetworkAsync(Guid vpnNetworkId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<RouterWireGuardPeer>()
            .Where(p => p.VpnNetworkId == vpnNetworkId && !string.IsNullOrEmpty(p.PeerIp))
            .Select(p => p.PeerIp)
            .ToListAsync(cancellationToken);
    }
}

