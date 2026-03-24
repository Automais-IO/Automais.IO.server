using Automais.Core.Entities;
using Automais.Core.Interfaces;

namespace Automais.Infrastructure.Repositories;

public class InMemoryDeviceRepository : IDeviceRepository
{
    private readonly List<Device> _devices = new();
    private readonly object _lock = new();

    public Task<Device?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var device = _devices.FirstOrDefault(d => d.Id == id);
            return Task.FromResult(device);
        }
    }

    public Task<Device?> GetByDevEuiAsync(Guid tenantId, string devEui, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var device = _devices.FirstOrDefault(d =>
                d.TenantId == tenantId &&
                d.DevEui.Equals(devEui, StringComparison.OrdinalIgnoreCase));

            return Task.FromResult(device);
        }
    }

    public Task<Device?> GetSingleByDevEuiAsync(string normalizedDevEui, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var matches = _devices
                .Where(d => d.DevEui.Equals(normalizedDevEui, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult<Device?>(matches.Count == 1 ? matches[0] : null);
        }
    }

    public Task<IEnumerable<Device>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var devices = _devices
                .Where(d => d.TenantId == tenantId)
                .OrderBy(d => d.Name)
                .ToList();

            return Task.FromResult<IEnumerable<Device>>(devices);
        }
    }

    public Task<IEnumerable<Device>> GetByApplicationIdAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var devices = _devices
                .Where(d => d.ApplicationId == applicationId)
                .OrderBy(d => d.Name)
                .ToList();

            return Task.FromResult<IEnumerable<Device>>(devices);
        }
    }

    public Task<Device> CreateAsync(Device device, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _devices.Add(device);
            return Task.FromResult(device);
        }
    }

    public Task<Device> UpdateAsync(Device device, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var index = _devices.FindIndex(d => d.Id == device.Id);
            if (index >= 0)
            {
                _devices[index] = device;
            }

            return Task.FromResult(device);
        }
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var device = _devices.FirstOrDefault(d => d.Id == id);
            if (device != null)
            {
                _devices.Remove(device);
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> DevEuiExistsAsync(Guid tenantId, string devEui, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var exists = _devices.Any(d =>
                d.TenantId == tenantId &&
                d.DevEui.Equals(devEui, StringComparison.OrdinalIgnoreCase));

            return Task.FromResult(exists);
        }
    }

    public Task<int> CountByApplicationIdAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var count = _devices.Count(d => d.ApplicationId == applicationId);
            return Task.FromResult(count);
        }
    }

    public Task<int> CountByNetworkIdAsync(Guid networkId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var count = _devices.Count(d => d.VpnNetworkId == networkId);
            return Task.FromResult(count);
        }
    }
}


