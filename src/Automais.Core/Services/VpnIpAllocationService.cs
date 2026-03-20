using Automais.Core.Entities;
using Automais.Core.Interfaces;
using System.Net;

namespace Automais.Core.Services;

/// <summary>
/// Alocação de IP na VPN baseada nos <c>PeerIp</c> já usados em <c>vpn_peers</c> (sem tabela separada).
/// </summary>
public class VpnIpAllocationService : IVpnIpAllocationService
{
    private readonly IVpnPeerRepository _peerRepo;
    private readonly IVpnNetworkRepository _vpnNetworkRepo;

    public VpnIpAllocationService(
        IVpnPeerRepository peerRepo,
        IVpnNetworkRepository vpnNetworkRepo)
    {
        _peerRepo = peerRepo;
        _vpnNetworkRepo = vpnNetworkRepo;
    }

    public async Task<string> AllocateNextIpAsync(
        Guid vpnNetworkId,
        VpnResourceKind kind,
        Guid resourceId,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        var (networkIp, prefixLength) = await ParseCidrAsync(vpnNetworkId, cancellationToken);
        var taken = await GetTakenAddressesAsync(vpnNetworkId, cancellationToken);
        var candidate = FindNextFree(networkIp, prefixLength, taken);
        return $"{candidate}/32";
    }

    public async Task<string> AllocateManualIpAsync(
        Guid vpnNetworkId,
        string address,
        VpnResourceKind kind,
        Guid resourceId,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        var ip = address.Split('/')[0].Trim();
        if (!IPAddress.TryParse(ip, out _))
            throw new InvalidOperationException($"IP inválido: {address}");

        var taken = await GetTakenAddressesAsync(vpnNetworkId, cancellationToken);
        if (taken.Contains(ip))
            throw new InvalidOperationException($"IP {ip} já está em uso nesta rede VPN.");

        return $"{ip}/32";
    }

    public async Task<string> PreviewNextIpAsync(Guid vpnNetworkId, CancellationToken cancellationToken = default)
    {
        var (networkIp, prefixLength) = await ParseCidrAsync(vpnNetworkId, cancellationToken);
        var taken = await GetTakenAddressesAsync(vpnNetworkId, cancellationToken);
        var candidate = FindNextFree(networkIp, prefixLength, taken);
        return $"{candidate}/32";
    }

    public Task ReleaseAsync(Guid vpnNetworkId, VpnResourceKind kind, Guid resourceId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    private async Task<(IPAddress networkIp, int prefixLength)> ParseCidrAsync(Guid vpnNetworkId, CancellationToken ct)
    {
        var vpn = await _vpnNetworkRepo.GetByIdAsync(vpnNetworkId, ct)
            ?? throw new KeyNotFoundException($"VpnNetwork {vpnNetworkId} não encontrada.");
        var parts = vpn.Cidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var netIp) || !int.TryParse(parts[1], out var prefix))
            throw new InvalidOperationException($"CIDR inválido: {vpn.Cidr}");
        return (netIp, prefix);
    }

    private async Task<HashSet<string>> GetTakenAddressesAsync(Guid vpnNetworkId, CancellationToken ct)
    {
        var allocatedIps = await _peerRepo.GetAllocatedIpsByNetworkAsync(vpnNetworkId, ct);
        var set = new HashSet<string>();
        foreach (var allocatedIp in allocatedIps)
        {
            var firstIp = allocatedIp.Split(',')[0].Trim();
            var ipOnly = firstIp.Contains('/') ? firstIp.Split('/')[0].Trim() : firstIp.Trim();
            if (IPAddress.TryParse(ipOnly, out _))
                set.Add(ipOnly);
        }

        return set;
    }

    private static string FindNextFree(IPAddress networkIp, int prefixLength, HashSet<string> taken)
    {
        var bytes = networkIp.GetAddressBytes();
        var netVal = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
        var hostBits = 32 - prefixLength;
        var maxSearch = Math.Min((uint)Math.Pow(2, hostBits) - 2, 254u);

        for (uint offset = 2; offset <= maxSearch; offset++)
        {
            var ipVal = netVal + offset;
            var ipBytes = new byte[]
            {
                (byte)((ipVal >> 24) & 0xFF),
                (byte)((ipVal >> 16) & 0xFF),
                (byte)((ipVal >> 8) & 0xFF),
                (byte)(ipVal & 0xFF)
            };
            var candidate = new IPAddress(ipBytes).ToString();
            if (!taken.Contains(candidate))
                return candidate;
        }

        throw new InvalidOperationException("Não há IPs disponíveis nesta rede VPN.");
    }
}
