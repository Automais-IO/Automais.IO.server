using System.Security.Cryptography;
using Automais.Core;
using Automais.Core.DTOs;
using Automais.Core.Entities;
using Automais.Core.Interfaces;
namespace Automais.Core.Services;

public class DeviceService : IDeviceService
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IApplicationRepository _applicationRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IVpnNetworkRepository _vpnNetworkRepository;

    public DeviceService(
        ITenantRepository tenantRepository,
        IApplicationRepository applicationRepository,
        IDeviceRepository deviceRepository,
        IVpnNetworkRepository vpnNetworkRepository)
    {
        _tenantRepository = tenantRepository;
        _applicationRepository = applicationRepository;
        _deviceRepository = deviceRepository;
        _vpnNetworkRepository = vpnNetworkRepository;
    }

    public async Task<IEnumerable<DeviceDto>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var devices = await _deviceRepository.GetByTenantIdAsync(tenantId, cancellationToken);
        return await MapManyAsync(devices, cancellationToken);
    }

    public async Task<IEnumerable<DeviceDto>> GetByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        var devices = await _deviceRepository.GetByApplicationIdAsync(applicationId, cancellationToken);
        return await MapManyAsync(devices, cancellationToken);
    }

    public async Task<DeviceDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var device = await _deviceRepository.GetByIdAsync(id, cancellationToken);
        return device == null ? null : await MapToDtoAsync(device, cancellationToken);
    }

    public async Task<DeviceDto> CreateAsync(Guid tenantId, CreateDeviceDto dto, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
        {
            throw new KeyNotFoundException($"Tenant com ID {tenantId} não encontrado.");
        }

        var application = await _applicationRepository.GetByIdAsync(dto.ApplicationId, cancellationToken);
        if (application == null || application.TenantId != tenantId)
        {
            throw new InvalidOperationException("Application inválida para este tenant.");
        }

        if (await _deviceRepository.DevEuiExistsAsync(tenantId, dto.DevEui, cancellationToken))
        {
            throw new InvalidOperationException($"Device com DevEUI '{dto.DevEui}' já existe.");
        }

        Guid? vpnNetworkId = null;
        if (dto.VpnNetworkId.HasValue)
        {
            var network = await ValidateNetworkAsync(tenantId, dto.VpnNetworkId.Value, cancellationToken);
            vpnNetworkId = network.Id;
        }

        var device = new Device
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ApplicationId = dto.ApplicationId,
            Name = dto.Name,
            DevEui = dto.DevEui.ToUpperInvariant(),
            Description = dto.Description,
            Kind = dto.Kind,
            Status = DeviceStatus.Provisioning,
            VpnNetworkId = vpnNetworkId,
            VpnEnabled = dto.VpnEnabled,
            VpnPublicKey = dto.VpnPublicKey,
            VpnIpAddress = dto.VpnIpAddress,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await _deviceRepository.CreateAsync(device, cancellationToken);
        return await MapToDtoAsync(created, cancellationToken);
    }

    public async Task<DeviceDto> UpdateAsync(Guid id, UpdateDeviceDto dto, CancellationToken cancellationToken = default)
    {
        var device = await _deviceRepository.GetByIdAsync(id, cancellationToken);
        if (device == null)
        {
            throw new KeyNotFoundException($"Device com ID {id} não encontrado.");
        }

        if (!string.IsNullOrWhiteSpace(dto.Name))
        {
            device.Name = dto.Name;
        }

        if (dto.Description != null)
        {
            device.Description = dto.Description;
        }

        if (dto.Status.HasValue)
        {
            device.Status = dto.Status.Value;
        }

        if (dto.BatteryLevel.HasValue)
        {
            device.BatteryLevel = dto.BatteryLevel;
        }

        if (dto.SignalStrength.HasValue)
        {
            device.SignalStrength = dto.SignalStrength;
        }

        if (dto.Location != null)
        {
            device.Location = dto.Location;
        }

        if (dto.LastSeenAt.HasValue)
        {
            device.LastSeenAt = dto.LastSeenAt;
        }

        if (dto.ApplicationId.HasValue && dto.ApplicationId.Value != device.ApplicationId)
        {
            var application = await _applicationRepository.GetByIdAsync(dto.ApplicationId.Value, cancellationToken);
            if (application == null || application.TenantId != device.TenantId)
            {
                throw new InvalidOperationException("Application inválida para este tenant.");
            }

            device.ApplicationId = dto.ApplicationId.Value;
        }

        if (dto.ClearVpnNetwork == true)
        {
            device.VpnNetworkId = null;
        }
        else if (dto.VpnNetworkId.HasValue)
        {
            var network = await ValidateNetworkAsync(device.TenantId, dto.VpnNetworkId.Value, cancellationToken);
            device.VpnNetworkId = network.Id;
        }

        if (dto.VpnEnabled.HasValue)
        {
            device.VpnEnabled = dto.VpnEnabled.Value;
        }

        if (dto.VpnPublicKey != null)
        {
            device.VpnPublicKey = dto.VpnPublicKey;
        }

        if (dto.VpnIpAddress != null)
        {
            device.VpnIpAddress = dto.VpnIpAddress;
        }

        if (dto.Kind.HasValue)
        {
            device.Kind = dto.Kind.Value;
        }

        if (dto.WebDeviceEnabled.HasValue)
        {
            device.WebDeviceEnabled = dto.WebDeviceEnabled.Value;
            if (!device.WebDeviceEnabled)
            {
                device.WebDeviceTokenHash = null;
            }
        }

        device.UpdatedAt = DateTime.UtcNow;

        var updated = await _deviceRepository.UpdateAsync(device, cancellationToken);
        return await MapToDtoAsync(updated, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var device = await _deviceRepository.GetByIdAsync(id, cancellationToken);
        if (device == null)
        {
            return;
        }

        await _deviceRepository.DeleteAsync(id, cancellationToken);
    }

    public async Task<ValidateWebDeviceAgentResponseDto?> ValidateWebDeviceAgentAsync(
        string devEui,
        string plainToken,
        CancellationToken cancellationToken = default)
    {
        if (!DevEuiNormalizer.TryNormalize(devEui, out var norm))
        {
            return null;
        }

        var device = await _deviceRepository.GetSingleByDevEuiAsync(norm, cancellationToken);
        if (device == null)
        {
            return null;
        }

        if (!device.WebDeviceEnabled || string.IsNullOrWhiteSpace(device.WebDeviceTokenHash))
        {
            return new ValidateWebDeviceAgentResponseDto
            {
                Valid = false,
                TenantId = device.TenantId,
                WebDeviceEnabled = device.WebDeviceEnabled
            };
        }

        if (string.IsNullOrWhiteSpace(plainToken))
        {
            return new ValidateWebDeviceAgentResponseDto
            {
                Valid = false,
                TenantId = device.TenantId,
                WebDeviceEnabled = true
            };
        }

        var ok = BCrypt.Net.BCrypt.Verify(plainToken, device.WebDeviceTokenHash);
        return new ValidateWebDeviceAgentResponseDto
        {
            Valid = ok,
            TenantId = device.TenantId,
            WebDeviceEnabled = true
        };
    }

    public async Task<WebDeviceTokenIssuedDto> EnableWebDeviceAsync(
        Guid deviceId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var device = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);
        if (device == null || device.TenantId != tenantId)
        {
            throw new KeyNotFoundException($"Device com ID {deviceId} não encontrado.");
        }

        var plain = GenerateWebDevicePlainToken();
        device.WebDeviceEnabled = true;
        device.WebDeviceTokenHash = BCrypt.Net.BCrypt.HashPassword(plain);
        device.UpdatedAt = DateTime.UtcNow;
        await _deviceRepository.UpdateAsync(device, cancellationToken);
        return new WebDeviceTokenIssuedDto { Token = plain };
    }

    public async Task<WebDeviceTokenIssuedDto> RegenerateWebDeviceTokenAsync(
        Guid deviceId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var device = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);
        if (device == null || device.TenantId != tenantId)
        {
            throw new KeyNotFoundException($"Device com ID {deviceId} não encontrado.");
        }

        if (!device.WebDeviceEnabled)
        {
            throw new InvalidOperationException("WebDevice não está habilitado para este device.");
        }

        var plain = GenerateWebDevicePlainToken();
        device.WebDeviceTokenHash = BCrypt.Net.BCrypt.HashPassword(plain);
        device.UpdatedAt = DateTime.UtcNow;
        await _deviceRepository.UpdateAsync(device, cancellationToken);
        return new WebDeviceTokenIssuedDto { Token = plain };
    }

    public async Task<DeviceDto> DisableWebDeviceAsync(
        Guid deviceId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var device = await _deviceRepository.GetByIdAsync(deviceId, cancellationToken);
        if (device == null || device.TenantId != tenantId)
        {
            throw new KeyNotFoundException($"Device com ID {deviceId} não encontrado.");
        }

        device.WebDeviceEnabled = false;
        device.WebDeviceTokenHash = null;
        device.UpdatedAt = DateTime.UtcNow;
        var updated = await _deviceRepository.UpdateAsync(device, cancellationToken);
        return await MapToDtoAsync(updated, cancellationToken);
    }

    private static string GenerateWebDevicePlainToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private async Task<IEnumerable<DeviceDto>> MapManyAsync(IEnumerable<Device> devices, CancellationToken cancellationToken)
    {
        var list = new List<DeviceDto>();
        foreach (var device in devices)
        {
            list.Add(await MapToDtoAsync(device, cancellationToken));
        }

        return list;
    }

    private async Task<DeviceDto> MapToDtoAsync(Device device, CancellationToken cancellationToken)
    {
        VpnNetwork? network = null;
        if (device.VpnNetworkId.HasValue)
        {
            network = await _vpnNetworkRepository.GetByIdAsync(device.VpnNetworkId.Value, cancellationToken);
        }

        return MapToDto(device, network);
    }

    private static DeviceDto MapToDto(Device device, VpnNetwork? network)
    {
        return new DeviceDto
        {
            Id = device.Id,
            TenantId = device.TenantId,
            ApplicationId = device.ApplicationId,
            Name = device.Name,
            DevEui = device.DevEui,
            Description = device.Description,
            Kind = device.Kind,
            Status = device.Status,
            BatteryLevel = device.BatteryLevel,
            SignalStrength = device.SignalStrength,
            Location = device.Location,
            LastSeenAt = device.LastSeenAt,
            VpnEnabled = device.VpnEnabled,
            VpnPublicKey = device.VpnPublicKey,
            VpnIpAddress = device.VpnIpAddress,
            VpnNetwork = network == null ? null : new VpnNetworkSummaryDto
            {
                NetworkId = network.Id,
                Name = network.Name,
                Cidr = network.Cidr
            },
            WebDeviceEnabled = device.WebDeviceEnabled,
            WebDeviceTokenConfigured = !string.IsNullOrEmpty(device.WebDeviceTokenHash),
            CreatedAt = device.CreatedAt,
            UpdatedAt = device.UpdatedAt
        };
    }

    private async Task<VpnNetwork> ValidateNetworkAsync(Guid tenantId, Guid networkId, CancellationToken cancellationToken)
    {
        var network = await _vpnNetworkRepository.GetByIdAsync(networkId, cancellationToken);
        if (network == null || network.TenantId != tenantId)
        {
            throw new InvalidOperationException("Rede VPN inválida para este tenant.");
        }

        return network;
    }
}


