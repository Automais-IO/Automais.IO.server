using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Automais.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Automais.Infrastructure.Repositories;

public class HostRepository : IHostRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<HostRepository>? _logger;

    public HostRepository(ApplicationDbContext context, ILogger<HostRepository>? logger = null)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Host?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Set<Host>()
            .Include(h => h.Tenant)
            .Include(h => h.VpnNetwork)
            .FirstOrDefaultAsync(h => h.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Host>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<Host>()
            .AsNoTracking()
            .Include(h => h.VpnNetwork)
            .Where(h => h.TenantId == tenantId)
            .OrderBy(h => h.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Host>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Set<Host>()
            .Include(h => h.Tenant)
            .Include(h => h.VpnNetwork)
            .OrderBy(h => h.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Host> CreateAsync(Host host, CancellationToken cancellationToken = default)
    {
        try
        {
            _context.Set<Host>().Add(host);
            await _context.SaveChangesAsync(cancellationToken);
            _logger?.LogInformation("Host {HostId} criado", host.Id);
            return host;
        }
        catch (DbUpdateException ex)
        {
            _logger?.LogError(ex, "Erro ao criar host");
            throw;
        }
    }

    public async Task<Host> UpdateAsync(Host host, CancellationToken cancellationToken = default)
    {
        _context.Set<Host>().Update(host);
        await _context.SaveChangesAsync(cancellationToken);
        return host;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var host = await _context.Set<Host>().FindAsync(new object[] { id }, cancellationToken);
        if (host != null)
        {
            _context.Set<Host>().Remove(host);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IEnumerable<Host>> GetByVpnNetworkIdsAsync(IEnumerable<Guid> vpnNetworkIds, CancellationToken cancellationToken = default)
    {
        var idSet = vpnNetworkIds.Distinct().ToHashSet();
        if (idSet.Count == 0)
            return Array.Empty<Host>();

        return await _context.Set<Host>()
            .AsNoTracking()
            .Include(h => h.VpnNetwork)
            .Where(h => h.VpnNetworkId.HasValue && idSet.Contains(h.VpnNetworkId.Value))
            .OrderBy(h => h.Name)
            .ToListAsync(cancellationToken);
    }
}
