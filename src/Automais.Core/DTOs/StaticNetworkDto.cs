using Automais.Core.Entities;

namespace Automais.Core.DTOs;

/// <summary>
/// Rota estática no peer / RouterOS (<c>static_networks</c>).
/// </summary>
public class StaticNetworkDto
{
    public Guid Id { get; set; }
    public Guid RouterId { get; set; }
    public Guid VpnPeerId { get; set; }
    public string Destination { get; set; } = string.Empty;
    public string Gateway { get; set; } = string.Empty;
    public string? Interface { get; set; }
    public int? Distance { get; set; }
    public int? Scope { get; set; }
    public string? RoutingTable { get; set; }
    public string? Description { get; set; }
    public string Comment { get; set; } = string.Empty;
    public StaticNetworkStatus Status { get; set; }
    public bool IsActive { get; set; }
    public string? RouterOsId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateStaticNetworkDto
{
    public string Destination { get; set; } = string.Empty;
    public string Gateway { get; set; } = string.Empty;
    public string? Interface { get; set; }
    public int? Distance { get; set; }
    public int? Scope { get; set; }
    public string? RoutingTable { get; set; }
    public string? Description { get; set; }
}

public class UpdateStaticNetworkDto
{
    public string? Destination { get; set; }
    public string? Gateway { get; set; }
    public string? Interface { get; set; }
    public int? Distance { get; set; }
    public int? Scope { get; set; }
    public string? RoutingTable { get; set; }
    public string? Description { get; set; }
}

public class BatchUpdateStaticNetworksDto
{
    public IEnumerable<Guid> StaticNetworkIdsToAdd { get; set; } = Enumerable.Empty<Guid>();
    public IEnumerable<Guid> StaticNetworkIdsToRemove { get; set; } = Enumerable.Empty<Guid>();
}

public class UpdateStaticNetworkStatusDto
{
    public Guid StaticNetworkId { get; set; }
    public StaticNetworkStatus Status { get; set; }
    public string? RouterOsId { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Gateway { get; set; }
}
