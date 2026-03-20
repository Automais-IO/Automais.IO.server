using Automais.Core.Entities;

namespace Automais.Core.Interfaces;

public interface IHostRepository
{
    Task<Host?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Host>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Host>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Host> CreateAsync(Host host, CancellationToken cancellationToken = default);
    Task<Host> UpdateAsync(Host host, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
