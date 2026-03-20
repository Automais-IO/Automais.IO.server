using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Automais.Core.DTOs;
using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace Automais.Infrastructure.Services;

public class UserVpnService : IUserVpnService
{
    private readonly ITenantUserRepository _userRepository;
    private readonly IVpnNetworkRepository _vpnNetworkRepository;
    private readonly IUserAllowedRouteRepository _userAllowedRouteRepository;
    private readonly ILogger<UserVpnService>? _logger;

    public UserVpnService(
        ITenantUserRepository userRepository,
        IVpnNetworkRepository vpnNetworkRepository,
        IUserAllowedRouteRepository userAllowedRouteRepository,
        ILogger<UserVpnService>? logger = null)
    {
        _userRepository = userRepository;
        _vpnNetworkRepository = vpnNetworkRepository;
        _userAllowedRouteRepository = userAllowedRouteRepository;
        _logger = logger;
    }

    public async Task<UserVpnConfigDto> GetUserVpnConfigAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new KeyNotFoundException($"Usuário com ID {userId} não encontrado.");
        }

        if (!user.VpnEnabled)
        {
            throw new InvalidOperationException("VPN não está habilitada para este usuário.");
        }

        // Garantir que o usuário está provisionado
        await EnsureUserVpnProvisionedAsync(userId, cancellationToken);

        var vpnNetwork = await _vpnNetworkRepository.GetDefaultOrFirstForTenantAsync(user.TenantId, cancellationToken);
        if (vpnNetwork == null)
        {
            throw new InvalidOperationException("Nenhuma rede VPN configurada para o tenant.");
        }

        // Gerar configuração do cliente VPN
        var configContent = await GenerateUserConfigContentAsync(user, vpnNetwork, cancellationToken);
        var fileName = SanitizeFileName($"automais-{user.Email.Replace("@", "_")}.conf");

        // Buscar rotas permitidas do usuário
        var allowedRoutes = await _userAllowedRouteRepository.GetByUserIdAsync(userId, cancellationToken);
        var allowedRoutesDto = allowedRoutes.Select(r => new UserAllowedRouteDto
        {
            Id = r.Id,
            RouterId = r.RouterId,
            RouterName = r.Router?.Name ?? "Unknown",
            NetworkCidr = r.NetworkCidr,
            Description = r.Description
        }).ToList();

        // Extrair gateway IP da rede VPN (primeiro IP da rede)
        var vpnGatewayIp = ExtractGatewayIp(vpnNetwork.Cidr);

        return new UserVpnConfigDto
        {
            ConfigContent = configContent,
            FileName = fileName,
            VpnEnabled = user.VpnEnabled,
            VpnDeviceName = user.VpnDeviceName,
            VpnPublicKey = user.VpnPublicKey,
            VpnIpAddress = user.VpnIpAddress,
            AllowedRoutes = allowedRoutesDto,
            VpnGatewayIp = vpnGatewayIp
        };
    }

    public async Task EnsureUserVpnProvisionedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new KeyNotFoundException($"Usuário com ID {userId} não encontrado.");
        }

        if (!user.VpnEnabled)
        {
            return; // Não precisa provisionar se VPN não está habilitada
        }

        // Se já tem chaves e IP, está provisionado
        if (!string.IsNullOrEmpty(user.VpnPublicKey) && !string.IsNullOrEmpty(user.VpnPrivateKey) && !string.IsNullOrEmpty(user.VpnIpAddress))
        {
            return;
        }

        var vpnNetwork = await _vpnNetworkRepository.GetDefaultOrFirstForTenantAsync(user.TenantId, cancellationToken);
        if (vpnNetwork == null)
        {
            throw new InvalidOperationException("Nenhuma rede VPN configurada para o tenant.");
        }

        // Gerar chaves do túnel VPN
        var (publicKey, privateKey) = await GenerateVpnTunnelKeysAsync(cancellationToken);

        // Alocar IP na rede VPN
        var userIp = await AllocateUserVpnIpAsync(vpnNetwork, cancellationToken);

        // Atualizar usuário
        user.VpnPublicKey = publicKey;
        user.VpnPrivateKey = privateKey; // Armazenar chave privada (criptografar em produção)
        user.VpnIpAddress = userIp;
        if (string.IsNullOrEmpty(user.VpnDeviceName))
        {
            user.VpnDeviceName = $"Device-{user.Email}";
        }
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user, cancellationToken);

        // Adicionar peer no servidor VPN
        await AddPeerToVpnServerAsync(vpnNetwork, publicKey, userIp, cancellationToken);

        _logger?.LogInformation("Usuário {UserId} ({Email}) provisionado na VPN com IP {Ip}", 
            userId, user.Email, userIp);
    }

    private async Task<string> GenerateUserConfigContentAsync(
        TenantUser user,
        VpnNetwork vpnNetwork,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(user.VpnPublicKey) || string.IsNullOrEmpty(user.VpnIpAddress))
        {
            throw new InvalidOperationException("Usuário não está provisionado na VPN. Chaves ou IP não encontrados.");
        }

        var sb = new StringBuilder();
        
        sb.AppendLine("# Configuração VPN Automais.io");
        sb.AppendLine();
        sb.AppendLine($"# Usuário: {user.Name} ({user.Email})");
        sb.AppendLine($"# Gerado em: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine("[Interface]");
        
        // Usar chave privada armazenada
        if (string.IsNullOrEmpty(user.VpnPrivateKey))
        {
            throw new InvalidOperationException("Chave privada do usuário não encontrada. O usuário precisa ser reprovisionado.");
        }
        
        sb.AppendLine($"PrivateKey = {user.VpnPrivateKey}");
        sb.AppendLine($"Address = {user.VpnIpAddress}");
        sb.AppendLine();
        
        // Seção [Peer] - Configuração do servidor
        sb.AppendLine("[Peer]");
        var serverPublicKey = await GetServerPublicKeyAsync(vpnNetwork, cancellationToken);
        sb.AppendLine($"PublicKey = {serverPublicKey}");
        
        var endpoint = GetServerEndpoint(vpnNetwork);
        sb.AppendLine($"Endpoint = {endpoint}:51820");
        sb.AppendLine($"AllowedIPs = {vpnNetwork.Cidr}");
        sb.AppendLine("PersistentKeepalive = 25");

        return sb.ToString();
    }

    private async Task<(string publicKey, string privateKey)> GenerateVpnTunnelKeysAsync(CancellationToken cancellationToken)
    {
        // Gerar chave privada
        var privateKeyProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/wg",
                Arguments = "genkey",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        privateKeyProcess.Start();
        var privateKey = (await privateKeyProcess.StandardOutput.ReadToEndAsync(cancellationToken)).Trim();
        await privateKeyProcess.WaitForExitAsync(cancellationToken);

        if (privateKeyProcess.ExitCode != 0 || string.IsNullOrEmpty(privateKey))
        {
            throw new InvalidOperationException("Erro ao gerar chave privada do túnel VPN");
        }

        // Gerar chave pública a partir da privada
        var publicKeyProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/wg",
                Arguments = "pubkey",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        publicKeyProcess.Start();
        await publicKeyProcess.StandardInput.WriteAsync(privateKey);
        await publicKeyProcess.StandardInput.FlushAsync();
        publicKeyProcess.StandardInput.Close();
        
        var publicKey = (await publicKeyProcess.StandardOutput.ReadToEndAsync(cancellationToken)).Trim();
        await publicKeyProcess.WaitForExitAsync(cancellationToken);

        if (publicKeyProcess.ExitCode != 0 || string.IsNullOrEmpty(publicKey))
        {
            throw new InvalidOperationException("Erro ao gerar chave pública do túnel VPN");
        }

        return (publicKey, privateKey);
    }

    private async Task<string> AllocateUserVpnIpAsync(VpnNetwork vpnNetwork, CancellationToken cancellationToken)
    {
        // Extrair IP base e prefixo da rede
        var cidrParts = vpnNetwork.Cidr.Split('/');
        if (cidrParts.Length != 2 || !IPAddress.TryParse(cidrParts[0], out var baseIp))
        {
            throw new InvalidOperationException($"CIDR inválido: {vpnNetwork.Cidr}");
        }

        if (!int.TryParse(cidrParts[1], out var prefixLength) || prefixLength < 0 || prefixLength > 32)
        {
            throw new InvalidOperationException($"Prefixo inválido: {cidrParts[1]}");
        }

        // Buscar todos os IPs já alocados para usuários nesta rede
        var users = await _userRepository.GetByTenantIdAsync(vpnNetwork.TenantId, cancellationToken);
        var allocatedIps = users
            .Where(u => u.VpnEnabled && !string.IsNullOrEmpty(u.VpnIpAddress))
            .Select(u => u.VpnIpAddress!)
            .ToList();

        // Converter IP base para número
        var baseBytes = baseIp.GetAddressBytes();
        var baseNumber = BitConverter.ToUInt32(baseBytes.Reverse().ToArray(), 0);

        // Encontrar próximo IP disponível (começando do .2, pois .1 é do servidor)
        var hostBits = 32 - prefixLength;
        var maxHosts = (uint)Math.Pow(2, hostBits);
        
        for (uint i = 2; i < maxHosts && i < 255; i++)
        {
            var candidateNumber = baseNumber + i;
            var candidateBytes = BitConverter.GetBytes(candidateNumber).Reverse().ToArray();
            var candidateIp = new IPAddress(candidateBytes);
            var candidateIpString = candidateIp.ToString();

            if (!allocatedIps.Contains(candidateIpString))
            {
                return $"{candidateIpString}/{prefixLength}";
            }
        }

        throw new InvalidOperationException("Não há IPs disponíveis na rede VPN");
    }

    private async Task<string> GetServerPublicKeyAsync(VpnNetwork vpnNetwork, CancellationToken cancellationToken)
    {
        var interfaceName = GetInterfaceName(vpnNetwork.Id);
        
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/wg",
                    Arguments = $"show {interfaceName}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.TrimStart().StartsWith("public key:", StringComparison.OrdinalIgnoreCase))
                    {
                        var publicKey = line.Split(':')[1].Trim();
                        if (!string.IsNullOrEmpty(publicKey))
                        {
                            return publicKey;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Erro ao obter chave pública do servidor para interface {InterfaceName}", interfaceName);
        }

        // Fallback: usar chave pública da VpnNetwork se disponível
        if (!string.IsNullOrEmpty(vpnNetwork.ServerPublicKey))
        {
            return vpnNetwork.ServerPublicKey;
        }

        throw new InvalidOperationException($"Chave pública do servidor não encontrada para a rede VPN {vpnNetwork.Id}");
    }

    private string GetServerEndpoint(VpnNetwork vpnNetwork)
    {
        if (!string.IsNullOrEmpty(vpnNetwork.ServerEndpoint))
        {
            return vpnNetwork.ServerEndpoint;
        }

        // Fallback: usar endpoint padrão
        return "vpn.automais.io";
    }

    private string GetInterfaceName(Guid vpnNetworkId)
    {
        return $"wg-{vpnNetworkId.ToString("N")[..8]}";
    }

    private async Task AddPeerToVpnServerAsync(
        VpnNetwork vpnNetwork,
        string publicKey,
        string allowedIp,
        CancellationToken cancellationToken)
    {
        var interfaceName = GetInterfaceName(vpnNetwork.Id);
        
        try
        {
            // Adicionar peer usando wg set
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/wg",
                    Arguments = $"set {interfaceName} peer {publicKey} allowed-ips {allowedIp}",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                _logger?.LogWarning("Erro ao adicionar peer ao servidor VPN: {Error}", error);
                // Não lançar exceção - pode ser que o peer já exista
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Erro ao adicionar peer ao servidor VPN");
            // Não lançar exceção - tentativa de adicionar peer
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = fileName;
        foreach (var c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '_');
        }
        return sanitized;
    }

    private static string? ExtractGatewayIp(string cidr)
    {
        try
        {
            var parts = cidr.Split('/');
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var baseIp))
            {
                return null;
            }

            var bytes = baseIp.GetAddressBytes();
            // Gateway é geralmente o primeiro IP da rede (x.x.x.1)
            bytes[3] = 1;
            return new IPAddress(bytes).ToString();
        }
        catch
        {
            return null;
        }
    }
}

