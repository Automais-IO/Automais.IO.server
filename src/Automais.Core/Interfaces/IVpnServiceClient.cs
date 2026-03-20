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
/// <remarks>
/// Agente de IA: o serviço Python implementa o túnel com <strong>WireGuard</strong> (provisionamento, .conf, chaves).
/// Contratos HTTP usam nomes "vpn" por convenção de produto.
/// </remarks>
public interface IVpnServiceClient
{
    /// <summary>Provisiona um peer VPN para um router.</summary>
    Task<ProvisionPeerResult> ProvisionPeerAsync(
        Guid routerId,
        Guid vpnNetworkId,
        IEnumerable<string> allowedNetworks,
        string? manualIp = null,
        CancellationToken cancellationToken = default);

    /// <summary>Obtém a configuração do cliente VPN para um router.</summary>
    Task<VpnPeerConfigDto> GetConfigAsync(
        Guid routerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adiciona uma rede destino ao router (encaminhamento via este tunnel).
    /// </summary>
    Task AddNetworkToRouterAsync(
        Guid routerId,
        string networkCidr,
        string? description = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove uma rede destino do router.
    /// </summary>
    Task RemoveNetworkFromRouterAsync(
        Guid routerId,
        string networkCidr,
        CancellationToken cancellationToken = default);

    /// <summary>Garante que a interface VPN existe no servidor para uma VpnNetwork.</summary>
    Task EnsureInterfaceAsync(
        Guid vpnNetworkId,
        CancellationToken cancellationToken = default);

    /// <summary>Remove a interface VPN no servidor para uma VpnNetwork.</summary>
    Task RemoveInterfaceAsync(
        Guid vpnNetworkId,
        CancellationToken cancellationToken = default);

    /// <summary>Obtém a chave pública do servidor VPN para uma VpnNetwork (interface ativa no servidor).
    /// Retorna null se a interface não existir ou não estiver ativa.</summary>
    Task<string?> GetServerPublicKeyAsync(
        Guid vpnNetworkId,
        CancellationToken cancellationToken = default);
}

