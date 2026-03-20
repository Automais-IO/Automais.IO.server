namespace Automais.Core.Entities;

/// <summary>
/// Faixa de rede por trás do peer (LAN no cliente). Usada no servidor VPN (AllowedIPs do peer) e em iptables.
/// </summary>
public class RemoteNetwork
{
    public Guid Id { get; set; }

    public Guid VpnPeerId { get; set; }

    public string NetworkCidr { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public VpnPeer VpnPeer { get; set; } = null!;
}
