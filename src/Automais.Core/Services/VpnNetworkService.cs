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

    public VpnNetworkService(
        ITenantRepository tenantRepository,
        IVpnNetworkRepository vpnNetworkRepository,
        IDeviceRepository deviceRepository,
        ITenantUserService tenantUserService,
        IOptions<WireGuardSettings> wireGuardSettings,
        IVpnServiceClient? vpnServiceClient = null)
    {
        _tenantRepository = tenantRepository;
        _vpnNetworkRepository = vpnNetworkRepository;
        _deviceRepository = deviceRepository;
        _tenantUserService = tenantUserService;
        _wireGuardSettings = wireGuardSettings.Value;
        _vpnServiceClient = vpnServiceClient;
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
            // ServerEndpoint: salva o valor que vier do frontend
            // Se não vier nada, usa o valor padrão da configuração como fallback de segurança
            // (mas o ideal é que o frontend sempre envie o valor)
            ServerEndpoint = !string.IsNullOrWhiteSpace(dto.ServerEndpoint) 
                ? dto.ServerEndpoint 
                : _wireGuardSettings.DefaultServerEndpoint,
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

        network.UpdatedAt = DateTime.UtcNow;

        var updated = await _vpnNetworkRepository.UpdateAsync(network, cancellationToken);
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
            UserCount = userCount,
            DeviceCount = deviceCount,
            CreatedAt = network.CreatedAt,
            UpdatedAt = network.UpdatedAt
        };
    }
}


