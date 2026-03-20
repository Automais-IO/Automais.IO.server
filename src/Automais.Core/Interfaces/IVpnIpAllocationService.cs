using Automais.Core.Entities;

namespace Automais.Core.Interfaces;

public interface IVpnIpAllocationService
{
    /// <summary>Retorna o próximo IP disponível na rede (formato "10.x.y.z/32").</summary>
    Task<string> AllocateNextIpAsync(Guid vpnNetworkId, VpnResourceKind kind, Guid resourceId, string? label = null, CancellationToken cancellationToken = default);

    /// <summary>Aloca um IP manual (validando conflito).</summary>
    Task<string> AllocateManualIpAsync(Guid vpnNetworkId, string address, VpnResourceKind kind, Guid resourceId, string? label = null, CancellationToken cancellationToken = default);

    /// <summary>Retorna o próximo IP que seria alocado (preview, sem persistir).</summary>
    Task<string> PreviewNextIpAsync(Guid vpnNetworkId, CancellationToken cancellationToken = default);

    /// <summary>Libera IPs alocados para um recurso.</summary>
    Task ReleaseAsync(Guid vpnNetworkId, VpnResourceKind kind, Guid resourceId, CancellationToken cancellationToken = default);
}
