using Automais.Core.Configuration;
using Automais.Core.DTOs;
using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Automais.Core.Services;

/// <summary>
/// Serviço para gerenciamento de WireGuard dos Routers
/// Cria peers diretamente no banco de dados. O serviço Python (vpnserver.io) sincroniza
/// automaticamente a cada minuto e adiciona os peers às interfaces WireGuard.
/// </summary>
public class RouterWireGuardService : IRouterWireGuardService
{
    private readonly IRouterWireGuardPeerRepository _peerRepository;
    private readonly IRouterRepository _routerRepository;
    private readonly IVpnNetworkRepository _vpnNetworkRepository;
    private readonly IVpnServiceClient _vpnServiceClient;
    private readonly WireGuardSettings _wireGuardSettings;
    private readonly ILogger<RouterWireGuardService>? _logger;

    public RouterWireGuardService(
        IRouterWireGuardPeerRepository peerRepository,
        IRouterRepository routerRepository,
        IVpnNetworkRepository vpnNetworkRepository,
        IOptions<WireGuardSettings> wireGuardSettings,
        IVpnServiceClient vpnServiceClient,
        ILogger<RouterWireGuardService>? logger = null)
    {
        _peerRepository = peerRepository;
        _routerRepository = routerRepository;
        _vpnNetworkRepository = vpnNetworkRepository;
        _wireGuardSettings = wireGuardSettings.Value;
        _vpnServiceClient = vpnServiceClient;
        _logger = logger;
    }

