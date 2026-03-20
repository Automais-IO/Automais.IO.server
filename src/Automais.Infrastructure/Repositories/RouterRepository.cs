using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Automais.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Automais.Infrastructure.Repositories;

/// <summary>
/// Implementação do repositório de Routers com EF Core
/// </summary>
public class RouterRepository : IRouterRepository
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<RouterRepository>? _logger;

    public RouterRepository(ApplicationDbContext context, ILogger<RouterRepository>? logger = null)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Router?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Set<Router>()
            .Include(r => r.Tenant)
            .Include(r => r.VpnNetwork)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<Router?> GetByVpnPeerIdAsync(Guid vpnPeerId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<Router>()
            .Include(r => r.Tenant)
            .Include(r => r.VpnNetwork)
            .FirstOrDefaultAsync(r => r.VpnPeerId == vpnPeerId, cancellationToken);
    }

    public async Task<Router?> GetBySerialNumberAsync(string serialNumber, CancellationToken cancellationToken = default)
    {
        return await _context.Set<Router>()
            .Include(r => r.Tenant)
            .Include(r => r.VpnNetwork)
            .FirstOrDefaultAsync(r => r.SerialNumber == serialNumber, cancellationToken);
    }

    public async Task<IEnumerable<Router>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<Router>()
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Router>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Set<Router>()
            .Include(r => r.Tenant)
            .Include(r => r.VpnNetwork)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Router> CreateAsync(Router router, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogDebug("Tentando criar router {RouterId} com nome {Name}, TenantId: {TenantId}, VpnNetworkId: {VpnNetworkId}", 
                router.Id, router.Name, router.TenantId, router.VpnNetworkId);
            
            _context.Set<Router>().Add(router);
            await _context.SaveChangesAsync(cancellationToken);
            
            _logger?.LogInformation("Router {RouterId} criado com sucesso", router.Id);
            return router;
        }
        catch (DbUpdateException ex)
        {
            var errorDetails = ex.InnerException?.Message ?? ex.Message;
            _logger?.LogError(ex, "Erro do Entity Framework ao criar router. Erro: {ErrorDetails}. StackTrace: {StackTrace}", 
                errorDetails, ex.InnerException?.StackTrace ?? ex.StackTrace);
            
            // Verificar se é erro de foreign key (VpnNetworkId não existe)
            if (errorDetails.Contains("foreign key") || errorDetails.Contains("violates foreign key constraint"))
            {
                throw new InvalidOperationException($"Rede VPN com ID {router.VpnNetworkId} não encontrada ou inválida. Verifique se o VpnNetworkId existe no banco de dados.");
            }
            
            // Re-throw para que o controller possa tratar
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Erro inesperado ao criar router. Erro: {ErrorMessage}", ex.Message);
            throw;
        }
    }

    public async Task<Router> UpdateAsync(Router router, CancellationToken cancellationToken = default)
    {
        _context.Set<Router>().Update(router);
        await _context.SaveChangesAsync(cancellationToken);
        return router;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var router = await GetByIdAsync(id, cancellationToken);
        if (router != null)
        {
            _context.Set<Router>().Remove(router);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> SerialNumberExistsAsync(string serialNumber, CancellationToken cancellationToken = default)
    {
        return await _context.Set<Router>()
            .AnyAsync(r => r.SerialNumber == serialNumber, cancellationToken);
    }
}

