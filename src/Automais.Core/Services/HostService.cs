using Automais.Core.DTOs;
using Automais.Core.Entities;
using Automais.Core.Interfaces;

namespace Automais.Core.Services;

public class HostService : IHostService
{
    private readonly IHostRepository _hostRepository;
    private readonly ITenantRepository _tenantRepository;

    public HostService(IHostRepository hostRepository, ITenantRepository tenantRepository)
    {
        _hostRepository = hostRepository;
        _tenantRepository = tenantRepository;
    }

    public async Task<IEnumerable<HostDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var hosts = await _hostRepository.GetAllAsync(cancellationToken);
        var list = new List<HostDto>();
        foreach (var h in hosts)
            list.Add(MapToDto(h));
        return list;
    }

    public async Task<IEnumerable<HostDto>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var hosts = await _hostRepository.GetByTenantIdAsync(tenantId, cancellationToken);
        return hosts.Select(MapToDto);
    }

    public async Task<HostDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var host = await _hostRepository.GetByIdAsync(id, cancellationToken);
        return host == null ? null : MapToDto(host);
    }

    public async Task<HostDto> CreateAsync(Guid tenantId, CreateHostDto dto, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            throw new KeyNotFoundException($"Tenant com ID {tenantId} não encontrado.");

        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("Nome é obrigatório.");
        if (string.IsNullOrWhiteSpace(dto.VpnIp))
            throw new InvalidOperationException("IP VPN (VpnIp) é obrigatório.");
        if (string.IsNullOrWhiteSpace(dto.SshUsername))
            throw new InvalidOperationException("Usuário SSH é obrigatório.");

        var port = dto.SshPort > 0 && dto.SshPort <= 65535 ? dto.SshPort : 22;

        var host = new Host
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = dto.Name.Trim(),
            HostKind = dto.HostKind,
            VpnNetworkId = dto.VpnNetworkId,
            VpnIp = dto.VpnIp.Trim(),
            SshPort = port,
            SshUsername = dto.SshUsername.Trim(),
            SshPassword = dto.SshPassword,
            Description = dto.Description?.Trim(),
            Status = HostStatus.Offline,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await _hostRepository.CreateAsync(host, cancellationToken);
        var loaded = await _hostRepository.GetByIdAsync(created.Id, cancellationToken);
        return MapToDto(loaded!);
    }

    public async Task<HostDto> UpdateAsync(Guid id, UpdateHostDto dto, CancellationToken cancellationToken = default)
    {
        var host = await _hostRepository.GetByIdAsync(id, cancellationToken);
        if (host == null)
            throw new KeyNotFoundException($"Host com ID {id} não encontrado.");

        if (!string.IsNullOrWhiteSpace(dto.Name))
            host.Name = dto.Name.Trim();
        if (dto.HostKind.HasValue)
            host.HostKind = dto.HostKind.Value;
        if (dto.VpnNetworkId.HasValue)
            host.VpnNetworkId = dto.VpnNetworkId.Value;
        if (dto.VpnIp != null)
            host.VpnIp = dto.VpnIp.Trim();
        if (dto.SshPort.HasValue && dto.SshPort.Value > 0 && dto.SshPort.Value <= 65535)
            host.SshPort = dto.SshPort.Value;
        if (dto.SshUsername != null)
            host.SshUsername = dto.SshUsername.Trim();
        if (dto.SshPassword != null)
            host.SshPassword = dto.SshPassword;
        if (dto.Status.HasValue)
            host.Status = dto.Status.Value;
        if (dto.LastSeenAt.HasValue)
            host.LastSeenAt = dto.LastSeenAt;
        if (dto.Description != null)
            host.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();

        host.UpdatedAt = DateTime.UtcNow;
        await _hostRepository.UpdateAsync(host, cancellationToken);
        var reloaded = await _hostRepository.GetByIdAsync(id, cancellationToken);
        return MapToDto(reloaded!);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var host = await _hostRepository.GetByIdAsync(id, cancellationToken);
        if (host == null)
            throw new KeyNotFoundException($"Host com ID {id} não encontrado.");
        await _hostRepository.DeleteAsync(id, cancellationToken);
    }

    private static HostDto MapToDto(Host h) => new()
    {
        Id = h.Id,
        TenantId = h.TenantId,
        Name = h.Name,
        HostKind = h.HostKind,
        VpnNetworkId = h.VpnNetworkId,
        VpnNetworkServerEndpoint = h.VpnNetwork?.ServerEndpoint,
        VpnIp = h.VpnIp,
        SshPort = h.SshPort,
        SshUsername = h.SshUsername,
        SshPassword = h.SshPassword,
        Status = h.Status,
        LastSeenAt = h.LastSeenAt,
        Description = h.Description,
        CreatedAt = h.CreatedAt,
        UpdatedAt = h.UpdatedAt
    };
}
