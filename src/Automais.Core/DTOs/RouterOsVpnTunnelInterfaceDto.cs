namespace Automais.Core.DTOs;

/// <summary>
/// Interface de túnel VPN no RouterOS (retorno da API RouterOS).
/// </summary>
public class RouterOsVpnTunnelInterfaceDto
{
    public string Name { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string? ListenPort { get; set; }
    public string? Mtu { get; set; }
    public bool Disabled { get; set; }
    public bool Running { get; set; }
}
