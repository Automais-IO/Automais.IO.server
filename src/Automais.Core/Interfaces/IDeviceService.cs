using Automais.Core.DTOs;

namespace Automais.Core.Interfaces;

public interface IDeviceService
{
    Task<IEnumerable<DeviceDto>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IEnumerable<DeviceDto>> GetByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default);
    Task<DeviceDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DeviceDto> CreateAsync(Guid tenantId, CreateDeviceDto dto, CancellationToken cancellationToken = default);
    Task<DeviceDto> UpdateAsync(Guid id, UpdateDeviceDto dto, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Valida token do agente (serviço webdevice, chave interna).</summary>
    Task<ValidateWebDeviceAgentResponseDto?> ValidateWebDeviceAgentAsync(Guid deviceId, string plainToken, CancellationToken cancellationToken = default);

    /// <summary>Ativa WebDevice e emite token em claro (uma vez).</summary>
    Task<WebDeviceTokenIssuedDto> EnableWebDeviceAsync(Guid deviceId, Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Regenera token; revoga o anterior.</summary>
    Task<WebDeviceTokenIssuedDto> RegenerateWebDeviceTokenAsync(Guid deviceId, Guid tenantId, CancellationToken cancellationToken = default);

    Task<DeviceDto> DisableWebDeviceAsync(Guid deviceId, Guid tenantId, CancellationToken cancellationToken = default);
}


