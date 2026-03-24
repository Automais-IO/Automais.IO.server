namespace Automais.Core.Entities;

/// <summary>
/// Dispositivo na plataforma (LoRaWAN, MQTT, API customizada, etc.).
/// <see cref="DevEui"/> é o identificador estável do device na aplicação (DevEUI LoRa, clientId MQTT, etc.).
/// </summary>
public class Device
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid? VpnNetworkId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DevEui { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? DeviceProfileId { get; set; }
    public DeviceKind Kind { get; set; } = DeviceKind.LoRaWan;
    public DeviceStatus Status { get; set; } = DeviceStatus.Provisioning;
    public double? BatteryLevel { get; set; }
    public double? SignalStrength { get; set; }
    public string? Location { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string? Metadata { get; set; }
    public bool VpnEnabled { get; set; }
    public string? VpnPublicKey { get; set; }
    public string? VpnIpAddress { get; set; }

    /// <summary>Permite túnel WebDevice (agente no firmware + proxy na nuvem).</summary>
    public bool WebDeviceEnabled { get; set; }

    /// <summary>Hash BCrypt do token do agente WebDevice (plain mostrado uma vez no painel).</summary>
    public string? WebDeviceTokenHash { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public Application Application { get; set; } = null!;
    public VpnNetwork? VpnNetwork { get; set; }
}

public enum DeviceKind
{
    LoRaWan = 1,
    Mqtt = 2,
    CustomApi = 3
}

public enum DeviceStatus
{
    Provisioning = 1,
    Active = 2,
    Warning = 3,
    Offline = 4,
    Decommissioned = 5
}
