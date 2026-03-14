using Automais.Core.DTOs;

namespace Automais.Core.Interfaces;

public interface ITenantUserService
{
    Task<IEnumerable<TenantUserDto>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<TenantUserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TenantUserDto> CreateAsync(Guid tenantId, CreateTenantUserDto dto, CancellationToken cancellationToken = default);
    Task<TenantUserDto> UpdateAsync(Guid id, UpdateTenantUserDto dto, CancellationToken cancellationToken = default);
    Task<TenantUserDto> UpdateNetworksAsync(Guid id, UpdateUserNetworksDto dto, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(Guid id, CancellationToken cancellationToken = default);
    Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);
}


