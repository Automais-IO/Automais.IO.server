using Automais.Core.Entities;

namespace Automais.Core.Interfaces;

public interface IUserAllowedRouteRepository
{
    Task<UserAllowedRoute?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<UserAllowedRoute>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<UserAllowedRoute>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default);
    Task<UserAllowedRoute?> GetByUserIdAndAllowedNetworkIdAsync(Guid userId, Guid allowedNetworkId, CancellationToken cancellationToken = default);
    Task<UserAllowedRoute> CreateAsync(UserAllowedRoute route, CancellationToken cancellationToken = default);
    Task<UserAllowedRoute> UpdateAsync(UserAllowedRoute route, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task ReplaceUserRoutesAsync(Guid userId, IEnumerable<Guid> allowedNetworkIds, CancellationToken cancellationToken = default);
}
