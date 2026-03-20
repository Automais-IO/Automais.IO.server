namespace Automais.Core.Entities;

/// <summary>
/// Rota estática aplicada no peer (RouterOS ou host Linux: PostUp/PostDown).
/// </summary>
public class StaticNetwork
{
    public Guid Id { get; set; }

    public Guid VpnPeerId { get; set; }

    public string Destination { get; set; } = string.Empty;

    public string Gateway { get; set; } = string.Empty;

    public string? Interface { get; set; }

    public int? Distance { get; set; }

    public int? Scope { get; set; }

    public string? RoutingTable { get; set; }

    public string? Description { get; set; }

    public string Comment { get; set; } = string.Empty;

    public StaticNetworkStatus Status { get; set; } = StaticNetworkStatus.PendingAdd;

    public bool IsActive { get; set; }

    public string? RouterOsId { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public VpnPeer VpnPeer { get; set; } = null!;
}
