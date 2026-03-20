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
            .Include(r => r.AllowedNetwork)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<UserAllowedRoute>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<UserAllowedRoute>()
            .Include(r => r.Router)
            .Include(r => r.AllowedNetwork)
            .Where(r => r.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<UserAllowedRoute>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<UserAllowedRoute>()
            .Include(r => r.Router)
            .Include(r => r.AllowedNetwork)
            .Where(r => r.RouterId == routerId)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserAllowedRoute?> GetByUserIdAndAllowedNetworkIdAsync(
        Guid userId,
        Guid allowedNetworkId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Set<UserAllowedRoute>()
            .FirstOrDefaultAsync(
                r => r.UserId == userId && r.AllowedNetworkId == allowedNetworkId,
                cancellationToken);
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

    public async Task ReplaceUserRoutesAsync(Guid userId, IEnumerable<Guid> allowedNetworkIds, CancellationToken cancellationToken = default)
    {
        var networkIds = allowedNetworkIds.Distinct().ToList();

        await DeleteByUserIdAsync(userId, cancellationToken);

        if (!networkIds.Any())
            return;

        var allowedNetworks = await _context.Set<AllowedNetwork>()
            .Where(n => networkIds.Contains(n.Id))
            .ToListAsync(cancellationToken);

        var missingIds = networkIds.Except(allowedNetworks.Select(n => n.Id)).ToList();
        if (missingIds.Any())
            throw new KeyNotFoundException($"Redes permitidas não encontradas: {string.Join(", ", missingIds)}");

        var routers = await _context.Set<Router>()
            .Where(r => r.VpnPeerId != null && allowedNetworks.Select(a => a.VpnPeerId).Contains(r.VpnPeerId!.Value))
            .ToListAsync(cancellationToken);
        var routerByPeer = routers.Where(r => r.VpnPeerId != null).ToDictionary(r => r.VpnPeerId!.Value);

        var newRoutes = new List<UserAllowedRoute>();
        foreach (var network in allowedNetworks)
        {
            if (!routerByPeer.TryGetValue(network.VpnPeerId, out var router))
                throw new InvalidOperationException(
                    $"Não há router associado ao peer {network.VpnPeerId} para a rede permitida {network.Id}.");

            newRoutes.Add(new UserAllowedRoute
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                RouterId = router.Id,
                AllowedNetworkId = network.Id,
                NetworkCidr = network.NetworkCidr,
                Description = network.Description,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (newRoutes.Any())
        {
            _context.Set<UserAllowedRoute>().AddRange(newRoutes);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
