using System.Security.Cryptography;
using System.Text;
using Automais.Core.DTOs;
using Automais.Core.Entities;
using Automais.Core.Interfaces;

namespace Automais.Core.Services;

public class HostService : IHostService
{
    private const int SetupExpirationMinutes = 10;
    private const int PasswordLength = 20;
    private const string PasswordChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%&*-_=+";

    private readonly IHostRepository _hostRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IVpnIpAllocationService _ipAlloc;
    private readonly IVpnPeerRepository _peerRepo;
    private readonly IVpnNetworkRepository _vpnNetworkRepo;
    private readonly IAllowedNetworkRepository _allowedNetworkRepository;
    private readonly IStaticNetworkRepository _staticRouteRepository;

    public HostService(
        IHostRepository hostRepository,
        ITenantRepository tenantRepository,
        IVpnIpAllocationService ipAlloc,
        IVpnPeerRepository peerRepo,
        IVpnNetworkRepository vpnNetworkRepo,
        IAllowedNetworkRepository allowedNetworkRepository,
        IStaticNetworkRepository staticRouteRepository)
    {
        _hostRepository = hostRepository;
        _tenantRepository = tenantRepository;
        _ipAlloc = ipAlloc;
        _peerRepo = peerRepo;
        _vpnNetworkRepo = vpnNetworkRepo;
        _allowedNetworkRepository = allowedNetworkRepository;
        _staticRouteRepository = staticRouteRepository;
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

    public async Task<InternalHostDto?> GetByIdInternalAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var host = await _hostRepository.GetByIdAsync(id, cancellationToken);
        return host == null ? null : await MapToInternalDtoAsync(host, cancellationToken);
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

        var (wgPub, wgPriv) = await VpnTunnelKeyGenerator.GenerateKeyPairAsync(cancellationToken);
        var (sshPriv, sshPub) = await SshKeyGenerator.GenerateEd25519KeyPairAsync(cancellationToken);

        var plainPassword = GenerateRandomPassword();
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(plainPassword);

        var host = new Host
        {
            Id = hostId,
            TenantId = tenantId,
            Name = dto.Name.Trim(),
            HostKind = dto.HostKind,
            VpnNetworkId = dto.VpnNetworkId,
            VpnIp = ipOnly,
            SshPort = dto.SshPort > 0 ? dto.SshPort : 22,
            SshUsername = "automais-io",
            SshPrivateKey = sshPriv,
            SshPublicKey = sshPub,
            SshPassword = plainPassword,
            SshPasswordHash = passwordHash,
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

    public async Task<string> ActivateSetupAsync(Guid hostId, string baseUrl, CancellationToken cancellationToken = default)
    {
        var host = await _hostRepository.GetByIdAsync(hostId, cancellationToken)
            ?? throw new KeyNotFoundException($"Host com ID {hostId} não encontrado.");

        host.SetupRequestedAt = DateTime.UtcNow;
        host.ProvisioningStatus = HostProvisioningStatus.PendingInstall;
        host.UpdatedAt = DateTime.UtcNow;
        await _hostRepository.UpdateAsync(host, cancellationToken);

        return $"{baseUrl.TrimEnd('/')}/api/hosts/{hostId}/setup-script";
    }

    public async Task<string> GenerateSetupScriptAsync(Guid hostId, CancellationToken cancellationToken = default)
    {
        var host = await _hostRepository.GetByIdAsync(hostId, cancellationToken)
            ?? throw new KeyNotFoundException($"Host com ID {hostId} não encontrado.");

        if (!host.SetupRequestedAt.HasValue)
            throw new InvalidOperationException("Setup não foi solicitado. Clique em \"Conectar-se\" primeiro.");

        var elapsed = DateTime.UtcNow - host.SetupRequestedAt.Value;
        if (elapsed.TotalMinutes > SetupExpirationMinutes)
            throw new InvalidOperationException(
                $"Setup expirado ({SetupExpirationMinutes} min). Clique em \"Conectar-se\" novamente.");

        if (!host.VpnPeerId.HasValue)
            throw new InvalidOperationException("Host não possui peer VPN configurado.");

        var peer = await _peerRepo.GetByIdAsync(host.VpnPeerId.Value, cancellationToken)
            ?? throw new InvalidOperationException("Peer VPN não encontrado.");

        var vpnNetwork = host.VpnNetworkId.HasValue
            ? await _vpnNetworkRepo.GetByIdAsync(host.VpnNetworkId.Value, cancellationToken)
            : null;
        if (vpnNetwork == null)
            throw new InvalidOperationException("Rede VPN não encontrada.");

        host.ProvisioningStatus = HostProvisioningStatus.Installing;
        host.UpdatedAt = DateTime.UtcNow;
        await _hostRepository.UpdateAsync(host, cancellationToken);

        return await BuildSetupScriptAsync(host, peer, vpnNetwork, cancellationToken);
    }

    private async Task<string> BuildSetupScriptAsync(Host host, VpnPeer peer, VpnNetwork vpnNetwork, CancellationToken cancellationToken)
    {
        var addrPart = peer.PeerIp.Split(',')[0].Trim();
        var address = addrPart.Contains('/') ? addrPart : $"{addrPart}/32";
        var serverEndpoint = vpnNetwork.ServerEndpoint ?? "automais.io";
        var serverPublicKey = (vpnNetwork.ServerPublicKey ?? "").Trim();
        var listenPort = vpnNetwork.ListenPort > 0 ? vpnNetwork.ListenPort : 51820;

        var allowedRows = await _allowedNetworkRepository.GetByVpnPeerIdAsync(peer.Id, cancellationToken);
        var allowedIpsClient = VpnPeerRoutingHelper.ComposeClientAllowedIps(
            vpnNetwork.Cidr,
            allowedRows.Select(a => a.NetworkCidr),
            peer.PeerIp);

        var staticRoutes = await _staticRouteRepository.GetByVpnPeerIdAsync(peer.Id, cancellationToken);
        var sshPubKey = (host.SshPublicKey ?? "").Trim();
        var password = host.SshPassword ?? "";

        var sb = new StringBuilder();
        sb.AppendLine("#!/bin/bash");
        sb.AppendLine("set -e");
        sb.AppendLine();
        sb.AppendLine($"# Automais.IO — Setup automático para host: {host.Name}");
        sb.AppendLine($"# Gerado em: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"# Expira 10 minutos após geração.");
        sb.AppendLine();
        sb.AppendLine("echo '=== Automais.IO Host Setup ==='");
        sb.AppendLine();

        sb.AppendLine("# 1. Criar usuário automais-io");
        sb.AppendLine("if id \"automais-io\" &>/dev/null; then");
        sb.AppendLine("  echo 'Usuário automais-io já existe.'");
        sb.AppendLine("else");
        sb.AppendLine("  useradd -m -s /bin/bash automais-io");
        sb.AppendLine("  echo 'Usuário automais-io criado.'");
        sb.AppendLine("fi");
        sb.AppendLine($"echo 'automais-io:{password}' | chpasswd");
        sb.AppendLine("echo 'Senha configurada.'");
        sb.AppendLine();

        sb.AppendLine("# 2. Configurar sudoers (NOPASSWD)");
        sb.AppendLine("echo 'automais-io ALL=(ALL) NOPASSWD:ALL' > /etc/sudoers.d/automais-io");
        sb.AppendLine("chmod 440 /etc/sudoers.d/automais-io");
        sb.AppendLine("echo 'Sudoers configurado.'");
        sb.AppendLine();

        sb.AppendLine("# 3. Configurar chaves SSH");
        sb.AppendLine("mkdir -p /home/automais-io/.ssh");
        sb.AppendLine($"echo '{sshPubKey}' > /home/automais-io/.ssh/authorized_keys");
        sb.AppendLine("chmod 700 /home/automais-io/.ssh");
        sb.AppendLine("chmod 600 /home/automais-io/.ssh/authorized_keys");
        sb.AppendLine("chown -R automais-io:automais-io /home/automais-io/.ssh");
        sb.AppendLine("echo 'Chaves SSH configuradas.'");
        sb.AppendLine();

        sb.AppendLine("# 4. Instalar VPN (WireGuard)");
        sb.AppendLine("if command -v wg &>/dev/null; then");
        sb.AppendLine("  echo 'WireGuard já instalado.'");
        sb.AppendLine("else");
        sb.AppendLine("  apt-get update -qq");
        sb.AppendLine("  apt-get install -y -qq wireguard");
        sb.AppendLine("  echo 'WireGuard instalado.'");
        sb.AppendLine("fi");
        sb.AppendLine();

        sb.AppendLine("# 5. Configurar túnel VPN");
        sb.AppendLine("cat > /etc/wireguard/wg-automais.conf << 'WGEOF'");
        sb.AppendLine("[Interface]");
        sb.AppendLine($"PrivateKey = {peer.PrivateKey}");
        sb.AppendLine($"Address = {address}");
        foreach (var sn in staticRoutes)
        {
            if (string.IsNullOrWhiteSpace(sn.Destination))
                continue;
            var dst = sn.Destination.Trim();
            sb.AppendLine($"PostUp = ip route add {dst} dev %i");
            sb.AppendLine($"PostDown = ip route del {dst} dev %i || true");
        }
        sb.AppendLine();
        sb.AppendLine("[Peer]");
        if (!string.IsNullOrEmpty(serverPublicKey))
            sb.AppendLine($"PublicKey = {serverPublicKey}");
        else
            sb.AppendLine("# PublicKey = (servidor VPN ainda sem chave pública configurada)");
        sb.AppendLine($"Endpoint = {serverEndpoint}:{listenPort}");
        sb.AppendLine($"AllowedIPs = {allowedIpsClient}");
        sb.AppendLine("PersistentKeepalive = 25");
        sb.AppendLine("WGEOF");
        sb.AppendLine("echo 'Configuração VPN gravada.'");
        sb.AppendLine();

        sb.AppendLine("# 6. Habilitar e iniciar VPN");
        sb.AppendLine("systemctl enable wg-quick@wg-automais");
        sb.AppendLine("systemctl restart wg-quick@wg-automais");
        sb.AppendLine("echo 'Serviço VPN iniciado.'");
        sb.AppendLine();

        sb.AppendLine("echo ''");
        sb.AppendLine("echo '=== Automais.IO Host Setup concluído! ==='");
        sb.AppendLine($"echo 'IP na VPN: {host.VpnIp}'");
        sb.AppendLine($"echo 'Porta SSH: {host.SshPort}'");
        sb.AppendLine("echo 'Usuário: automais-io'");

        return sb.ToString();
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
            VpnPeerKeysConfigured = peer != null && !string.IsNullOrEmpty(peer.PublicKey) && !string.IsNullOrEmpty(peer.PrivateKey),
            SetupRequestedAt = h.SetupRequestedAt,
            VpnPeerPingAvgTimeMs = peer?.PingAvgTimeMs,
            VpnPeerPingSuccess = peer?.PingSuccess,
            VpnPeerPingPacketLoss = peer?.PingPacketLoss,
            VpnPeerLastHandshake = peer?.LastHandshake
        };
    }

    private async Task<InternalHostDto> MapToInternalDtoAsync(Host h, CancellationToken ct)
    {
        VpnPeer? peer = null;
        if (h.VpnPeerId.HasValue)
            peer = await _peerRepo.GetByIdAsync(h.VpnPeerId.Value, ct);

        var vpnPeerId = h.VpnPeerId ?? peer?.Id;

        return new InternalHostDto
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
            VpnPeerKeysConfigured = peer != null && !string.IsNullOrEmpty(peer.PublicKey) && !string.IsNullOrEmpty(peer.PrivateKey),
            SetupRequestedAt = h.SetupRequestedAt,
            VpnPeerPingAvgTimeMs = peer?.PingAvgTimeMs,
            VpnPeerPingSuccess = peer?.PingSuccess,
            VpnPeerPingPacketLoss = peer?.PingPacketLoss,
            VpnPeerLastHandshake = peer?.LastHandshake,
            SshPrivateKey = h.SshPrivateKey,
            SshPassword = h.SshPassword
        };
    }

    private static string GenerateRandomPassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(PasswordLength);
        var sb = new StringBuilder(PasswordLength);
        for (int i = 0; i < PasswordLength; i++)
            sb.Append(PasswordChars[bytes[i] % PasswordChars.Length]);
        return sb.ToString();
    }
}
