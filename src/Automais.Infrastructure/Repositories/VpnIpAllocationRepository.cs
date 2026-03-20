using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Automais.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Automais.Infrastructure.Repositories;

public class VpnIpAllocationRepository : IVpnIpAllocationRepository
{
    private readonly ApplicationDbContext _context;

    public VpnIpAllocationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<VpnIpAllocation>> GetByNetworkIdAsync(Guid vpnNetworkId, bool onlyActive = true, CancellationToken cancellationToken = default)
    {
        var q = _context.Set<VpnIpAllocation>()
            .Where(a => a.VpnNetworkId == vpnNetworkId);
        if (onlyActive) q = q.Where(a => a.IsActive);
        return await q.OrderBy(a => a.Address).ToListAsync(cancellationToken);
    }

    public async Task<VpnIpAllocation?> GetByResourceAsync(Guid vpnNetworkId, VpnResourceKind kind, Guid resourceId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<VpnIpAllocation>()
            .FirstOrDefaultAsync(a =>
                a.VpnNetworkId == vpnNetworkId &&
                a.ResourceKind == kind &&
                a.ResourceId == resourceId &&
                a.IsActive,
                cancellationToken);
    }

    public async Task<VpnIpAllocation> CreateAsync(VpnIpAllocation allocation, CancellationToken cancellationToken = default)
    {
        _context.Set<VpnIpAllocation>().Add(allocation);
        await _context.SaveChangesAsync(cancellationToken);
        return allocation;
    }

    public async Task ReleaseAsync(Guid vpnNetworkId, VpnResourceKind kind, Guid resourceId, CancellationToken cancellationToken = default)
    {
        var existing = await _context.Set<VpnIpAllocation>()
            .Where(a => a.VpnNetworkId == vpnNetworkId && a.ResourceKind == kind && a.ResourceId == resourceId && a.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var a in existing)
        {
            a.IsActive = false;
            a.ReleasedAt = DateTime.UtcNow;
        }
        if (existing.Count > 0)
            await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> IsAddressTakenAsync(Guid vpnNetworkId, string address, CancellationToken cancellationToken = default)
    {
        return await _context.Set<VpnIpAllocation>()
            .AnyAsync(a => a.VpnNetworkId == vpnNetworkId && a.Address == address && a.IsActive, cancellationToken);
    }
}
