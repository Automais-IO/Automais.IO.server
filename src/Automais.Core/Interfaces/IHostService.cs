using Automais.Core.DTOs;

namespace Automais.Core.Interfaces;

public interface IHostService
{
    Task<IEnumerable<HostDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<HostDto>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<HostDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<HostDto> CreateAsync(Guid tenantId, CreateHostDto dto, CancellationToken cancellationToken = default);
    Task<HostDto> UpdateAsync(Guid id, UpdateHostDto dto, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
