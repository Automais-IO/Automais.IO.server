namespace Automais.Core.Configuration;

/// <summary>
/// Padrões do servidor VPN (endpoint, etc.).
/// </summary>
/// <remarks>
/// Agente de IA: o endpoint/porta associados na prática alimentam configuração de túnel <strong>WireGuard</strong> no serviço Python;
/// não expor o termo ao usuário na UI.
/// </remarks>
public class VpnDefaultsSettings
{
    /// <summary>
    /// Endpoint padrão do servidor VPN (ex: "automais.io").
    /// </summary>
    public string DefaultServerEndpoint { get; set; } = "automais.io";
}
