using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Automais.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Automais.Infrastructure.Repositories;

/// <summary>
/// Implementação EF Core para usuários de tenant.
/// </summary>
public class TenantUserRepository : ITenantUserRepository
{
    private readonly ApplicationDbContext _context;

    public TenantUserRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<TenantUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.TenantUsers
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<TenantUser?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await _context.TenantUsers
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
    }

    public async Task<IEnumerable<TenantUser>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await _context.TenantUsers
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<TenantUser> CreateAsync(TenantUser user, CancellationToken cancellationToken = default)
    {
        _context.TenantUsers.Add(user);
        await _context.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<TenantUser> UpdateAsync(TenantUser user, CancellationToken cancellationToken = default)
    {
        _context.TenantUsers.Update(user);
        await _context.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await _context.TenantUsers.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user == null)
        {
            return;
        }

        _context.TenantUsers.Remove(user);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> EmailExistsAsync(Guid tenantId, string email, CancellationToken cancellationToken = default)
    {
        return await _context.TenantUsers
            .AnyAsync(u => u.TenantId == tenantId && u.Email == email, cancellationToken);
    }
}


