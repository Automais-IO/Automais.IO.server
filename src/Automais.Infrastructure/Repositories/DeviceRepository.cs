using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Automais.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Automais.Infrastructure.Repositories;

/// <summary>
/// Implementação EF Core para Devices.
/// </summary>
public class DeviceRepository : IDeviceRepository
{
    private readonly ApplicationDbContext _context;

    public DeviceRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Device?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Devices
            .Include(d => d.VpnNetwork)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<Device?> GetByDevEuiAsync(Guid tenantId, string devEui, CancellationToken cancellationToken = default)
    {
        var normalized = devEui.ToUpperInvariant();
        return await _context.Devices
            .FirstOrDefaultAsync(d => d.TenantId == tenantId && d.DevEui == normalized, cancellationToken);
    }

    public async Task<Device?> GetSingleByDevEuiAsync(string normalizedDevEui, CancellationToken cancellationToken = default)
    {
        var list = await _context.Devices
            .Where(d => d.DevEui == normalizedDevEui)
            .Take(2)
            .ToListAsync(cancellationToken);
        return list.Count == 1 ? list[0] : null;
    }

    public async Task<IEnumerable<Device>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _context.Devices
            .Where(d => d.TenantId == tenantId)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Device>> GetByApplicationIdAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        return await _context.Devices
            .Where(d => d.ApplicationId == applicationId)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Device> CreateAsync(Device device, CancellationToken cancellationToken = default)
    {
        _context.Devices.Add(device);
        await _context.SaveChangesAsync(cancellationToken);
        return device;
    }

    public async Task<Device> UpdateAsync(Device device, CancellationToken cancellationToken = default)
    {
        _context.Devices.Update(device);
        await _context.SaveChangesAsync(cancellationToken);
        return device;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var device = await _context.Devices.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (device == null)
        {
            return;
        }

        _context.Devices.Remove(device);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DevEuiExistsAsync(Guid tenantId, string devEui, CancellationToken cancellationToken = default)
    {
        var normalized = devEui.ToUpperInvariant();
        return await _context.Devices
            .AnyAsync(d => d.TenantId == tenantId && d.DevEui == normalized, cancellationToken);
    }

    public async Task<int> CountByApplicationIdAsync(Guid applicationId, CancellationToken cancellationToken = default)
    {
        return await _context.Devices
            .CountAsync(d => d.ApplicationId == applicationId, cancellationToken);
    }

    public async Task<int> CountByNetworkIdAsync(Guid networkId, CancellationToken cancellationToken = default)
    {
        return await _context.Devices
            .CountAsync(d => d.VpnNetworkId == networkId, cancellationToken);
    }
}


