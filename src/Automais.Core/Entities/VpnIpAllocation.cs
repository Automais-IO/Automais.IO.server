namespace Automais.Core.Entities;

/// <summary>
/// Registro único de IP alocado dentro de uma VpnNetwork.
/// Centraliza todos os IPs usados por routers, hosts, usuários, devices, etc.
/// </summary>
public class VpnIpAllocation
{
    public Guid Id { get; set; }
    public Guid VpnNetworkId { get; set; }

    /// <summary>IP sem máscara (ex.: "10.100.1.50").</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Tipo de recurso dono deste IP.</summary>
    public VpnResourceKind ResourceKind { get; set; }

    /// <summary>ID do recurso (Router, Host, TenantUser, Device, Gateway…). Nulo para reservas manuais.</summary>
    public Guid? ResourceId { get; set; }

    /// <summary>Label legível para exibição (nome do recurso ou nota da reserva).</summary>
    public string? Label { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }

    public VpnNetwork VpnNetwork { get; set; } = null!;
}

public enum VpnResourceKind
{
    VpnServer = 0,
    Router = 1,
    Host = 2,
    TenantUser = 3,
    Device = 4,
    Gateway = 5,
    ManualReserve = 99
}
