using Automais.Core.DTOs;

namespace Automais.Core.Interfaces;

/// <summary>
/// Interface para serviço de Routers
/// </summary>
public interface IRouterService
{
    Task<IEnumerable<RouterDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<RouterDto>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<RouterDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RouterDto> CreateAsync(Guid tenantId, CreateRouterDto dto, string routerOsApiUsername, string routerOsApiPassword, CancellationToken cancellationToken = default);
    Task<RouterDto> UpdateAsync(Guid id, UpdateRouterDto dto, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RouterDto> TestConnectionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RouterDto> UpdateSystemInfoAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>
    /// Atualiza a senha do router e marca PasswordChanged como true.
    /// Usado quando a senha é alterada automaticamente na primeira conexão.
    /// </summary>
    Task UpdatePasswordAsync(Guid id, string newPassword, CancellationToken cancellationToken = default);
}

