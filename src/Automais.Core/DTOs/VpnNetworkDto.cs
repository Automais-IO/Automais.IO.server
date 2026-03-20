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
    /// <summary>Porta UDP do túnel no servidor (única por ServerEndpoint).</summary>
    public int ListenPort { get; set; }
    /// <summary>True se ServerPrivateKey e ServerPublicKey estão preenchidos no banco.</summary>
    public bool ServerKeysConfigured { get; set; }
    /// <summary>Chave pública do servidor (somente leitura; a privada nunca é exposta).</summary>
    public string? ServerPublicKey { get; set; }
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
    /// <summary>Se omitido, aloca automaticamente a próxima porta livre no mesmo ServerEndpoint.</summary>
    public int? ListenPort { get; set; }
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
    /// Chave pública do servidor VPN. Preenchida automaticamente pela API ao obter do servidor,
    /// ou manualmente se o servidor for externo.
    /// </summary>
    public string? ServerPublicKey { get; set; }
    /// <summary>Alterar exige porta livre entre redes com o mesmo ServerEndpoint.</summary>
    public int? ListenPort { get; set; }
}


