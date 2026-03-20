namespace Automais.Core.Entities;

/// <summary>
/// Linha na tabela <c>vpn_peers</c> (protocolo WireGuard no servidor VPN).
/// Vínculo com router: <see cref="Router.VpnPeerId"/>; com host: <see cref="Host.VpnPeerId"/>.
/// </summary>
public class VpnPeer
{
    public Guid Id { get; set; }

    public Guid VpnNetworkId { get; set; }

    public string PublicKey { get; set; } = string.Empty;

    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>IP do peer na VPN (ex.: "10.100.1.50/32" ou lista com redes destino).</summary>
    public string PeerIp { get; set; } = string.Empty;

    public string? Endpoint { get; set; }

    public DateTime? LastHandshake { get; set; }

    public long? BytesReceived { get; set; }

    public long? BytesSent { get; set; }

    public bool? PingSuccess { get; set; }

    public double? PingAvgTimeMs { get; set; }

    public double? PingPacketLoss { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public VpnNetwork VpnNetwork { get; set; } = null!;
}
