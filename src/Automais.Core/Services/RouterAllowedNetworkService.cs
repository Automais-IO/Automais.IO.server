using Automais.Core.DTOs;
using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Automais.Core.Services;

/// <summary>
/// Serviço para gerenciamento de redes destino dos routers.
/// Ao criar/atualizar/deletar uma rede destino, o PeerIp do(s) peer(s) WireGuard do router é recalculado.
/// </summary>
public class RouterAllowedNetworkService : IRouterAllowedNetworkService
{
    private static readonly Regex CidrRegex = new(@"^[\d.]+(/\d+)?$", RegexOptions.Compiled);

    private readonly IRouterAllowedNetworkRepository _allowedNetworkRepository;
    private readonly IRouterRepository _routerRepository;
    private readonly IRouterWireGuardPeerRepository _peerRepository;
    private readonly ILogger<RouterAllowedNetworkService>? _logger;

    public RouterAllowedNetworkService(
        IRouterAllowedNetworkRepository allowedNetworkRepository,
        IRouterRepository routerRepository,
        IRouterWireGuardPeerRepository peerRepository,
        ILogger<RouterAllowedNetworkService>? logger = null)
    {
        _allowedNetworkRepository = allowedNetworkRepository;
        _routerRepository = routerRepository;
        _peerRepository = peerRepository;
        _logger = logger;
    }

    public async Task<IEnumerable<RouterAllowedNetworkDto>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default)
    {
        var list = await _allowedNetworkRepository.GetByRouterIdAsync(routerId, cancellationToken);
        return list.Select(MapToDto);
    }

    public async Task<RouterAllowedNetworkDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _allowedNetworkRepository.GetByIdAsync(id, cancellationToken);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<RouterAllowedNetworkDto> CreateAsync(Guid routerId, CreateRouterAllowedNetworkDto dto, CancellationToken cancellationToken = default)
    {
        var router = await _routerRepository.GetByIdAsync(routerId, cancellationToken);
        if (router == null)
            throw new KeyNotFoundException($"Router com ID {routerId} não encontrado.");

        ValidateCidr(dto.NetworkCidr);
        var cidr = dto.NetworkCidr.Trim();

        var existing = await _allowedNetworkRepository.GetByRouterIdAndCidrAsync(routerId, cidr, cancellationToken);
        if (existing != null)
            throw new InvalidOperationException($"Já existe uma rede destino {cidr} para este router.");

        var entity = new RouterAllowedNetwork
        {
            Id = Guid.NewGuid(),
            RouterId = routerId,
            NetworkCidr = cidr,
            Description = dto.Description?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        var created = await _allowedNetworkRepository.CreateAsync(entity, cancellationToken);
        await RefreshPeerIpForRouterAsync(routerId, cancellationToken);

        return MapToDto(created);
    }

    public async Task<RouterAllowedNetworkDto> UpdateAsync(Guid id, UpdateRouterAllowedNetworkDto dto, CancellationToken cancellationToken = default)
    {
        var entity = await _allowedNetworkRepository.GetByIdAsync(id, cancellationToken);
        if (entity == null)
            throw new KeyNotFoundException($"Rede destino com ID {id} não encontrada.");

        if (!string.IsNullOrWhiteSpace(dto.NetworkCidr))
        {
            ValidateCidr(dto.NetworkCidr);
            var cidr = dto.NetworkCidr.Trim();
            if (cidr != entity.NetworkCidr)
            {
                var existing = await _allowedNetworkRepository.GetByRouterIdAndCidrAsync(entity.RouterId, cidr, cancellationToken);
                if (existing != null && existing.Id != id)
                    throw new InvalidOperationException($"Já existe uma rede destino {cidr} para este router.");
                entity.NetworkCidr = cidr;
            }
        }

        if (dto.Description != null)
            entity.Description = dto.Description.Trim();

        var updated = await _allowedNetworkRepository.UpdateAsync(entity, cancellationToken);
        await RefreshPeerIpForRouterAsync(entity.RouterId, cancellationToken);

        return MapToDto(updated);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _allowedNetworkRepository.GetByIdAsync(id, cancellationToken);
        if (entity == null)
            return;

        var routerId = entity.RouterId;
        await _allowedNetworkRepository.DeleteAsync(id, cancellationToken);
        await RefreshPeerIpForRouterAsync(routerId, cancellationToken);
    }

    /// <summary>
    /// Recalcula e atualiza o PeerIp de todos os peers WireGuard do router com a lista atual de redes destino.
    /// </summary>
    private async Task RefreshPeerIpForRouterAsync(Guid routerId, CancellationToken cancellationToken)
    {
        var peers = await _peerRepository.GetByRouterIdAsync(routerId, cancellationToken);
        if (peers == null || !peers.Any())
            return;

        var networks = await _allowedNetworkRepository.GetByRouterIdAsync(routerId, cancellationToken);
        var destinationCidrs = networks.OrderBy(n => n.NetworkCidr).Select(n => n.NetworkCidr).ToList();

        foreach (var peer in peers)
        {
            if (string.IsNullOrWhiteSpace(peer.PeerIp))
                continue;

            var parts = peer.PeerIp.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var routerIpPart = parts.Length > 0 ? parts[0].Trim() : string.Empty;
            if (string.IsNullOrEmpty(routerIpPart))
                continue;

            var newPeerIp = destinationCidrs.Count > 0
                ? routerIpPart + "," + string.Join(",", destinationCidrs)
                : routerIpPart;

            if (peer.PeerIp == newPeerIp)
                continue;

            peer.PeerIp = newPeerIp;
            peer.UpdatedAt = DateTime.UtcNow;
            await _peerRepository.UpdateAsync(peer, cancellationToken);
            _logger?.LogInformation("Peer {PeerId} do router {RouterId}: PeerIp atualizado para refletir {Count} rede(s) destino.", peer.Id, routerId, destinationCidrs.Count);
        }
    }

    private static void ValidateCidr(string? cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr))
            throw new InvalidOperationException("CIDR da rede destino é obrigatório.");
        if (!CidrRegex.IsMatch(cidr.Trim()))
            throw new InvalidOperationException("CIDR deve estar no formato válido (ex: 192.168.1.0/24).");
    }

    private static RouterAllowedNetworkDto MapToDto(RouterAllowedNetwork n) => new()
    {
        Id = n.Id,
        RouterId = n.RouterId,
        NetworkCidr = n.NetworkCidr,
        Description = n.Description,
        CreatedAt = n.CreatedAt
    };
}
