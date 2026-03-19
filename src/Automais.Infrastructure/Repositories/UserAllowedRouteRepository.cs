using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Automais.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Automais.Infrastructure.Repositories;

public class UserAllowedRouteRepository : IUserAllowedRouteRepository
{
    private readonly ApplicationDbContext _context;

    public UserAllowedRouteRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<UserAllowedRoute?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Set<UserAllowedRoute>()
            .Include(r => r.Router)
            .Include(r => r.RouterAllowedNetwork)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<UserAllowedRoute>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<UserAllowedRoute>()
            .Include(r => r.Router)
            .Include(r => r.RouterAllowedNetwork)
            .Where(r => r.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<UserAllowedRoute>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<UserAllowedRoute>()
            .Include(r => r.Router)
            .Include(r => r.RouterAllowedNetwork)
            .Where(r => r.RouterId == routerId)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserAllowedRoute?> GetByUserIdAndRouterAllowedNetworkIdAsync(
        Guid userId, 
        Guid routerAllowedNetworkId, 
        CancellationToken cancellationToken = default)
    {
        return await _context.Set<UserAllowedRoute>()
            .FirstOrDefaultAsync(r => r.UserId == userId && r.RouterAllowedNetworkId == routerAllowedNetworkId, cancellationToken);
    }

    public async Task<UserAllowedRoute> CreateAsync(UserAllowedRoute route, CancellationToken cancellationToken = default)
    {
        _context.Set<UserAllowedRoute>().Add(route);
        await _context.SaveChangesAsync(cancellationToken);
        return route;
    }

    public async Task<UserAllowedRoute> UpdateAsync(UserAllowedRoute route, CancellationToken cancellationToken = default)
    {
        _context.Set<UserAllowedRoute>().Update(route);
        await _context.SaveChangesAsync(cancellationToken);
        return route;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var route = await _context.Set<UserAllowedRoute>().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (route != null)
        {
            _context.Set<UserAllowedRoute>().Remove(route);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var routes = await _context.Set<UserAllowedRoute>()
            .Where(r => r.UserId == userId)
            .ToListAsync(cancellationToken);
        
        if (routes.Any())
        {
            _context.Set<UserAllowedRoute>().RemoveRange(routes);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ReplaceUserRoutesAsync(Guid userId, IEnumerable<Guid> routerAllowedNetworkIds, CancellationToken cancellationToken = default)
    {
        var networkIds = routerAllowedNetworkIds.Distinct().ToList();
        
        // Remover rotas existentes
        await DeleteByUserIdAsync(userId, cancellationToken);

        if (!networkIds.Any())
        {
            return; // Nenhuma rota para adicionar
        }

        // Buscar informações das redes destino
        var allowedNetworks = await _context.Set<RouterAllowedNetwork>()
            .Include(n => n.Router)
            .Where(n => networkIds.Contains(n.Id))
            .ToListAsync(cancellationToken);

        // Validar que todas as redes foram encontradas
        var missingIds = networkIds.Except(allowedNetworks.Select(n => n.Id)).ToList();
        if (missingIds.Any())
        {
            throw new KeyNotFoundException($"Redes destino não encontradas: {string.Join(", ", missingIds)}");
        }

        // Criar novas rotas
        var newRoutes = allowedNetworks.Select(network => new UserAllowedRoute
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            RouterId = network.RouterId,
            RouterAllowedNetworkId = network.Id,
            NetworkCidr = network.NetworkCidr,
            Description = network.Description,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }).ToList();

        if (newRoutes.Any())
        {
            _context.Set<UserAllowedRoute>().AddRange(newRoutes);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}

