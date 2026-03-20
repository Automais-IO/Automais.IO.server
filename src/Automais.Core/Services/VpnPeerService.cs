using Automais.Core.DTOs;
using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Automais.Core.Services;

/// <summary>
/// Gerencia linhas em <c>vpn_peers</c>. O serviço Python sincroniza peers ao servidor VPN periodicamente.
/// </summary>
/// <remarks>
/// Para agentes de IA: o túnel operacional servidor/cliente e os .conf gerados seguem o stack <strong>WireGuard</strong>.
/// A nomenclatura "VPN" é deliberada para o produto; comandos no host (wg/wg-quick) e formato de chaves são os do WireGuard.
/// </remarks>
public class VpnPeerService : IVpnPeerService
{
    private readonly IVpnPeerRepository _peerRepository;
    private readonly IRouterRepository _routerRepository;
    private readonly IVpnNetworkRepository _vpnNetworkRepository;
    private readonly IVpnServiceClient _vpnServiceClient;
    private readonly IAllowedNetworkRepository _allowedNetworkRepository;
    private readonly IRemoteNetworkRepository _remoteNetworkRepository;
    private readonly ILogger<VpnPeerService>? _logger;

    public VpnPeerService(
        IVpnPeerRepository peerRepository,
        IRouterRepository routerRepository,
        IVpnNetworkRepository vpnNetworkRepository,
        IVpnServiceClient vpnServiceClient,
        IAllowedNetworkRepository allowedNetworkRepository,
        IRemoteNetworkRepository remoteNetworkRepository,
        ILogger<VpnPeerService>? logger = null)
    {
        _peerRepository = peerRepository;
        _routerRepository = routerRepository;
        _vpnNetworkRepository = vpnNetworkRepository;
        _vpnServiceClient = vpnServiceClient;
        _allowedNetworkRepository = allowedNetworkRepository;
        _remoteNetworkRepository = remoteNetworkRepository;
        _logger = logger;
    }

