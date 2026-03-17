namespace Automais.Core.DTOs;

public class VpnNetworkDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Cidr { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public string? DnsServers { get; set; }
    /// <summary>
    /// Endpoint do servidor VPN (ex: "automais.io"). 
    /// O frontend deve sempre enviar este valor (preenchido com "automais.io" por padrão, mas editável).
    /// </summary>
    public string? ServerEndpoint { get; set; }
    public int UserCount { get; set; }
    public int DeviceCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateVpnNetworkDto
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Cidr { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public string? DnsServers { get; set; }
    /// <summary>
    /// Endpoint do servidor VPN (ex: "automais.io"). 
    /// O frontend deve sempre enviar este valor (preenchido com "automais.io" por padrão, mas editável).
    /// </summary>
    public string? ServerEndpoint { get; set; }
}

public class UpdateVpnNetworkDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool? IsDefault { get; set; }
    public string? DnsServers { get; set; }
    /// <summary>
    /// Endpoint do servidor VPN (ex: "automais.io"). 
    /// O frontend deve sempre enviar este valor (preenchido com "automais.io" por padrão, mas editável).
    /// </summary>
    public string? ServerEndpoint { get; set; }
    /// <summary>
    /// Chave pública do servidor WireGuard. Preenchida automaticamente pela API ao obter do VPN server,
    /// ou manualmente se o servidor for externo.
    /// </summary>
    public string? ServerPublicKey { get; set; }
}


