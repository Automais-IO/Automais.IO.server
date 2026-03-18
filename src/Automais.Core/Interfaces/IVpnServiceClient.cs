using Automais.Core.DTOs;

namespace Automais.Core.Interfaces;

/// <summary>
/// Resultado do provisionamento de peer
/// </summary>
public class ProvisionPeerResult
{
    public string PublicKey { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
    /// <summary>IP do peer na VPN (ex: 10.100.1.50/32).</summary>
    public string PeerIp { get; set; } = string.Empty;
}

/// <summary>
/// Cliente HTTP para comunicação com o serviço VPN Python
/// </summary>
public interface IVpnServiceClient
{
    /// <summary>
    /// Provisiona um peer WireGuard para um router
    /// </summary>
    Task<ProvisionPeerResult> ProvisionPeerAsync(
        Guid routerId,
        Guid vpnNetworkId,
        IEnumerable<string> allowedNetworks,
        string? manualIp = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtém a configuração WireGuard para um router
    /// </summary>
    Task<RouterWireGuardConfigDto> GetConfigAsync(
        Guid routerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adiciona uma rede permitida ao router
    /// </summary>
    Task AddNetworkToRouterAsync(
        Guid routerId,
        string networkCidr,
        string? description = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove uma rede permitida do router
    /// </summary>
    Task RemoveNetworkFromRouterAsync(
        Guid routerId,
        string networkCidr,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Garante que a interface WireGuard existe para uma VpnNetwork
    /// </summary>
    Task EnsureInterfaceAsync(
        Guid vpnNetworkId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a interface WireGuard de uma VpnNetwork
    /// </summary>
    Task RemoveInterfaceAsync(
        Guid vpnNetworkId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtém a chave pública do servidor WireGuard para uma VpnNetwork (da interface ativa no VPN server).
    /// Retorna null se a interface não existir ou não estiver ativa.
    /// </summary>
    Task<string?> GetServerPublicKeyAsync(
        Guid vpnNetworkId,
        CancellationToken cancellationToken = default);
}

