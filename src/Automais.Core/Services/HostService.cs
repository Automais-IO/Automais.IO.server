using System.Security.Cryptography;
using System.Text;
using Automais.Core;
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
        {
            var trimmed = dto.VpnIp.Trim();
            if (string.IsNullOrEmpty(trimmed))
                throw new InvalidOperationException("VpnIp não pode ser vazio.");
            if (!host.VpnPeerId.HasValue)
                throw new InvalidOperationException("Host sem peer VPN; associe um peer antes de definir VpnIp.");

            var peerForIp = await _peerRepo.GetByIdAsync(host.VpnPeerId.Value, cancellationToken)
                ?? throw new InvalidOperationException("Peer VPN não encontrado.");
            var networkId = host.VpnNetworkId ?? peerForIp.VpnNetworkId;
            var newIpOnly = trimmed.Split('/')[0].Trim();
            var currentIp = HostDisplayName.PeerTunnelIpv4Only(peerForIp);
            if (!string.Equals(newIpOnly, currentIp, StringComparison.OrdinalIgnoreCase))
            {
                var allocated = await _ipAlloc.AllocateManualIpAsync(
                    networkId,
                    trimmed,
                    VpnResourceKind.Host,
                    host.Id,
                    host.Name,
                    cancellationToken);
                peerForIp.PeerIp = allocated.Contains('/') ? allocated : $"{allocated}/32";
                peerForIp.UpdatedAt = DateTime.UtcNow;
                await _peerRepo.UpdateAsync(peerForIp, cancellationToken);
            }
        }

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
        host.SetupCompletionToken = GenerateSetupCompletionToken();
        host.UpdatedAt = DateTime.UtcNow;
        await _hostRepository.UpdateAsync(host, cancellationToken);

        return $"{baseUrl.TrimEnd('/')}/api/hosts/{hostId}/setup-script";
    }

    public async Task CompleteSetupAsync(Guid hostId, string token, CancellationToken cancellationToken = default)
    {
        var host = await _hostRepository.GetByIdAsync(hostId, cancellationToken)
            ?? throw new KeyNotFoundException($"Host com ID {hostId} não encontrado.");

        if (host.ProvisioningStatus == HostProvisioningStatus.Ready)
            return;

        if (!host.SetupRequestedAt.HasValue)
            throw new InvalidOperationException("Setup não foi solicitado.");

        var elapsed = DateTime.UtcNow - host.SetupRequestedAt.Value;
        if (elapsed.TotalMinutes > SetupExpirationMinutes)
            throw new InvalidOperationException($"Confirmação de setup expirada ({SetupExpirationMinutes} min). Solicite o setup novamente.");

        if (host.ProvisioningStatus != HostProvisioningStatus.Installing)
            throw new InvalidOperationException("O host não está em instalação; gere o script de setup novamente se precisar.");

        var expected = host.SetupCompletionToken;
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(token))
            throw new InvalidOperationException("Token inválido.");

        var expBytes = Encoding.UTF8.GetBytes(expected);
        var tokBytes = Encoding.UTF8.GetBytes(token);
        if (expBytes.Length != tokBytes.Length || !CryptographicOperations.FixedTimeEquals(expBytes, tokBytes))
            throw new InvalidOperationException("Token inválido.");

        host.ProvisioningStatus = HostProvisioningStatus.Ready;
        host.SetupCompletionToken = null;
        host.UpdatedAt = DateTime.UtcNow;
        await _hostRepository.UpdateAsync(host, cancellationToken);
    }

    public async Task<string> GenerateSetupScriptAsync(Guid hostId, string publicApiBaseUrl, CancellationToken cancellationToken = default)
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

        if (string.IsNullOrEmpty(host.SetupCompletionToken))
        {
            host.SetupCompletionToken = GenerateSetupCompletionToken();
            host.UpdatedAt = DateTime.UtcNow;
            await _hostRepository.UpdateAsync(host, cancellationToken);
        }

        host.ProvisioningStatus = HostProvisioningStatus.Installing;
        host.UpdatedAt = DateTime.UtcNow;
        await _hostRepository.UpdateAsync(host, cancellationToken);

        return await BuildSetupScriptAsync(host, peer, vpnNetwork, publicApiBaseUrl, cancellationToken);
    }

    private async Task<string> BuildSetupScriptAsync(Host host, VpnPeer peer, VpnNetwork vpnNetwork, string publicApiBaseUrl, CancellationToken cancellationToken)
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
        var setupToken = host.SetupCompletionToken ?? "";
        var callbackUrl = $"{publicApiBaseUrl.TrimEnd('/')}/api/hosts/{host.Id}/setup-complete";

        var sb = new StringBuilder();
        sb.AppendLine("#!/bin/bash");
        sb.AppendLine("set -e");
        sb.AppendLine("# Mensagens em stderr: com \"wget ... | bash\" o stdout pode bufferizar; stderr aparece na hora.");
        sb.AppendLine("log() { printf '%s\\n' \"[Automais.IO] $*\" >&2; }");
        sb.AppendLine("trap 'ec=$?; log \"ERRO: comando falhou (código $ec, linha $LINENO).\"; exit \"$ec\"' ERR");
        sb.AppendLine("notify_automais_setup_done() {");
        sb.AppendLine("  local url=$1 token=$2");
        sb.AppendLine("  local body");
        sb.AppendLine("  body=$(printf '{\"token\":\"%s\"}' \"$token\")");
        sb.AppendLine("  if command -v curl &>/dev/null; then");
        sb.AppendLine("    curl -sfS -m 45 -X POST -H 'Content-Type: application/json' -d \"$body\" \"$url\" && return 0");
        sb.AppendLine("  fi");
        sb.AppendLine("  if command -v wget &>/dev/null; then");
        sb.AppendLine("    wget -qO- --timeout=45 --post-data=\"$body\" --header='Content-Type: application/json' \"$url\" && return 0");
        sb.AppendLine("  fi");
        sb.AppendLine("  return 1");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine($"# Automais.IO — Setup automático para host: {host.Name}");
        sb.AppendLine($"# Gerado em: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"# Expira 10 minutos após geração.");
        sb.AppendLine();
        sb.AppendLine("log '=== Host Setup — 7 passos (usuário, sudo, SSH, pacote VPN, configuração, serviço, confirmação ao painel) ==='");
        sb.AppendLine();

        sb.AppendLine("# 1/6 — Criar usuário automais-io");
        sb.AppendLine("log '[1/6] Verificando usuário automais-io...'");
        sb.AppendLine("if id \"automais-io\" &>/dev/null; then");
        sb.AppendLine("  log '[1/6] Usuário automais-io já existe.'");
        sb.AppendLine("else");
        sb.AppendLine("  useradd -m -s /bin/bash automais-io");
        sb.AppendLine("  log '[1/6] Usuário automais-io criado.'");
        sb.AppendLine("fi");
        sb.AppendLine("log '[1/6] Definindo senha do usuário...'");
        sb.AppendLine($"echo 'automais-io:{password}' | chpasswd");
        sb.AppendLine("log '[1/6] Senha configurada.'");
        sb.AppendLine();

        sb.AppendLine("# 2/6 — Configurar sudoers (NOPASSWD)");
        sb.AppendLine("log '[2/6] Ajustando sudoers...'");
        sb.AppendLine("echo 'automais-io ALL=(ALL) NOPASSWD:ALL' > /etc/sudoers.d/automais-io");
        sb.AppendLine("chmod 440 /etc/sudoers.d/automais-io");
        sb.AppendLine("log '[2/6] Sudoers configurado.'");
        sb.AppendLine();

        sb.AppendLine("# 3/6 — Configurar chaves SSH");
        sb.AppendLine("log '[3/6] Configurando authorized_keys...'");
        sb.AppendLine("mkdir -p /home/automais-io/.ssh");
        sb.AppendLine($"echo '{sshPubKey}' > /home/automais-io/.ssh/authorized_keys");
        sb.AppendLine("chmod 700 /home/automais-io/.ssh");
        sb.AppendLine("chmod 600 /home/automais-io/.ssh/authorized_keys");
        sb.AppendLine("chown -R automais-io:automais-io /home/automais-io/.ssh");
        sb.AppendLine("log '[3/6] Chaves SSH configuradas.'");
        sb.AppendLine();

        sb.AppendLine("# 4/6 — Instalar pacote do túnel VPN (implementação: WireGuard)");
        sb.AppendLine("if command -v wg &>/dev/null; then");
        sb.AppendLine("  log '[4/6] Pacote WireGuard já instalado — nada a baixar.'");
        sb.AppendLine("else");
        sb.AppendLine("  log '[4/6] Instalando WireGuard via apt — pode levar vários minutos e gerar bastante saída abaixo.'");
        sb.AppendLine("  export DEBIAN_FRONTEND=noninteractive");
        sb.AppendLine("  apt-get update");
        sb.AppendLine("  apt-get install -y wireguard");
        sb.AppendLine("  log '[4/6] WireGuard instalado.'");
        sb.AppendLine("fi");
        sb.AppendLine();

        sb.AppendLine("# 5/6 — Configurar túnel VPN");
        sb.AppendLine("log '[5/6] Gravando /etc/wireguard/wg-automais.conf...'");
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
        sb.AppendLine("log '[5/6] Configuração do túnel VPN gravada.'");
        sb.AppendLine();

        sb.AppendLine("# 6/6 — Habilitar e iniciar serviço VPN");
        sb.AppendLine("log '[6/6] Habilitando e iniciando wg-quick@wg-automais...'");
        sb.AppendLine("systemctl enable wg-quick@wg-automais");
        sb.AppendLine("systemctl restart wg-quick@wg-automais");
        sb.AppendLine("if systemctl is-active --quiet wg-quick@wg-automais; then");
        sb.AppendLine("  log '[6/6] Serviço VPN ativo (running).'");
        sb.AppendLine("else");
        sb.AppendLine("  log '[6/6] Aviso: serviço não está active — veja: journalctl -u wg-quick@wg-automais -n 50 --no-pager'");
        sb.AppendLine("fi");
        sb.AppendLine();

        sb.AppendLine("log ''");
        sb.AppendLine("log '=== Setup local concluído (VPN + SSH) ==='");
        sb.AppendLine($"log 'IP na VPN: {HostDisplayName.PeerTunnelIpv4Only(peer)}'");
        sb.AppendLine($"log 'Porta SSH: {host.SshPort}'");
        sb.AppendLine("log 'Usuário: automais-io'");
        sb.AppendLine();
        sb.AppendLine("# 7/7 — Confirmar ao painel Automais.IO (status Ready no banco)");
        sb.AppendLine($"AUTOMAIS_SETUP_CALLBACK_URL='{callbackUrl}'");
        sb.AppendLine($"AUTOMAIS_SETUP_TOKEN='{setupToken}'");
        sb.AppendLine("log '[7/7] Notificando o painel que o setup terminou...'");
        sb.AppendLine("if notify_automais_setup_done \"$AUTOMAIS_SETUP_CALLBACK_URL\" \"$AUTOMAIS_SETUP_TOKEN\"; then");
        sb.AppendLine("  log '[7/7] Painel atualizado (provisionamento Ready).'");
        sb.AppendLine("else");
        sb.AppendLine("  log '[7/7] Aviso: não foi possível avisar o painel (firewall/DNS/rede). O host já está configurado; use \"Conectar-se\" de novo se o status continuar Installing.'");
        sb.AppendLine("fi");

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
            Name = HostDisplayName.ForUi(h, peer),
            HostKind = h.HostKind,
            VpnNetworkId = h.VpnNetworkId,
            VpnNetworkServerEndpoint = h.VpnNetwork?.ServerEndpoint,
            VpnIp = HostDisplayName.PeerTunnelIpv4Only(peer),
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
            VpnPeerReachableViaVpn = peer?.ReachableViaVpn
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
            Name = HostDisplayName.ForUi(h, peer),
            HostKind = h.HostKind,
            VpnNetworkId = h.VpnNetworkId,
            VpnNetworkServerEndpoint = h.VpnNetwork?.ServerEndpoint,
            VpnIp = HostDisplayName.PeerTunnelIpv4Only(peer),
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
            VpnPeerReachableViaVpn = peer?.ReachableViaVpn,
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

    /// <summary>Token URL-safe para o POST de confirmação (32 bytes aleatórios).</summary>
    private static string GenerateSetupCompletionToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
