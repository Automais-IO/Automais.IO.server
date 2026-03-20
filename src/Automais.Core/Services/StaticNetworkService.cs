using Automais.Core.DTOs;
using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.RegularExpressions;

namespace Automais.Core.Services;

/// <summary>
/// Rotas estáticas no peer (RouterOS / host). Persistência em <c>static_networks</c> (VpnPeerId).
/// </summary>
public class StaticNetworkService : IStaticNetworkService
{
    private readonly IStaticNetworkRepository _routeRepository;
    private readonly IRouterRepository _routerRepository;
    private readonly ILogger<StaticNetworkService>? _logger;

    public StaticNetworkService(
        IStaticNetworkRepository routeRepository,
        IRouterRepository routerRepository,
        ILogger<StaticNetworkService>? logger = null)
    {
        _routeRepository = routeRepository;
        _routerRepository = routerRepository;
        _logger = logger;
    }

    public async Task<IEnumerable<StaticNetworkDto>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default)
    {
        var routes = await _routeRepository.GetByRouterIdAsync(routerId, cancellationToken);
        var list = new List<StaticNetworkDto>();
        foreach (var r in routes)
            list.Add(await MapToDtoAsync(r, cancellationToken));
        return list;
    }

    public async Task<StaticNetworkDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var route = await _routeRepository.GetByIdAsync(id, cancellationToken);
        return route == null ? null : await MapToDtoAsync(route, cancellationToken);
    }

    public async Task<StaticNetworkDto> CreateAsync(Guid routerId, CreateStaticNetworkDto dto, CancellationToken cancellationToken = default)
    {
        var router = await _routerRepository.GetByIdAsync(routerId, cancellationToken);
        if (router == null)
            throw new KeyNotFoundException($"Router com ID {routerId} não encontrado.");
        if (!router.VpnPeerId.HasValue)
            throw new InvalidOperationException("Router não possui peer VPN.");

        ValidateRoute(dto);

        var existing = await _routeRepository.GetByRouterIdAndDestinationAsync(routerId, dto.Destination, cancellationToken);
        if (existing != null)
            throw new InvalidOperationException($"Já existe rota com destino {dto.Destination} para este peer.");

        var routeId = Guid.NewGuid();
        var route = new StaticNetwork
        {
            Id = routeId,
            VpnPeerId = router.VpnPeerId.Value,
            Destination = dto.Destination.Trim(),
            Gateway = dto.Gateway.Trim(),
            Interface = dto.Interface?.Trim(),
            Distance = dto.Distance,
            Scope = dto.Scope,
            RoutingTable = dto.RoutingTable?.Trim() ?? "main",
            Description = dto.Description?.Trim(),
            Comment = $"AUTOMAIS.IO NÃO APAGAR: {routeId}",
            Status = StaticNetworkStatus.PendingAdd,
            IsActive = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await _routeRepository.CreateAsync(route, cancellationToken);
        return await MapToDtoAsync(created, cancellationToken);
    }

    public async Task<StaticNetworkDto> UpdateAsync(Guid id, UpdateStaticNetworkDto dto, CancellationToken cancellationToken = default)
    {
        var route = await _routeRepository.GetByIdAsync(id, cancellationToken);
        if (route == null)
            throw new KeyNotFoundException($"Rota estática com ID {id} não encontrada.");

        if (!string.IsNullOrWhiteSpace(dto.Destination) && dto.Destination != route.Destination)
        {
            var router = await _routerRepository.GetByVpnPeerIdAsync(route.VpnPeerId, cancellationToken);
            if (router != null)
            {
                var existing = await _routeRepository.GetByRouterIdAndDestinationAsync(router.Id, dto.Destination, cancellationToken);
                if (existing != null && existing.Id != id)
                    throw new InvalidOperationException($"Já existe rota com destino {dto.Destination} para este peer.");
            }
        }

        if (!string.IsNullOrWhiteSpace(dto.Destination))
            route.Destination = dto.Destination;
        if (!string.IsNullOrWhiteSpace(dto.Gateway))
            route.Gateway = dto.Gateway;
        if (dto.Interface != null)
            route.Interface = dto.Interface;
        if (dto.Distance.HasValue)
            route.Distance = dto.Distance;
        if (dto.Scope.HasValue)
            route.Scope = dto.Scope;
        if (dto.RoutingTable != null)
            route.RoutingTable = dto.RoutingTable;
        if (dto.Description != null)
            route.Description = dto.Description;

        route.UpdatedAt = DateTime.UtcNow;

        var updated = await _routeRepository.UpdateAsync(route, cancellationToken);
        return await MapToDtoAsync(updated, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _routeRepository.DeleteAsync(id, cancellationToken);
    }

    public async Task BatchUpdateStatusAsync(Guid routerId, BatchUpdateStaticNetworksDto dto, CancellationToken cancellationToken = default)
    {
        var router = await _routerRepository.GetByIdAsync(routerId, cancellationToken);
        var peerId = router?.VpnPeerId;

        foreach (var routeId in dto.StaticNetworkIdsToAdd)
        {
            var route = await _routeRepository.GetByIdAsync(routeId, cancellationToken);
            if (route != null && peerId.HasValue && route.VpnPeerId == peerId.Value)
            {
                route.Status = StaticNetworkStatus.PendingAdd;
                route.ErrorMessage = null;
                route.UpdatedAt = DateTime.UtcNow;
                await _routeRepository.UpdateAsync(route, cancellationToken);
            }
        }

        foreach (var routeId in dto.StaticNetworkIdsToRemove)
        {
            var route = await _routeRepository.GetByIdAsync(routeId, cancellationToken);
            if (route != null && peerId.HasValue && route.VpnPeerId == peerId.Value)
            {
                route.Status = StaticNetworkStatus.PendingRemove;
                route.ErrorMessage = null;
                route.UpdatedAt = DateTime.UtcNow;
                await _routeRepository.UpdateAsync(route, cancellationToken);
            }
        }
    }

    public async Task UpdateStaticNetworkStatusAsync(UpdateStaticNetworkStatusDto dto, CancellationToken cancellationToken = default)
    {
        var route = await _routeRepository.GetByIdAsync(dto.StaticNetworkId, cancellationToken);
        if (route == null)
            throw new KeyNotFoundException($"Rede estática com ID {dto.StaticNetworkId} não encontrada.");

        route.Status = dto.Status;
        if (dto.RouterOsId != null)
            route.RouterOsId = dto.RouterOsId;
        route.ErrorMessage = dto.ErrorMessage;

        if (dto.Gateway != null)
            route.Gateway = dto.Gateway;
        else
            _logger?.LogWarning("Gateway não atualizado: StaticNetworkId={StaticNetworkId}, Gateway no DTO é null", dto.StaticNetworkId);

        route.UpdatedAt = DateTime.UtcNow;

        if (dto.Status == StaticNetworkStatus.Applied)
            route.IsActive = true;

        await _routeRepository.UpdateAsync(route, cancellationToken);
    }

    private async Task<StaticNetworkDto> MapToDtoAsync(StaticNetwork route, CancellationToken cancellationToken)
    {
        var router = await _routerRepository.GetByVpnPeerIdAsync(route.VpnPeerId, cancellationToken);
        return new StaticNetworkDto
        {
            Id = route.Id,
            RouterId = router?.Id ?? Guid.Empty,
            VpnPeerId = route.VpnPeerId,
            Destination = route.Destination,
            Gateway = route.Gateway,
            Interface = route.Interface,
            Distance = route.Distance,
            Scope = route.Scope,
            RoutingTable = route.RoutingTable,
            Description = route.Description,
            Comment = route.Comment,
            Status = route.Status,
            IsActive = route.IsActive,
            RouterOsId = route.RouterOsId,
            ErrorMessage = route.ErrorMessage,
            CreatedAt = route.CreatedAt,
            UpdatedAt = route.UpdatedAt
        };
    }

    private static void ValidateRoute(CreateStaticNetworkDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Destination))
            throw new InvalidOperationException("Destination é obrigatório.");

        if (!IsValidIpOrCidr(dto.Destination))
            throw new InvalidOperationException($"Destination inválido: {dto.Destination}. Use formato IP/CIDR.");

        if (!string.IsNullOrWhiteSpace(dto.Gateway))
        {
            if (Regex.IsMatch(dto.Gateway, @"^\d+\.\d+\.\d+\.\d+$") && !IsValidIp(dto.Gateway))
                throw new InvalidOperationException($"Gateway inválido: {dto.Gateway}.");
        }

        if (dto.Distance.HasValue && (dto.Distance < 0 || dto.Distance > 255))
            throw new InvalidOperationException("Distance deve estar entre 0 e 255.");

        if (dto.Scope.HasValue && (dto.Scope < 0 || dto.Scope > 255))
            throw new InvalidOperationException("Scope deve estar entre 0 e 255.");
    }

    private static bool IsValidIpOrCidr(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;
        if (input.Contains('/'))
        {
            var parts = input.Split('/');
            if (parts.Length != 2)
                return false;
            if (!IPAddress.TryParse(parts[0], out _))
                return false;
            if (!int.TryParse(parts[1], out var prefix) || prefix < 0 || prefix > 32)
                return false;
            return true;
        }
        return IPAddress.TryParse(input, out _);
    }

    private static bool IsValidIp(string input)
    {
        return !string.IsNullOrWhiteSpace(input) && IPAddress.TryParse(input, out _);
    }
}
