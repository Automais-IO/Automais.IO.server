using Automais.Core.Entities;

namespace Automais.Core.Interfaces;

public interface IDeviceRepository
{
    Task<Device?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Device?> GetByDevEuiAsync(Guid tenantId, string devEui, CancellationToken cancellationToken = default);

    /// <summary>Um device por DevEUI (hex já normalizado). Vários ou nenhum → null.</summary>
    Task<Device?> GetSingleByDevEuiAsync(string normalizedDevEui, CancellationToken cancellationToken = default);
    Task<IEnumerable<Device>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Device>> GetByApplicationIdAsync(Guid applicationId, CancellationToken cancellationToken = default);
    Task<Device> CreateAsync(Device device, CancellationToken cancellationToken = default);
    Task<Device> UpdateAsync(Device device, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> DevEuiExistsAsync(Guid tenantId, string devEui, CancellationToken cancellationToken = default);
    Task<int> CountByApplicationIdAsync(Guid applicationId, CancellationToken cancellationToken = default);
    Task<int> CountByNetworkIdAsync(Guid networkId, CancellationToken cancellationToken = default);
}


