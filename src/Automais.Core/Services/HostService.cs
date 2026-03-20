using Automais.Core.DTOs;
using Automais.Core.Entities;
using Automais.Core.Interfaces;

namespace Automais.Core.Services;

public class HostService : IHostService
{
    private readonly IHostRepository _hostRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IVpnIpAllocationService _ipAlloc;
    private readonly IVpnPeerRepository _peerRepo;
    private readonly IVpnNetworkRepository _vpnNetworkRepo;

    public HostService(
        IHostRepository hostRepository,
        ITenantRepository tenantRepository,
        IVpnIpAllocationService ipAlloc,
        IVpnPeerRepository peerRepo,
        IVpnNetworkRepository vpnNetworkRepo)
    {
        _hostRepository = hostRepository;
        _tenantRepository = tenantRepository;
        _ipAlloc = ipAlloc;
        _peerRepo = peerRepo;
        _vpnNetworkRepo = vpnNetworkRepo;
    }

    public async Task<IEnumerable<HostDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var hosts = await _hostRepository.GetAllAsync(cancellationToken);
        var list = new List<HostDto>();
        foreach (var h in hosts)
            list.Add(await MapToDtoAsync(h, cancellationToken));
        return list;
    }

    public async Task<IEnumerable<HostDto>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var hosts = await _hostRepository.GetByTenantIdAsync(tenantId, cancellationToken);
        var list = new List<HostDto>();
        foreach (var h in hosts)
            list.Add(await MapToDtoAsync(h, cancellationToken));
        return list;
    }

    public async Task<HostDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var host = await _hostRepository.GetByIdAsync(id, cancellationToken);
        return host == null ? null : await MapToDtoAsync(host, cancellationToken);
    }

    public async Task<HostDto> CreateAsync(Guid tenantId, CreateHostDto dto, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            throw new KeyNotFoundException($"Tenant com ID {tenantId} não encontrado.");
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new InvalidOperationException("Nome é obrigatório.");

        var vpnNetwork = await _vpnNetworkRepo.GetByIdAsync(dto.VpnNetworkId, cancellationToken)
            ?? throw new KeyNotFoundException($"Rede VPN {dto.VpnNetworkId} não encontrada.");

        var hostId = Guid.NewGuid();

        string allocatedIp;
        if (!string.IsNullOrWhiteSpace(dto.VpnIp))
            allocatedIp = await _ipAlloc.AllocateManualIpAsync(dto.VpnNetworkId, dto.VpnIp, VpnResourceKind.Host, hostId, dto.Name.Trim(), cancellationToken);
        else
            allocatedIp = await _ipAlloc.AllocateNextIpAsync(dto.VpnNetworkId, VpnResourceKind.Host, hostId, dto.Name.Trim(), cancellationToken);

        var ipOnly = allocatedIp.Split('/')[0];

        var (wgPub, wgPriv) = await WireGuardKeyGenerator.GenerateKeyPairAsync(cancellationToken);
        var (sshPriv, sshPub) = await SshKeyGenerator.GenerateEd25519KeyPairAsync(cancellationToken);

        var host = new Host
        {
            Id = hostId,
            TenantId = tenantId,
            Name = dto.Name.Trim(),
            HostKind = dto.HostKind,
            VpnNetworkId = dto.VpnNetworkId,
            VpnIp = ipOnly,
            SshPort = 22,
            SshUsername = "automais-io",
            SshPrivateKey = sshPriv,
            SshPublicKey = sshPub,
            ProvisioningStatus = HostProvisioningStatus.PendingInstall,
            Status = HostStatus.Offline,
            Description = dto.Description?.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await _hostRepository.CreateAsync(host, cancellationToken);

        var peer = new VpnPeer
        {
            Id = Guid.NewGuid(),
            VpnNetworkId = dto.VpnNetworkId,
            PublicKey = wgPub,
            PrivateKey = wgPriv,
            PeerIp = allocatedIp.Contains('/') ? allocatedIp : $"{allocatedIp}/32",
            Endpoint = vpnNetwork.ServerEndpoint,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _peerRepo.CreateAsync(peer, cancellationToken);

        created.VpnPeerId = peer.Id;
        await _hostRepository.UpdateAsync(created, cancellationToken);

        var loaded = await _hostRepository.GetByIdAsync(created.Id, cancellationToken);
        return await MapToDtoAsync(loaded!, cancellationToken);
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
        if (dto.VpnIp != null)
            host.VpnIp = dto.VpnIp.Split('/')[0].Trim();
        if (dto.ProvisioningStatus.HasValue)
            host.ProvisioningStatus = dto.ProvisioningStatus.Value;
        if (dto.Status.HasValue)
            host.Status = dto.Status.Value;
        if (dto.LastSeenAt.HasValue)
            host.LastSeenAt = dto.LastSeenAt;
        if (dto.Description != null)
            host.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        if (dto.MetricsJson != null)
            host.MetricsJson = dto.MetricsJson;
        if (dto.LastMetricsAt.HasValue)
            host.LastMetricsAt = dto.LastMetricsAt;

        host.UpdatedAt = DateTime.UtcNow;
        await _hostRepository.UpdateAsync(host, cancellationToken);
        var reloaded = await _hostRepository.GetByIdAsync(id, cancellationToken);
        return await MapToDtoAsync(reloaded!, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var host = await _hostRepository.GetByIdAsync(id, cancellationToken);
        if (host == null)
            throw new KeyNotFoundException($"Host com ID {id} não encontrado.");

        if (host.VpnPeerId.HasValue)
            await _peerRepo.DeleteAsync(host.VpnPeerId.Value, cancellationToken);

        await _hostRepository.DeleteAsync(id, cancellationToken);
    }

    private async Task<HostDto> MapToDtoAsync(Host h, CancellationToken ct)
    {
        VpnPeer? peer = null;
        if (h.VpnPeerId.HasValue)
            peer = await _peerRepo.GetByIdAsync(h.VpnPeerId.Value, ct);

        var vpnPeerId = h.VpnPeerId ?? peer?.Id;

        return new HostDto
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
            ProvisioningStatus = h.ProvisioningStatus,
            Status = h.Status,
            LastSeenAt = h.LastSeenAt,
            Description = h.Description,
            MetricsJson = h.MetricsJson,
            LastMetricsAt = h.LastMetricsAt,
            CreatedAt = h.CreatedAt,
            UpdatedAt = h.UpdatedAt,
            VpnPeerId = vpnPeerId,
            WireGuardPeerId = vpnPeerId,
            WireGuardPeerKeysConfigured = peer != null && !string.IsNullOrEmpty(peer.PublicKey) && !string.IsNullOrEmpty(peer.PrivateKey)
        };
    }
}
