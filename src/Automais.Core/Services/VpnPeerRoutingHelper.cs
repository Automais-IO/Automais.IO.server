namespace Automais.Core.Services;

/// <summary>
/// Monta listas CIDR para WireGuard (servidor vs cliente) a partir de <see cref="Entities.VpnPeer.PeerIp"/> e tabelas por peer.
/// </summary>
public static class VpnPeerRoutingHelper
{
    public static string TunnelCidrFromPeerIp(string peerIpField)
    {
        var first = peerIpField.Split(',')[0].Trim();
        return first.Contains('/') ? first : $"{first}/32";
    }

    /// <summary>AllowedIPs no servidor: IP do túnel + redes remotas (LAN) + sufixos legados em PeerIp.</summary>
    public static string ComposeServerAllowedIps(string peerIpField, IEnumerable<string> remoteNetworkCidrs)
    {
        var tunnel = TunnelCidrFromPeerIp(peerIpField);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        void Add(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            var t = s.Trim();
            if (set.Add(t))
                ordered.Add(t);
        }

        Add(tunnel);
        foreach (var c in remoteNetworkCidrs)
            Add(c);
        foreach (var seg in peerIpField.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1))
            Add(seg);

        return string.Join(",", ordered);
    }

    /// <summary>AllowedIPs no cliente: rede VPN + redes permitidas (split tunnel) — sem LAN remota.</summary>
    public static string ComposeClientAllowedIps(string vpnNetworkCidr, IEnumerable<string> allowedNetworkCidrs, string peerIpField)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        void Add(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            var t = s.Trim();
            if (set.Add(t))
                ordered.Add(t);
        }

        Add(vpnNetworkCidr);
        foreach (var c in allowedNetworkCidrs)
            Add(c);

        return string.Join(",", ordered);
    }
}
