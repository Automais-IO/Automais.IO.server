using Automais.Core.DTOs;

namespace Automais.Core.Interfaces;

public interface IVpnNetworkService
{
    Task<IEnumerable<VpnNetworkDto>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<VpnNetworkDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<VpnNetworkDto> CreateAsync(Guid tenantId, CreateVpnNetworkDto dto, CancellationToken cancellationToken = default);
    Task<VpnNetworkDto> UpdateAsync(Guid id, UpdateVpnNetworkDto dto, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<TenantUserDto>> GetUsersAsync(Guid networkId, CancellationToken cancellationToken = default);
    Task<VpnNetworkDto> RegenerateServerKeysAsync(Guid networkId, CancellationToken cancellationToken = default);
}


