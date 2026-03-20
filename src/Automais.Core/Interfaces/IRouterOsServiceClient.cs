using Automais.Core.DTOs;

namespace Automais.Core.Interfaces;

/// <summary>
/// Cliente HTTP para comunicação com o serviço RouterOS Python
/// </summary>
public interface IRouterOsServiceClient
{
    /// <summary>
    /// Adiciona rota estática no RouterOS
    /// Retorna tupla (sucesso, gateway_usado) onde gateway_usado pode ser diferente do fornecido
    /// se o gateway estava vazio e RouterOS usou a interface como gateway
    /// </summary>
    Task<(bool Success, string? GatewayUsed)> AddRouteAsync(
        Guid routerId,
        RouterStaticRouteDto route,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove rota estática do RouterOS
    /// </summary>
    Task<bool> RemoveRouteAsync(
        Guid routerId,
        string routerOsRouteId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista interfaces de túnel VPN no RouterOS
    /// </summary>
    Task<List<RouterOsVpnTunnelInterfaceDto>> ListVpnTunnelInterfacesAsync(
        Guid routerId,
        CancellationToken cancellationToken = default);
}
