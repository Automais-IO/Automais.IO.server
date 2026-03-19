using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Automais.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Automais.Infrastructure.Repositories;

/// <summary>
/// Implementação do repositório de Router Allowed Networks com EF Core
/// </summary>
public class RouterAllowedNetworkRepository : IRouterAllowedNetworkRepository
{
    private readonly ApplicationDbContext _context;

    public RouterAllowedNetworkRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<RouterAllowedNetwork?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Set<RouterAllowedNetwork>()
            .Include(n => n.Router)
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<RouterAllowedNetwork>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<RouterAllowedNetwork>()
            .Include(n => n.Router)
            .Where(n => n.RouterId == routerId)
            .OrderBy(n => n.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<RouterAllowedNetwork>> GetAllByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<RouterAllowedNetwork>()
            .Include(n => n.Router)
            .Where(n => n.Router.TenantId == tenantId)
            .OrderBy(n => n.Router.Name)
            .ThenBy(n => n.NetworkCidr)
            .ToListAsync(cancellationToken);
    }

    public async Task<RouterAllowedNetwork?> GetByRouterIdAndCidrAsync(Guid routerId, string networkCidr, CancellationToken cancellationToken = default)
    {
        return await _context.Set<RouterAllowedNetwork>()
            .FirstOrDefaultAsync(n => n.RouterId == routerId && n.NetworkCidr == networkCidr, cancellationToken);
    }

    public async Task<RouterAllowedNetwork> CreateAsync(RouterAllowedNetwork network, CancellationToken cancellationToken = default)
    {
        _context.Set<RouterAllowedNetwork>().Add(network);
        await _context.SaveChangesAsync(cancellationToken);
        return network;
    }

    public async Task<RouterAllowedNetwork> UpdateAsync(RouterAllowedNetwork network, CancellationToken cancellationToken = default)
    {
        _context.Set<RouterAllowedNetwork>().Update(network);
        await _context.SaveChangesAsync(cancellationToken);
        return network;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var network = await GetByIdAsync(id, cancellationToken);
        if (network != null)
        {
            _context.Set<RouterAllowedNetwork>().Remove(network);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteByRouterIdAndCidrAsync(Guid routerId, string networkCidr, CancellationToken cancellationToken = default)
    {
        var network = await GetByRouterIdAndCidrAsync(routerId, networkCidr, cancellationToken);
        if (network != null)
        {
            _context.Set<RouterAllowedNetwork>().Remove(network);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}