    public async Task<IEnumerable<RouterWireGuardPeerDto>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default)
    {
        var peers = await _peerRepository.GetByRouterIdAsync(routerId, cancellationToken);
        return peers.Select(MapToDto);
    }

    public async Task<RouterWireGuardPeerDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var peer = await _peerRepository.GetByIdAsync(id, cancellationToken);
        return peer == null ? null : MapToDto(peer);
    }

    public async Task<RouterWireGuardPeerDto> CreatePeerAsync(Guid routerId, CreateRouterWireGuardPeerDto dto, CancellationToken cancellationToken = default)
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

        // Verificar se já existe peer para este router e network
        var existing = await _peerRepository.GetByRouterIdAndNetworkIdAsync(routerId, dto.VpnNetworkId, cancellationToken);
        if (existing != null)
        {
            throw new InvalidOperationException($"Já existe um peer WireGuard para este router e rede VPN.");
        }


        // Gerar chaves WireGuard localmente
        var (publicKey, privateKey) = await GenerateWireGuardKeysAsync(cancellationToken);

        // Alocar IP (manual ou automático)
        string routerIp;
        var allowedNetworks = new List<string>();
        
        if (!string.IsNullOrWhiteSpace(dto.PeerIp))
        {
            // PeerIp pode conter múltiplas redes separadas por vírgula (primeiro = IP do router, demais = redes permitidas)
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
                
                // Demais elementos são redes permitidas
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

        // Construir PeerIp (IP do router + redes permitidas). IP do router deve usar /32.
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

        var peer = new RouterWireGuardPeer
        {
            Id = Guid.NewGuid(),
            RouterId = routerId,
            VpnNetworkId = dto.VpnNetworkId,
            PublicKey = publicKey,
            PrivateKey = privateKey,
            PeerIp = peerIpValue,
            Endpoint = vpnNetwork.ServerEndpoint, // Endpoint vem da VpnNetwork
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Gerar e salvar configuração no banco
        peer.ConfigContent = GenerateRouterConfig(router, peer, vpnNetwork);

        var created = await _peerRepository.CreateAsync(peer, cancellationToken);
        created.VpnNetwork = vpnNetwork;
        return MapToDto(created);
    }

    public async Task<RouterWireGuardPeerDto> UpdatePeerAsync(Guid id, CreateRouterWireGuardPeerDto dto, CancellationToken cancellationToken = default)
    {
        var peer = await _peerRepository.GetByIdAsync(id, cancellationToken);
        if (peer == null)
        {
            throw new KeyNotFoundException($"Peer WireGuard com ID {id} não encontrado.");
        }

        var normalizedPeerIp = NormalizePeerIp(dto.PeerIp);
        peer.PeerIp = normalizedPeerIp;
        
        // Endpoint e ListenPort vêm da VpnNetwork
        peer.UpdatedAt = DateTime.UtcNow;

        var updated = await _peerRepository.UpdateAsync(peer, cancellationToken);
        updated.VpnNetwork = await _vpnNetworkRepository.GetByIdAsync(peer.VpnNetworkId, cancellationToken);
        return MapToDto(updated);
    }

    public async Task UpdatePeerStatsAsync(Guid id, UpdatePeerStatsDto dto, CancellationToken cancellationToken = default)
    {
        var peer = await _peerRepository.GetByIdAsync(id, cancellationToken);
        if (peer == null)
        {
            throw new KeyNotFoundException($"Peer WireGuard com ID {id} não encontrado.");
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
        var peer = await _peerRepository.GetByIdAsync(id, cancellationToken);
        if (peer == null)
        {
            return;
        }

        await _peerRepository.DeleteAsync(id, cancellationToken);
    }

    public async Task<RouterWireGuardConfigDto> GetConfigAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var peer = await _peerRepository.GetByIdAsync(id, cancellationToken);
        if (peer == null)
        {
            throw new KeyNotFoundException($"Peer WireGuard com ID {id} não encontrado.");
        }

        var vpnNetwork = await _vpnNetworkRepository.GetByIdAsync(peer.VpnNetworkId, cancellationToken);
        if (vpnNetwork == null)
        {
            throw new KeyNotFoundException($"Rede VPN com ID {peer.VpnNetworkId} não encontrada.");
        }

        var hadServerPublicKey = !string.IsNullOrWhiteSpace(vpnNetwork.ServerPublicKey);

        // Se ServerPublicKey está vazio, tentar obter do VPN server e salvar na VpnNetwork
        if (!hadServerPublicKey)
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

        // Só usar cache se já tínhamos a chave antes (cache antigo pode ter PublicKey vazio)
        if (!string.IsNullOrWhiteSpace(peer.ConfigContent) && hadServerPublicKey)
        {
            var router = await _routerRepository.GetByIdAsync(peer.RouterId, cancellationToken);
            var fileName = router != null 
                ? SanitizeFileName(router.Name) 
                : $"router_{peer.RouterId}.conf";
            
            if (!fileName.EndsWith(".conf", StringComparison.OrdinalIgnoreCase))
            {
                fileName = $"{fileName}.conf";
            }

            return new RouterWireGuardConfigDto
            {
                ConfigContent = peer.ConfigContent,
                FileName = fileName
            };
        }

        // Gerar config (peer antigo sem cache, ou acabamos de obter ServerPublicKey)
        var routerForConfig = await _routerRepository.GetByIdAsync(peer.RouterId, cancellationToken);
        if (routerForConfig == null)
        {
            throw new KeyNotFoundException($"Router com ID {peer.RouterId} não encontrado.");
        }

        var configContent = GenerateRouterConfig(routerForConfig, peer, vpnNetwork);
        
        peer.ConfigContent = configContent;
        peer.UpdatedAt = DateTime.UtcNow;
        await _peerRepository.UpdateAsync(peer, cancellationToken);
        
        var fileNameForConfig = SanitizeFileName(routerForConfig.Name);
        if (!fileNameForConfig.EndsWith(".conf", StringComparison.OrdinalIgnoreCase))
        {
            fileNameForConfig = $"{fileNameForConfig}.conf";
        }

        return new RouterWireGuardConfigDto
        {
            ConfigContent = configContent,
            FileName = fileNameForConfig
        };
    }

    public async Task<RouterWireGuardPeerDto> RegenerateKeysAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var peer = await _peerRepository.GetByIdAsync(id, cancellationToken);
        if (peer == null)
        {
            throw new KeyNotFoundException($"Peer WireGuard com ID {id} não encontrado.");
        }

        var vpnNetwork = peer.VpnNetwork ?? await _vpnNetworkRepository.GetByIdAsync(peer.VpnNetworkId, cancellationToken);
        if (vpnNetwork == null)
        {
            throw new InvalidOperationException($"Rede VPN {peer.VpnNetworkId} não encontrada para o peer.");
        }

        var router = peer.Router ?? await _routerRepository.GetByIdAsync(peer.RouterId, cancellationToken);
        if (router == null)
        {
            throw new InvalidOperationException($"Router {peer.RouterId} não encontrado para o peer.");
        }

        var (publicKey, privateKey) = await WireGuardKeyGenerator.GenerateKeyPairAsync(cancellationToken);
        peer.PublicKey = publicKey;
        peer.PrivateKey = privateKey;
        peer.VpnNetwork = vpnNetwork;
        peer.ConfigContent = GenerateRouterConfig(router, peer, vpnNetwork);
        peer.UpdatedAt = DateTime.UtcNow;

        await _peerRepository.UpdateAsync(peer, cancellationToken);

        var reloaded = await _peerRepository.GetByIdAsync(id, cancellationToken);
        return MapToDto(reloaded!);
    }

    public async Task RefreshPeerConfigsForNetworkAsync(Guid vpnNetworkId, CancellationToken cancellationToken = default)
    {
        var vpnNetwork = await _vpnNetworkRepository.GetByIdAsync(vpnNetworkId, cancellationToken);
        if (vpnNetwork == null)
        {
            return;
        }

        var peers = await _peerRepository.GetByVpnNetworkIdAsync(vpnNetworkId, cancellationToken);
        foreach (var peer in peers)
        {
            var router = await _routerRepository.GetByIdAsync(peer.RouterId, cancellationToken);
            if (router == null)
            {
                continue;
            }

            peer.ConfigContent = GenerateRouterConfig(router, peer, vpnNetwork);
            peer.UpdatedAt = DateTime.UtcNow;
            await _peerRepository.UpdateAsync(peer, cancellationToken);
        }
    }

    private static RouterWireGuardPeerDto MapToDto(RouterWireGuardPeer peer)
    {
        var listenPort = peer.VpnNetwork?.ListenPort > 0 ? peer.VpnNetwork.ListenPort : 51820;
        return new RouterWireGuardPeerDto
        {
            Id = peer.Id,
            RouterId = peer.RouterId,
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

    /// <summary>
    /// Gera chaves WireGuard usando wg genkey e wg pubkey
    /// </summary>
    private async Task<(string publicKey, string privateKey)> GenerateWireGuardKeysAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await WireGuardKeyGenerator.GenerateKeyPairAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Erro ao gerar chaves WireGuard");
            throw new InvalidOperationException($"Erro ao gerar chaves WireGuard: {ex.Message}", ex);
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

    /// <summary>
    /// Gera o conteúdo do arquivo de configuração WireGuard (.conf) para o router
    /// </summary>
    private static string GenerateRouterConfig(Router router, RouterWireGuardPeer peer, VpnNetwork vpnNetwork)
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
            configLines.Add("# PublicKey do servidor — preencha com a chave do servidor (wg show no servidor VPN)");
        }
        configLines.Add($"PublicKey = {serverPublicKey}");
        configLines.Add($"Endpoint = {serverEndpoint}:{listenPort}");

        var peerIpParts = peer.PeerIp.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var allowedNetworks = new List<string> { vpnNetwork.Cidr };
        if (peerIpParts.Length > 1)
            allowedNetworks.AddRange(peerIpParts.Skip(1));
        
        configLines.Add($"AllowedIPs = {string.Join(", ", allowedNetworks)}");
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

