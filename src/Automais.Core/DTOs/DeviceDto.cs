using Automais.Core.Entities;

namespace Automais.Core.DTOs;

public class DeviceDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DevEui { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DeviceKind Kind { get; set; }
    public DeviceStatus Status { get; set; }
    public double? BatteryLevel { get; set; }
    public double? SignalStrength { get; set; }
    public string? Location { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public bool VpnEnabled { get; set; }
    public string? VpnPublicKey { get; set; }
    public string? VpnIpAddress { get; set; }
    public VpnNetworkSummaryDto? VpnNetwork { get; set; }
    public bool WebDeviceEnabled { get; set; }
    /// <summary>Indica se existe token configurado (sem expor o hash).</summary>
    public bool WebDeviceTokenConfigured { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateDeviceDto
{
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DevEui { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DeviceKind Kind { get; set; } = DeviceKind.LoRaWan;
    public Guid? VpnNetworkId { get; set; }
    public bool VpnEnabled { get; set; }
    public string? VpnPublicKey { get; set; }
    public string? VpnIpAddress { get; set; }
}

public class UpdateDeviceDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public DeviceKind? Kind { get; set; }
    public DeviceStatus? Status { get; set; }
    public double? BatteryLevel { get; set; }
    public double? SignalStrength { get; set; }
    public string? Location { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public Guid? ApplicationId { get; set; }
    public Guid? VpnNetworkId { get; set; }
    public bool? ClearVpnNetwork { get; set; }
    public bool? VpnEnabled { get; set; }
    public string? VpnPublicKey { get; set; }
    public string? VpnIpAddress { get; set; }
    public bool? WebDeviceEnabled { get; set; }
}

/// <summary>Resposta única ao ativar ou regenerar token WebDevice.</summary>
public class WebDeviceTokenIssuedDto
{
    public string Token { get; set; } = string.Empty;
    public string Message { get; set; } = "Guarde o token com segurança; ele não será exibido novamente.";
}