    public async Task<IEnumerable<VpnPeerDto>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default)
    {
        var peers = await _peerRepository.GetByRouterIdAsync(routerId, cancellationToken);
        var list = new List<VpnPeerDto>();
        foreach (var p in peers)
            list.Add(await MapToDtoAsync(p, routerId, cancellationToken));
        return list;
    }

    public async Task<VpnPeerDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var peer = await _peerRepository.GetByIdAsync(id, cancellationToken);
        return peer == null ? null : await MapToDtoAsync(peer, null, cancellationToken);
    }

    public async Task<VpnPeerDto> CreatePeerAsync(Guid routerId, CreateVpnPeerDto dto, CancellationToken cancellationToken = default)
    {
        var router = await _routerRepository.GetByIdAsync(routerId, cancellationToken);
        if (router == null)
        {
            throw new KeyNotFoundException($"Router com ID {routerId} não encontrado.");
        }

        var vpnNetwork = await _vpnNetworkRepository.GetByIdAsync(dto.VpnNetworkId, cancellationToken);
        if (vpnNetwork == null)
        {
            throw new KeyNotFoundException($"Rede VPN com ID {dto.VpnNetworkId} não encontrada.");
        }

        if (router.VpnPeerId.HasValue)
        {
            throw new InvalidOperationException(
                "Este router já possui um peer VPN. Remova o peer atual antes de criar outro ou use regenerar chaves.");
        }

        // Gerar chaves do túnel VPN localmente
        var (publicKey, privateKey) = await GenerateVpnTunnelKeysAsync(cancellationToken);

        // Alocar IP (manual ou automático)
        string routerIp;
        var allowedNetworks = new List<string>();
        
        if (!string.IsNullOrWhiteSpace(dto.PeerIp))
        {
            // PeerIp pode conter múltiplas redes separadas por vírgula (primeiro = IP do router, demais = redes destino)
            var networks = dto.PeerIp.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            
            if (networks.Length > 0)
            {
                // Primeiro elemento é o IP do router (manual)
                routerIp = networks[0];
                
                // Validar formato do IP manual
                if (!IsValidIpWithPrefix(routerIp))
                {
                    throw new InvalidOperationException($"IP manual inválido: {routerIp}. Use o formato IP/PREFIX (ex: 10.100.1.50/32)");
                }
                
                // Verificar se IP está na rede VPN
                if (!IsIpInNetwork(routerIp, vpnNetwork.Cidr))
                {
                    throw new InvalidOperationException($"IP {routerIp} não está na rede VPN {vpnNetwork.Cidr}");
                }
                
                // Demais elementos são redes destino
                if (networks.Length > 1)
                {
                    allowedNetworks.AddRange(networks.Skip(1));
                }
            }
            else
            {
                // Alocar IP automaticamente
                routerIp = await AllocateNextAvailableIpAsync(vpnNetwork, cancellationToken);
            }
        }
        else
        {
            // Alocar IP automaticamente
            routerIp = await AllocateNextAvailableIpAsync(vpnNetwork, cancellationToken);
        }

        // Construir PeerIp (IP do router + redes destino). IP do router deve usar /32.
        var routerIpNormalized = routerIp;
        if (IsValidIpWithPrefix(routerIp))
        {
            var ipParts = routerIp.Split('/');
            if (ipParts.Length == 2 && ipParts[1] != "32")
                routerIpNormalized = $"{ipParts[0]}/32";
        }
        else
            routerIpNormalized = $"{routerIp}/32";
        var peerIpParts = new List<string> { routerIpNormalized };
        peerIpParts.AddRange(allowedNetworks);
        var peerIpValue = NormalizePeerIp(string.Join(",", peerIpParts));

        var peer = new VpnPeer
        {
            Id = Guid.NewGuid(),
            VpnNetworkId = dto.VpnNetworkId,
            PublicKey = publicKey,
            PrivateKey = privateKey,
            PeerIp = peerIpValue,
            Endpoint = vpnNetwork.ServerEndpoint, // Endpoint vem da VpnNetwork
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await _peerRepository.CreateAsync(peer, cancellationToken);
        created.VpnNetwork = vpnNetwork;

        router.VpnPeerId = created.Id;
        await _routerRepository.UpdateAsync(router, cancellationToken);

        return await MapToDtoAsync(created, routerId, cancellationToken);
    }

    public async Task<VpnPeerDto> UpdatePeerAsync(Guid id, CreateVpnPeerDto dto, CancellationToken cancellationToken = default)
    {
        var peer = await _peerRepository.GetByIdAsync(id, cancellationToken);
        if (peer == null)
        {
            throw new KeyNotFoundException($"Peer VPN com ID {id} não encontrado.");
        }

        var normalizedPeerIp = NormalizePeerIp(dto.PeerIp);
        peer.PeerIp = normalizedPeerIp;
        
        // Endpoint e ListenPort vêm da VpnNetwork
        peer.UpdatedAt = DateTime.UtcNow;

        var updated = await _peerRepository.UpdateAsync(peer, cancellationToken);
        updated.VpnNetwork = await _vpnNetworkRepository.GetByIdAsync(peer.VpnNetworkId, cancellationToken);
        return await MapToDtoAsync(updated, null, cancellationToken);
    }

    public async Task UpdatePeerStatsAsync(Guid id, UpdatePeerStatsDto dto, CancellationToken cancellationToken = default)
    {
        var peer = await _peerRepository.GetByIdAsync(id, cancellationToken);
        if (peer == null)
        {
            throw new KeyNotFoundException($"Peer VPN com ID {id} não encontrado.");
        }

        // Atualizar apenas estatísticas (não configuração)
        if (dto.LastHandshake.HasValue)
        {
            peer.LastHandshake = dto.LastHandshake.Value;
        }
        if (dto.BytesReceived.HasValue)
        {
            peer.BytesReceived = dto.BytesReceived.Value;
        }
        if (dto.BytesSent.HasValue)
        {
            peer.BytesSent = dto.BytesSent.Value;
        }
        if (dto.PingSuccess.HasValue)
        {
            peer.PingSuccess = dto.PingSuccess.Value;
        }
        if (dto.PingAvgTimeMs.HasValue)
        {
            peer.PingAvgTimeMs = dto.PingAvgTimeMs.Value;
        }
        if (dto.PingPacketLoss.HasValue)
        {
            peer.PingPacketLoss = dto.PingPacketLoss.Value;
        }
        
        peer.UpdatedAt = DateTime.UtcNow;

        await _peerRepository.UpdateAsync(peer, cancellationToken);
    }

    public async Task DeletePeerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _peerRepository.DeleteAsync(id, cancellationToken);
    }

    public async Task<VpnPeerConfigDto> GetConfigAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var peer = await _peerRepository.GetByIdAsync(id, cancellationToken);
        if (peer == null)
        {
            throw new KeyNotFoundException($"Peer VPN com ID {id} não encontrado.");
        }

        var vpnNetwork = await _vpnNetworkRepository.GetByIdAsync(peer.VpnNetworkId, cancellationToken);
        if (vpnNetwork == null)
        {
            throw new KeyNotFoundException($"Rede VPN com ID {peer.VpnNetworkId} não encontrada.");
        }

        // Se ServerPublicKey está vazio, tentar obter do VPN server e salvar na VpnNetwork
        if (string.IsNullOrWhiteSpace(vpnNetwork.ServerPublicKey))
        {
            var serverKey = await _vpnServiceClient.GetServerPublicKeyAsync(peer.VpnNetworkId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(serverKey))
            {
                vpnNetwork.ServerPublicKey = serverKey.Trim();
                vpnNetwork.UpdatedAt = DateTime.UtcNow;
                await _vpnNetworkRepository.UpdateAsync(vpnNetwork, cancellationToken);
                _logger?.LogInformation("ServerPublicKey obtida do VPN server e salva na VpnNetwork {VpnNetworkId}", vpnNetwork.Id);
            }
        }

        var routerForPeer = await _routerRepository.GetByVpnPeerIdAsync(peer.Id, cancellationToken);

        if (routerForPeer == null)
        {
            var hostConfig = await GenerateHostVpnClientConfigAsync(peer, vpnNetwork, cancellationToken);
            return new VpnPeerConfigDto
            {
                ConfigContent = hostConfig,
                FileName = $"host_{peer.Id}.conf"
            };
        }

        var configContent = await GenerateRouterConfigAsync(routerForPeer, peer, vpnNetwork, cancellationToken);
        var fileNameForConfig = SanitizeFileName(routerForPeer.Name);
        if (!fileNameForConfig.EndsWith(".conf", StringComparison.OrdinalIgnoreCase))
            fileNameForConfig = $"{fileNameForConfig}.conf";

        return new VpnPeerConfigDto
        {
            ConfigContent = configContent,
            FileName = fileNameForConfig
        };
    }

    public async Task<VpnPeerDto> RegenerateKeysAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var peer = await _peerRepository.GetByIdAsync(id, cancellationToken);
        if (peer == null)
        {
            throw new KeyNotFoundException($"Peer VPN com ID {id} não encontrado.");
        }

        var vpnNetwork = peer.VpnNetwork ?? await _vpnNetworkRepository.GetByIdAsync(peer.VpnNetworkId, cancellationToken);
        if (vpnNetwork == null)
        {
            throw new InvalidOperationException($"Rede VPN {peer.VpnNetworkId} não encontrada para o peer.");
        }

        var (publicKey, privateKey) = await VpnTunnelKeyGenerator.GenerateKeyPairAsync(cancellationToken);
        peer.PublicKey = publicKey;
        peer.PrivateKey = privateKey;
        peer.VpnNetwork = vpnNetwork;

        peer.UpdatedAt = DateTime.UtcNow;

        await _peerRepository.UpdateAsync(peer, cancellationToken);

        var reloaded = await _peerRepository.GetByIdAsync(id, cancellationToken);
        return await MapToDtoAsync(reloaded!, null, cancellationToken);
    }

    public Task RefreshPeerConfigsForNetworkAsync(Guid vpnNetworkId, CancellationToken cancellationToken = default)
    {
        // .conf não é mais persistido; GetConfigAsync monta o conteúdo na hora do download.
        return Task.CompletedTask;
    }

    private async Task<VpnPeerDto> MapToDtoAsync(
        VpnPeer peer,
        Guid? linkedRouterId,
        CancellationToken cancellationToken = default)
    {
        var listenPort = peer.VpnNetwork?.ListenPort > 0 ? peer.VpnNetwork.ListenPort : 51820;
        var routerId = linkedRouterId ?? (await _routerRepository.GetByVpnPeerIdAsync(peer.Id, cancellationToken))?.Id;
        return new VpnPeerDto
        {
            Id = peer.Id,
            RouterId = routerId,
            VpnNetworkId = peer.VpnNetworkId,
            PublicKey = peer.PublicKey,
            PeerIp = peer.PeerIp,
            Endpoint = peer.Endpoint,
            ListenPort = listenPort,
            LastHandshake = peer.LastHandshake,
            BytesReceived = peer.BytesReceived,
            BytesSent = peer.BytesSent,
            PingSuccess = peer.PingSuccess,
            PingAvgTimeMs = peer.PingAvgTimeMs,
            PingPacketLoss = peer.PingPacketLoss,
            IsEnabled = peer.IsEnabled,
            CreatedAt = peer.CreatedAt,
            UpdatedAt = peer.UpdatedAt
        };
    }

    /// <summary>Gera chaves do túnel VPN (ferramenta <c>wg</c> no host).</summary>
    private async Task<(string publicKey, string privateKey)> GenerateVpnTunnelKeysAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await VpnTunnelKeyGenerator.GenerateKeyPairAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Erro ao gerar chaves do túnel VPN");
            throw new InvalidOperationException($"Erro ao gerar chaves do túnel VPN: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Aloca o próximo IP disponível na rede VPN
    /// </summary>
    private async Task<string> AllocateNextAvailableIpAsync(VpnNetwork vpnNetwork, CancellationToken cancellationToken)
    {
        // Parsear CIDR da rede VPN (ex: "10.100.1.0/24")
        var cidrParts = vpnNetwork.Cidr.Split('/');
        if (cidrParts.Length != 2)
        {
            throw new InvalidOperationException($"CIDR inválido: {vpnNetwork.Cidr}");
        }

        if (!IPAddress.TryParse(cidrParts[0], out var networkIp))
        {
            throw new InvalidOperationException($"IP de rede inválido: {cidrParts[0]}");
        }

        if (!int.TryParse(cidrParts[1], out var prefixLength) || prefixLength < 0 || prefixLength > 32)
        {
            throw new InvalidOperationException($"Prefix length inválido: {cidrParts[1]}");
        }

        // Buscar IPs já alocados nesta rede VPN
        var allocatedIps = await _peerRepository.GetAllocatedIpsByNetworkAsync(vpnNetwork.Id, cancellationToken);
        var allocatedIpSet = new HashSet<string>();
        
        foreach (var allocatedIp in allocatedIps)
        {
            var firstIp = allocatedIp.Split(',')[0].Trim();
            if (IsValidIpWithPrefix(firstIp))
            {
                var ipOnly = firstIp.Split('/')[0];
                allocatedIpSet.Add(ipOnly);
            }
        }

        // Encontrar próximo IP disponível (começando do .2, pois .1 é reservado para o servidor)
        var networkBytes = networkIp.GetAddressBytes();
        
        // Converter IP de rede para inteiro (big-endian)
        var networkValue = (uint)(networkBytes[0] << 24 | networkBytes[1] << 16 | networkBytes[2] << 8 | networkBytes[3]);
        var hostBits = 32 - prefixLength;
        var maxHosts = (uint)Math.Pow(2, hostBits) - 2; // -2 para excluir .0 e broadcast
        
        // Limitar busca até 254 para evitar problemas com redes muito grandes
        var maxSearch = Math.Min(maxHosts, 254u);
        
        for (uint hostOffset = 2; hostOffset <= maxSearch; hostOffset++)
        {
            var ipValue = networkValue + hostOffset;
            
            // Converter de volta para IPAddress (big-endian)
            var ipBytes = new byte[4];
            ipBytes[0] = (byte)((ipValue >> 24) & 0xFF);
            ipBytes[1] = (byte)((ipValue >> 16) & 0xFF);
            ipBytes[2] = (byte)((ipValue >> 8) & 0xFF);
            ipBytes[3] = (byte)(ipValue & 0xFF);
            
            var candidateIp = new IPAddress(ipBytes).ToString();
            
            if (!allocatedIpSet.Contains(candidateIp))
            {
                // IMPORTANTE: Para IPs individuais, usar /32 (não o prefixo da rede)
                // O prefixo da rede (/24) é usado apenas para a interface do servidor
                return $"{candidateIp}/32";
            }
        }

        throw new InvalidOperationException($"Não há IPs disponíveis na rede VPN {vpnNetwork.Cidr}");
    }

    /// <summary>
    /// Normaliza PeerIp: primeiro IP (router) em /32; redes adicionais mantêm o prefixo.
    /// </summary>
    private static string NormalizePeerIp(string? peerIp)
    {
        if (string.IsNullOrWhiteSpace(peerIp))
            return peerIp ?? string.Empty;

        var parts = peerIp.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return peerIp;

        // Normalizar primeiro IP (IP do router) para /32
        var firstIp = parts[0];
        if (IsValidIpWithPrefix(firstIp))
        {
            var ipParts = firstIp.Split('/');
            if (ipParts.Length == 2 && ipParts[1] != "32")
            {
                // Se o prefixo não é /32, normalizar para /32
                parts[0] = $"{ipParts[0]}/32";
            }
        }
        else
        {
            // Se não tem prefixo, adicionar /32
            parts[0] = $"{firstIp}/32";
        }

        return string.Join(",", parts);
    }

    /// <summary>
    /// Valida se o formato do IP está correto (IP/PREFIX)
    /// </summary>
    private static bool IsValidIpWithPrefix(string ipWithPrefix)
    {
        if (string.IsNullOrWhiteSpace(ipWithPrefix))
            return false;

        var parts = ipWithPrefix.Split('/');
        if (parts.Length != 2)
            return false;

        if (!IPAddress.TryParse(parts[0], out _))
            return false;

        if (!int.TryParse(parts[1], out var prefix) || prefix < 0 || prefix > 32)
            return false;

        return true;
    }

    /// <summary>
    /// Verifica se um IP está dentro de uma rede CIDR
    /// </summary>
    private static bool IsIpInNetwork(string ipWithPrefix, string networkCidr)
    {
        try
        {
            var ipParts = ipWithPrefix.Split('/');
            if (ipParts.Length != 2)
                return false;

            if (!IPAddress.TryParse(ipParts[0], out var ip))
                return false;

            var networkParts = networkCidr.Split('/');
            if (networkParts.Length != 2)
                return false;

            if (!IPAddress.TryParse(networkParts[0], out var networkIp))
                return false;

            if (!int.TryParse(networkParts[1], out var prefixLength))
                return false;

            // Calcular máscara de rede
            var mask = (uint)(0xFFFFFFFF << (32 - prefixLength));
            mask = (uint)IPAddress.HostToNetworkOrder((int)mask);

            var ipBytes = BitConverter.ToUInt32(ip.GetAddressBytes(), 0);
            var networkBytes = BitConverter.ToUInt32(networkIp.GetAddressBytes(), 0);

            return (ipBytes & mask) == (networkBytes & mask);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Config .conf para host Linux (peer referenciado só por <see cref="Host.VpnPeerId"/>).</summary>
    private async Task<string> GenerateHostVpnClientConfigAsync(VpnPeer peer, VpnNetwork vpnNetwork, CancellationToken cancellationToken)
    {
        var addrPart = peer.PeerIp.Split(',')[0].Trim();
        var addressLine = addrPart.Contains('/') ? addrPart : $"{addrPart}/32";

        var serverEndpoint = vpnNetwork.ServerEndpoint ?? "automais.io";
        var serverPublicKey = (vpnNetwork.ServerPublicKey ?? "").Trim();
        var listenPort = vpnNetwork.ListenPort > 0 ? vpnNetwork.ListenPort : 51820;

        var allowedRows = await _allowedNetworkRepository.GetByVpnPeerIdAsync(peer.Id, cancellationToken);
        var allowedIps = VpnPeerRoutingHelper.ComposeClientAllowedIps(
            vpnNetwork.Cidr,
            allowedRows.Select(a => a.NetworkCidr),
            peer.PeerIp);

        var lines = new List<string>
        {
            "# Automais.IO — peer de host (Linux)",
            $"# Gerado em: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
            "",
            "[Interface]",
            $"PrivateKey = {peer.PrivateKey}",
            $"Address = {addressLine}",
            "",
            "[Peer]",
        };
        if (string.IsNullOrEmpty(serverPublicKey))
            lines.Add("# PublicKey do servidor — preencha após consultar o servidor VPN");
        lines.Add($"PublicKey = {serverPublicKey}");
        lines.Add($"Endpoint = {serverEndpoint}:{listenPort}");
        lines.Add($"AllowedIPs = {allowedIps}");
        lines.Add("PersistentKeepalive = 25");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Gera o conteúdo do arquivo de configuração do cliente VPN (.conf) para o router
    /// </summary>
    private async Task<string> GenerateRouterConfigAsync(Router router, VpnPeer peer, VpnNetwork vpnNetwork, CancellationToken cancellationToken)
    {
        // Extrair IP do router (primeiro elemento do PeerIp)
        var routerIpWithPrefix = peer.PeerIp.Split(',')[0].Trim();
        var routerIp = routerIpWithPrefix;
        if (routerIpWithPrefix.Contains('/'))
        {
            routerIp = routerIpWithPrefix.Split('/')[0];
        }
        
        // Extrair prefixo da rede VPN (ex: 10.222.111.0/24 -> /24)
        var cidrParts = vpnNetwork.Cidr.Split('/');
        var networkPrefix = cidrParts.Length == 2 ? cidrParts[1] : "24";
        
        // Extrair IP do servidor da rede VPN (primeiro IP da rede + 1)
        var serverIp = ExtractServerIpFromCidr(vpnNetwork.Cidr);
        
        var serverEndpoint = vpnNetwork.ServerEndpoint ?? "automais.io";
        var serverPublicKey = (vpnNetwork.ServerPublicKey ?? "").Trim();
        var listenPort = vpnNetwork.ListenPort > 0 ? vpnNetwork.ListenPort : 51820;

        var configLines = new List<string>
        {
            "# Configuração VPN para Router",
            "",
            $"# Router (peer): {router.Name}",
            $"# Server (endpoint): {serverEndpoint}",
            $"# Gerado em: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
            "",
            "[Interface]",
            $"PrivateKey = {peer.PrivateKey}",
            $"Address = {routerIp}/{networkPrefix}", // Usar /24 do CIDR da VPN (não /32 do BD) para RouterOS importar corretamente
            "",
            "# Peer = servidor VPN (este router conecta a ele)",
            "[Peer]",
        };

        if (string.IsNullOrEmpty(serverPublicKey))
        {
            configLines.Add("# PublicKey do servidor — preencha com a chave obtida no servidor VPN");
        }
        configLines.Add($"PublicKey = {serverPublicKey}");
        configLines.Add($"Endpoint = {serverEndpoint}:{listenPort}");

        var allowedRows = await _allowedNetworkRepository.GetByVpnPeerIdAsync(peer.Id, cancellationToken);
        var allowedIpsClient = VpnPeerRoutingHelper.ComposeClientAllowedIps(
            vpnNetwork.Cidr,
            allowedRows.Select(a => a.NetworkCidr),
            peer.PeerIp);

        configLines.Add($"AllowedIPs = {allowedIpsClient}");
        configLines.Add("PersistentKeepalive = 25");

        return string.Join("\n", configLines);
    }
    
    /// <summary>
    /// Extrai o IP do servidor a partir do CIDR (primeiro IP da rede + 1)
    /// Exemplo: 10.222.111.0/24 -> 10.222.111.1
    /// </summary>
    private static string ExtractServerIpFromCidr(string cidr)
    {
        try
        {
            var parts = cidr.Split('/');
            if (parts.Length != 2)
                return "10.0.0.1"; // Fallback
            
            if (!IPAddress.TryParse(parts[0], out var networkIp))
                return "10.0.0.1"; // Fallback
            
            var networkBytes = networkIp.GetAddressBytes();
            var networkValue = (uint)(networkBytes[0] << 24 | networkBytes[1] << 16 | networkBytes[2] << 8 | networkBytes[3]);
            var serverValue = networkValue + 1; // Primeiro IP da rede + 1
            
            var serverBytes = new byte[4];
            serverBytes[0] = (byte)((serverValue >> 24) & 0xFF);
            serverBytes[1] = (byte)((serverValue >> 16) & 0xFF);
            serverBytes[2] = (byte)((serverValue >> 8) & 0xFF);
            serverBytes[3] = (byte)(serverValue & 0xFF);
            
            return new IPAddress(serverBytes).ToString();
        }
        catch
        {
            return "10.0.0.1"; // Fallback
        }
    }

    /// <summary>
    /// Sanitiza o nome do arquivo removendo caracteres inválidos
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "router";

        // Remover caracteres inválidos para nomes de arquivo (sem usar Path para manter na camada Core)
        var invalidChars = new[] { '"', '<', '>', '|', ':', '*', '?', '\\', '/' };
        var sanitized = new string(fileName
            .Where(c => !invalidChars.Contains(c))
            .ToArray());

        // Substituir espaços por underscores
        sanitized = sanitized.Replace(" ", "_");

        // Remover underscores múltiplos
        while (sanitized.Contains("__"))
        {
            sanitized = sanitized.Replace("__", "_");
        }

        // Remover underscores no início e fim
        sanitized = sanitized.Trim('_');

        // Se ficou vazio após sanitização, usar nome padrão
        if (string.IsNullOrWhiteSpace(sanitized))
            return "router";

        return sanitized;
    }
}
