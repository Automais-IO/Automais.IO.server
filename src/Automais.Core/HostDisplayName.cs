using System.Linq;
using Automais.Core.Entities;

namespace Automais.Core;

/// <summary>
/// Nome amigável do host para API/UI. Evita mostrar chave WireGuard quando <see cref="Host.Name"/> foi preenchido por engano com (ou com prefixo de) <see cref="VpnPeer.PublicKey"/>.
/// </summary>
public static class HostDisplayName
{
    public static string ForUi(Host host, VpnPeer? peer)
    {
        var raw = (host.Name ?? string.Empty).Trim();
        var pub = peer?.PublicKey?.Trim();

        if (LooksLikePeerPublicKey(raw, pub))
        {
            if (!string.IsNullOrWhiteSpace(host.Description))
            {
                var line = host.Description
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Split('\n')
                    .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
                if (!string.IsNullOrWhiteSpace(line))
                    return line.Trim();
            }

            return string.IsNullOrWhiteSpace(host.VpnIp) ? "Host" : $"Host {host.VpnIp}";
        }

        if (string.IsNullOrEmpty(raw))
            return string.IsNullOrWhiteSpace(host.VpnIp) ? "Host" : $"Host {host.VpnIp}";

        return raw;
    }

    private static bool LooksLikePeerPublicKey(string raw, string? pub)
    {
        if (string.IsNullOrEmpty(raw))
            return false;

        if (!string.IsNullOrEmpty(pub))
        {
            if (string.Equals(raw, pub, StringComparison.Ordinal))
                return true;
            // Nome truncado ou colado só com o começo da chave
            if (raw.Length >= 8 && pub.StartsWith(raw, StringComparison.Ordinal))
                return true;
        }

        // Sem peer: sequência só com caracteres base64 e tamanho típico de chave WG (44 chars)
        return raw.Length is >= 43 and <= 44 && IsBase64UrlAlphabet(raw);
    }

    private static bool IsBase64UrlAlphabet(string s)
    {
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c))
                continue;
            if (c is '+' or '/' or '=')
                continue;
            return false;
        }
        return true;
    }
}
