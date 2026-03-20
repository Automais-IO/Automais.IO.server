using Automais.Core.DTOs;
using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Automais.Core.Services;

/// <summary>
/// Redes que o peer pode alcançar via túnel (lado cliente — AllowedIPs além da rede VPN).
/// </summary>
public class AllowedNetworkService : IAllowedNetworkService
{
    private static readonly Regex CidrRegex = new(@"^[\d.]+(/\d+)?$", RegexOptions.Compiled);

    private readonly IAllowedNetworkRepository _allowedNetworkRepository;
    private readonly IRouterRepository _routerRepository;
    private readonly ILogger<AllowedNetworkService>? _logger;

    public AllowedNetworkService(
        IAllowedNetworkRepository allowedNetworkRepository,
        IRouterRepository routerRepository,
        ILogger<AllowedNetworkService>? logger = null)
    {
        _allowedNetworkRepository = allowedNetworkRepository;
        _routerRepository = routerRepository;
        _logger = logger;
    }

    public async Task<IEnumerable<AllowedNetworkDto>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default)
    {
        var list = await _allowedNetworkRepository.GetByRouterIdAsync(routerId, cancellationToken);
        return list.Select(n => MapToDto(n, routerId));
    }

    public async Task<AllowedNetworkDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _allowedNetworkRepository.GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return null;
        var router = await _routerRepository.GetByVpnPeerIdAsync(entity.VpnPeerId, cancellationToken);
        if (router == null)
            return null;
        return MapToDto(entity, router.Id);
    }

    public async Task<AllowedNetworkDto> CreateAsync(Guid routerId, CreateAllowedNetworkDto dto, CancellationToken cancellationToken = default)
    {
        var router = await _routerRepository.GetByIdAsync(routerId, cancellationToken);
        if (router == null)
            throw new KeyNotFoundException($"Router com ID {routerId} não encontrado.");
        if (!router.VpnPeerId.HasValue)
            throw new InvalidOperationException("Router não possui peer VPN; associe um peer antes de configurar redes permitidas.");

        ValidateCidr(dto.NetworkCidr);
        var cidr = dto.NetworkCidr.Trim();

        var existing = await _allowedNetworkRepository.GetByRouterIdAndCidrAsync(routerId, cidr, cancellationToken);
        if (existing != null)
            throw new InvalidOperationException($"Já existe a rede {cidr} para este peer.");

        var entity = new AllowedNetwork
        {
            Id = Guid.NewGuid(),
            VpnPeerId = router.VpnPeerId.Value,
            NetworkCidr = cidr,
            Description = dto.Description?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        var created = await _allowedNetworkRepository.CreateAsync(entity, cancellationToken);
        _logger?.LogInformation("AllowedNetwork {Id} criada para peer {PeerId} (router {RouterId}).", created.Id, created.VpnPeerId, routerId);

        return MapToDto(created, routerId);
    }

    public async Task<AllowedNetworkDto> UpdateAsync(Guid id, UpdateAllowedNetworkDto dto, CancellationToken cancellationToken = default)
    {
        var entity = await _allowedNetworkRepository.GetByIdAsync(id, cancellationToken);
        if (entity == null)
            throw new KeyNotFoundException($"Rede com ID {id} não encontrada.");

        var router = await _routerRepository.GetByVpnPeerIdAsync(entity.VpnPeerId, cancellationToken)
            ?? throw new InvalidOperationException("Não foi possível localizar o router deste peer.");

        if (!string.IsNullOrWhiteSpace(dto.NetworkCidr))
        {
            ValidateCidr(dto.NetworkCidr);
            var cidr = dto.NetworkCidr.Trim();
            if (cidr != entity.NetworkCidr)
            {
                var existing = await _allowedNetworkRepository.GetByRouterIdAndCidrAsync(router.Id, cidr, cancellationToken);
                if (existing != null && existing.Id != id)
                    throw new InvalidOperationException($"Já existe a rede {cidr} para este peer.");
                entity.NetworkCidr = cidr;
            }
        }

        if (dto.Description != null)
            entity.Description = dto.Description.Trim();

        var updated = await _allowedNetworkRepository.UpdateAsync(entity, cancellationToken);
        return MapToDto(updated, router.Id);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _allowedNetworkRepository.DeleteAsync(id, cancellationToken);
    }

    private static void ValidateCidr(string? cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr))
            throw new InvalidOperationException("CIDR é obrigatório.");
        if (!CidrRegex.IsMatch(cidr.Trim()))
            throw new InvalidOperationException("CIDR deve estar no formato válido (ex: 192.168.1.0/24).");
    }

    private static AllowedNetworkDto MapToDto(AllowedNetwork n, Guid routerId) => new()
    {
        Id = n.Id,
        RouterId = routerId,
        VpnPeerId = n.VpnPeerId,
        NetworkCidr = n.NetworkCidr,
        Description = n.Description,
        CreatedAt = n.CreatedAt
    };
}
