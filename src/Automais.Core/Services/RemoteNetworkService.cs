using Automais.Core.DTOs;
using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Automais.Core.Services;

/// <summary>
/// LAN por trás do peer (servidor VPN: AllowedIPs do peer; iptables).
/// </summary>
public class RemoteNetworkService : IRemoteNetworkService
{
    private static readonly Regex CidrRegex = new(@"^[\d.]+(/\d+)?$", RegexOptions.Compiled);

    private readonly IRemoteNetworkRepository _repository;
    private readonly IRouterRepository _routerRepository;
    private readonly ILogger<RemoteNetworkService>? _logger;

    public RemoteNetworkService(
        IRemoteNetworkRepository repository,
        IRouterRepository routerRepository,
        ILogger<RemoteNetworkService>? logger = null)
    {
        _repository = repository;
        _routerRepository = routerRepository;
        _logger = logger;
    }

    public async Task<IEnumerable<RemoteNetworkDto>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default)
    {
        var list = await _repository.GetByRouterIdAsync(routerId, cancellationToken);
        return list.Select(n => MapToDto(n, routerId));
    }

    public async Task<RemoteNetworkDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return null;
        var router = await _routerRepository.GetByVpnPeerIdAsync(entity.VpnPeerId, cancellationToken);
        if (router == null)
            return null;
        return MapToDto(entity, router.Id);
    }

    public async Task<RemoteNetworkDto> CreateAsync(Guid routerId, CreateRemoteNetworkDto dto, CancellationToken cancellationToken = default)
    {
        var router = await _routerRepository.GetByIdAsync(routerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Router com ID {routerId} não encontrado.");
        if (!router.VpnPeerId.HasValue)
            throw new InvalidOperationException("Router não possui peer VPN.");

        ValidateCidr(dto.NetworkCidr);
        var cidr = dto.NetworkCidr.Trim();

        var existing = await _repository.GetByRouterIdAndCidrAsync(routerId, cidr, cancellationToken);
        if (existing != null)
            throw new InvalidOperationException($"Já existe a rede remota {cidr} para este peer.");

        var entity = new RemoteNetwork
        {
            Id = Guid.NewGuid(),
            VpnPeerId = router.VpnPeerId.Value,
            NetworkCidr = cidr,
            Description = dto.Description?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        var created = await _repository.CreateAsync(entity, cancellationToken);
        _logger?.LogInformation("RemoteNetwork {Id} para peer {PeerId} (router {RouterId}).", created.Id, created.VpnPeerId, routerId);

        return MapToDto(created, routerId);
    }

    public async Task<RemoteNetworkDto> UpdateAsync(Guid id, UpdateRemoteNetworkDto dto, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Rede remota {id} não encontrada.");

        var router = await _routerRepository.GetByVpnPeerIdAsync(entity.VpnPeerId, cancellationToken)
            ?? throw new InvalidOperationException("Não foi possível localizar o router deste peer.");

        if (!string.IsNullOrWhiteSpace(dto.NetworkCidr))
        {
            ValidateCidr(dto.NetworkCidr);
            var cidr = dto.NetworkCidr.Trim();
            if (cidr != entity.NetworkCidr)
            {
                var existing = await _repository.GetByRouterIdAndCidrAsync(router.Id, cidr, cancellationToken);
                if (existing != null && existing.Id != id)
                    throw new InvalidOperationException($"Já existe a rede remota {cidr} para este peer.");
                entity.NetworkCidr = cidr;
            }
        }

        if (dto.Description != null)
            entity.Description = dto.Description.Trim();

        var updated = await _repository.UpdateAsync(entity, cancellationToken);
        return MapToDto(updated, router.Id);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _repository.DeleteAsync(id, cancellationToken);
    }

    private static void ValidateCidr(string? cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr))
            throw new InvalidOperationException("CIDR é obrigatório.");
        if (!CidrRegex.IsMatch(cidr.Trim()))
            throw new InvalidOperationException("CIDR inválido (ex: 192.168.1.0/24).");
    }

    private static RemoteNetworkDto MapToDto(RemoteNetwork n, Guid routerId) => new()
    {
        Id = n.Id,
        RouterId = routerId,
        VpnPeerId = n.VpnPeerId,
        NetworkCidr = n.NetworkCidr,
        Description = n.Description,
        CreatedAt = n.CreatedAt
    };
}
