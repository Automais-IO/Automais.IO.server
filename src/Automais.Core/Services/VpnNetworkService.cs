using Automais.Core.Configuration;
using Automais.Core.DTOs;
using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace Automais.Core.Services;

public class VpnNetworkService : IVpnNetworkService
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IVpnNetworkRepository _vpnNetworkRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly ITenantUserService _tenantUserService;
    private readonly WireGuardSettings _wireGuardSettings;
    private readonly IVpnServiceClient? _vpnServiceClient;
    private readonly IRouterWireGuardService? _routerWireGuardService;

    public VpnNetworkService(
        ITenantRepository tenantRepository,
        IVpnNetworkRepository vpnNetworkRepository,
        IDeviceRepository deviceRepository,
        ITenantUserService tenantUserService,
        IOptions<WireGuardSettings> wireGuardSettings,
        IVpnServiceClient? vpnServiceClient = null,
        IRouterWireGuardService? routerWireGuardService = null)
    {
        _tenantRepository = tenantRepository;
        _vpnNetworkRepository = vpnNetworkRepository;
        _deviceRepository = deviceRepository;
        _tenantUserService = tenantUserService;
        _wireGuardSettings = wireGuardSettings.Value;
        _vpnServiceClient = vpnServiceClient;
        _routerWireGuardService = routerWireGuardService;
    }

    public async Task<IEnumerable<VpnNetworkDto>> GetByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var networks = await _vpnNetworkRepository.GetByTenantIdAsync(tenantId, cancellationToken);
        var result = new List<VpnNetworkDto>();

        foreach (var network in networks)
        {
            result.Add(await MapToDtoAsync(network, cancellationToken));
        }

        return result;
    }

    public async Task<VpnNetworkDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var network = await _vpnNetworkRepository.GetByIdAsync(id, cancellationToken);
        return network == null ? null : await MapToDtoAsync(network, cancellationToken);
    }

    public async Task<VpnNetworkDto> CreateAsync(Guid tenantId, CreateVpnNetworkDto dto, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
        {
            throw new KeyNotFoundException($"Tenant com ID {tenantId} não encontrado.");
        }

        if (await _vpnNetworkRepository.SlugExistsAsync(tenantId, dto.Slug, cancellationToken))
        {
            throw new InvalidOperationException($"Slug '{dto.Slug}' já está em uso para este tenant.");
        }

        var serverEndpoint = !string.IsNullOrWhiteSpace(dto.ServerEndpoint)
            ? dto.ServerEndpoint.Trim()
            : _wireGuardSettings.DefaultServerEndpoint;

        int listenPort;
        if (dto.ListenPort.HasValue)
        {
            listenPort = ValidateListenPort(dto.ListenPort.Value);
            if (!await IsListenPortAvailableAsync(serverEndpoint, listenPort, null, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"A porta UDP {listenPort} já está em uso por outra rede VPN no mesmo servidor ({serverEndpoint}).");
            }
        }
        else
        {
            listenPort = await AllocateNextListenPortAsync(serverEndpoint, cancellationToken);
        }

        var network = new VpnNetwork
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = dto.Name,
            Slug = dto.Slug,
            Cidr = dto.Cidr,
            Description = dto.Description,
            IsDefault = dto.IsDefault,
            DnsServers = dto.DnsServers,
            ServerEndpoint = serverEndpoint,
            ListenPort = listenPort,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await _vpnNetworkRepository.CreateAsync(network, cancellationToken);
        
        // Garantir que a interface WireGuard seja criada e iniciada (via serviço Python)
        // O serviço Python fará isso automaticamente via auto-descoberta
        if (_vpnServiceClient != null)
        {
            try
            {
                await _vpnServiceClient.EnsureInterfaceAsync(created.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                // Logar erro mas não falhar criação da VPN
                // A interface pode ser criada depois automaticamente pelo serviço Python
                // TODO: Adicionar ILogger para logar este erro
            }
        }
        
        return await MapToDtoAsync(created, cancellationToken);
    }

    public async Task<VpnNetworkDto> UpdateAsync(Guid id, UpdateVpnNetworkDto dto, CancellationToken cancellationToken = default)
    {
        var network = await _vpnNetworkRepository.GetByIdAsync(id, cancellationToken);
        if (network == null)
        {
            throw new KeyNotFoundException($"Rede VPN com ID {id} não encontrada.");
        }

        if (!string.IsNullOrWhiteSpace(dto.Name))
        {
            network.Name = dto.Name;
        }

        if (dto.Description != null)
        {
            network.Description = dto.Description;
        }

        if (dto.IsDefault.HasValue)
        {
            network.IsDefault = dto.IsDefault.Value;
        }

        if (dto.DnsServers != null)
        {
            network.DnsServers = dto.DnsServers;
        }

        if (dto.ServerEndpoint != null)
        {
            network.ServerEndpoint = dto.ServerEndpoint;
        }

        if (dto.ServerPublicKey != null)
        {
            network.ServerPublicKey = dto.ServerPublicKey;
        }

        var listenPortChanged = false;
        if (dto.ListenPort.HasValue && dto.ListenPort.Value != network.ListenPort)
        {
            var newPort = ValidateListenPort(dto.ListenPort.Value);
            if (!await IsListenPortAvailableAsync(network.ServerEndpoint, newPort, network.Id, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"A porta UDP {newPort} já está em uso por outra rede VPN no mesmo servidor.");
            }

            network.ListenPort = newPort;
            listenPortChanged = true;
        }

        network.UpdatedAt = DateTime.UtcNow;

        var updated = await _vpnNetworkRepository.UpdateAsync(network, cancellationToken);
        if (listenPortChanged && _routerWireGuardService != null)
        {
            await _routerWireGuardService.RefreshPeerConfigsForNetworkAsync(network.Id, cancellationToken);
        }

        return await MapToDtoAsync(updated, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var network = await _vpnNetworkRepository.GetByIdAsync(id, cancellationToken);
        if (network == null)
        {
            return;
        }

        // Remover interface WireGuard antes de deletar do banco (via serviço Python)
        if (_vpnServiceClient != null)
        {
            try
            {
                await _vpnServiceClient.RemoveInterfaceAsync(id, cancellationToken);
            }
            catch (Exception ex)
            {
                // Logar erro mas continuar com a deleção do banco
                // A interface pode não existir ou já ter sido removida
                // TODO: Adicionar ILogger para logar este erro
            }
        }

        await _vpnNetworkRepository.DeleteAsync(id, cancellationToken);
    }

    public async Task<IEnumerable<TenantUserDto>> GetUsersAsync(Guid networkId, CancellationToken cancellationToken = default)
    {
        var network = await _vpnNetworkRepository.GetByIdAsync(networkId, cancellationToken);
        if (network == null)
        {
            throw new KeyNotFoundException($"Rede VPN com ID {networkId} não encontrada.");
        }

        var memberships = await _vpnNetworkRepository.GetMembershipsByNetworkIdAsync(networkId, cancellationToken);
        var result = new List<TenantUserDto>();

        foreach (var membership in memberships)
        {
            var user = await _tenantUserService.GetByIdAsync(membership.TenantUserId, cancellationToken);
            if (user != null)
            {
                result.Add(user);
            }
        }

        return result;
    }

    private async Task<VpnNetworkDto> MapToDtoAsync(VpnNetwork network, CancellationToken cancellationToken)
    {
        var userCount = await _vpnNetworkRepository.CountMembershipsByNetworkIdAsync(network.Id, cancellationToken);
        var deviceCount = await _deviceRepository.CountByNetworkIdAsync(network.Id, cancellationToken);

        return new VpnNetworkDto
        {
            Id = network.Id,
            TenantId = network.TenantId,
            Name = network.Name,
            Slug = network.Slug,
            Cidr = network.Cidr,
            Description = network.Description,
            IsDefault = network.IsDefault,
            DnsServers = network.DnsServers,
            ServerEndpoint = network.ServerEndpoint,
            ListenPort = network.ListenPort > 0 ? network.ListenPort : 51820,
            UserCount = userCount,
            DeviceCount = deviceCount,
            CreatedAt = network.CreatedAt,
            UpdatedAt = network.UpdatedAt
        };
    }

    private static int ValidateListenPort(int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new InvalidOperationException("ListenPort deve estar entre 1 e 65535.");
        }

        return port;
    }

    private async Task<bool> IsListenPortAvailableAsync(string? serverEndpoint, int port, Guid? excludeNetworkId, CancellationToken cancellationToken)
    {
        var used = await _vpnNetworkRepository.GetListenPortsForServerEndpointAsync(serverEndpoint, excludeNetworkId, cancellationToken);
        return !used.Contains(port);
    }

    private async Task<int> AllocateNextListenPortAsync(string? serverEndpoint, CancellationToken cancellationToken)
    {
        var used = new HashSet<int>(await _vpnNetworkRepository.GetListenPortsForServerEndpointAsync(serverEndpoint, null, cancellationToken));
        for (var p = 51820; p <= 65535; p++)
        {
            if (!used.Contains(p))
            {
                return p;
            }
        }

        throw new InvalidOperationException("Não há porta UDP livre (51820–65535) para este servidor VPN.");
    }
}


