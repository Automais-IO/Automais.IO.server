namespace Automais.Core.Entities;

/// <summary>
/// Redes que o peer pode alcançar via túnel (lado cliente: AllowedIPs além da rede VPN).
/// </summary>
public class AllowedNetwork
{
    public Guid Id { get; set; }

    public Guid VpnPeerId { get; set; }

    public string NetworkCidr { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public VpnPeer VpnPeer { get; set; } = null!;
}
