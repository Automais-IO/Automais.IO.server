using Automais.Core.DTOs;
using Automais.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace Automais.Infrastructure.Services;

/// <summary>
/// Configuração do serviço RouterOS Python
/// </summary>
public class RouterOsServiceOptions
{
    public string BaseUrl { get; set; } = "http://localhost:8001";
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
}

/// <summary>
/// Cliente HTTP para comunicação com o serviço RouterOS Python
/// IMPORTANTE: As URLs são construídas dinamicamente baseadas no ServerEndpoint da VpnNetwork.
/// Cada router pode estar associado a uma VpnNetwork diferente, que por sua vez pode ter um ServerEndpoint diferente.
/// O ServerEndpoint identifica qual servidor RouterOS físico gerencia aquela rede.
/// </summary>
public class RouterOsServiceClient : IRouterOsServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RouterOsServiceClient> _logger;
    private readonly RouterOsServiceOptions _options;
    private readonly IVpnNetworkRepository? _vpnNetworkRepository;
    private readonly IRouterRepository? _routerRepository;

    public RouterOsServiceClient(
        HttpClient httpClient,
        IOptions<RouterOsServiceOptions> options,
        ILogger<RouterOsServiceClient> logger,
        IVpnNetworkRepository? vpnNetworkRepository = null,
        IRouterRepository? routerRepository = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _vpnNetworkRepository = vpnNetworkRepository;
        _routerRepository = routerRepository;

        // NÃO configurar BaseAddress - URLs serão construídas dinamicamente por chamada
        // baseadas no ServerEndpoint da VpnNetwork
        _httpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    /// <summary>
    /// Busca o ServerEndpoint da VpnNetwork
    /// </summary>
    private async Task<string?> GetServerEndpointAsync(Guid vpnNetworkId, CancellationToken cancellationToken)
    {
        if (_vpnNetworkRepository == null)
        {
            return null;
        }

        try
        {
            var vpnNetwork = await _vpnNetworkRepository.GetByIdAsync(vpnNetworkId, cancellationToken);
            return vpnNetwork?.ServerEndpoint;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Busca o ServerEndpoint do router via sua VpnNetwork
    /// </summary>
    private async Task<string?> GetServerEndpointFromRouterAsync(Guid routerId, CancellationToken cancellationToken)
    {
        if (_routerRepository == null || _vpnNetworkRepository == null)
        {
            return null;
        }

        try
        {
            var router = await _routerRepository.GetByIdAsync(routerId, cancellationToken);
            if (router?.VpnNetworkId == null)
            {
                return null;
            }

            return await GetServerEndpointAsync(router.VpnNetworkId.Value, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Constrói a URL base do serviço RouterOS baseada no ServerEndpoint.
    /// IMPORTANTE: O ServerEndpoint identifica qual servidor RouterOS físico gerencia a rede.
    /// Cada VpnNetwork pode ter um ServerEndpoint diferente, permitindo múltiplos servidores RouterOS.
    /// Se o ServerEndpoint não for fornecido, usa a URL padrão da configuração.
    /// </summary>
    private string GetBaseUrl(string? serverEndpoint = null)
    {
        if (string.IsNullOrWhiteSpace(serverEndpoint))
        {
            // Fallback para URL padrão se não houver ServerEndpoint
            return _options.BaseUrl;
        }

        // Construir URL baseada no ServerEndpoint
        // Formato esperado: http://{ServerEndpoint}:8001 ou https://{ServerEndpoint}:8001
        // Se o ServerEndpoint já contém protocolo, usar; senão, adicionar http://
        if (serverEndpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            serverEndpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // Se já tem porta, usar como está; senão, adicionar porta padrão do RouterOS (8001)
            if (serverEndpoint.Contains(':', StringComparison.Ordinal) && 
                !serverEndpoint.EndsWith("://", StringComparison.OrdinalIgnoreCase))
            {
                return serverEndpoint;
            }
            return $"{serverEndpoint}:8001";
        }

        // Adicionar protocolo e porta padrão do RouterOS (8001)
        return $"http://{serverEndpoint}:8001";
    }

    public async Task<(bool Success, string? GatewayUsed)> AddRouteAsync(
        Guid routerId,
        RouterStaticRouteDto route,
        CancellationToken cancellationToken = default)
    {
        var request = new
        {
            router_id = routerId.ToString(),
            route_id = route.Id.ToString(),
            destination = route.Destination,
            gateway = route.Gateway,
            interface_name = route.Interface,
            distance = route.Distance,
            scope = route.Scope,
            routing_table = route.RoutingTable,
            comment = route.Comment
        };

        _logger.LogInformation(
            "[Router API] Tentativa de acesso ao router via API: RouterId={RouterId}, operação=AdicionarRota, Destination={Destination}, Gateway={Gateway}",
            routerId, route.Destination, route.Gateway);

        try
        {
            // Buscar ServerEndpoint do router para construir URL dinâmica
            var serverEndpoint = await GetServerEndpointFromRouterAsync(routerId, cancellationToken);
            var baseUrl = GetBaseUrl(serverEndpoint);
            var fullUrl = $"{baseUrl.TrimEnd('/')}/api/v1/routeros/add-route";

            var response = await _httpClient.PostAsJsonAsync(
                fullUrl,
                request,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogInformation(
                    "Resposta do RouterOS (raw): {Response}", 
                    jsonContent);
                
                // Usar JsonSerializerOptions com case insensitive para garantir mapeamento correto
                // Os atributos JsonPropertyName têm prioridade sobre o PropertyNamingPolicy
                var jsonOptions = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                
                var result = await response.Content.ReadFromJsonAsync<RouteOperationResponse>(jsonOptions, cancellationToken);
                if (result?.Success == true)
                {
                    // Retornar gateway usado pelo RouterOS (pode ser IP ou nome de interface quando gateway estava vazio)
                    var gatewayUsed = result.GatewayUsed ?? string.Empty;
                    _logger.LogInformation(
                        "[Router API] Resultado da tentativa de acesso ao router via API: RouterId={RouterId}, operação=AdicionarRota, sucesso=Sim, RouterOsId={RouterOsId}, GatewayUsed='{GatewayUsed}'",
                        routerId, result.RouterOsId, gatewayUsed);
                    return (true, gatewayUsed);
                }
                _logger.LogWarning(
                    "[Router API] Resultado da tentativa de acesso ao router via API: RouterId={RouterId}, operação=AdicionarRota, sucesso=Não, mensagem=Resposta do serviço não indicou sucesso",
                    routerId);
                return (false, null);
            }

            _logger.LogWarning(
                "[Router API] Resultado da tentativa de acesso ao router via API: RouterId={RouterId}, operação=AdicionarRota, sucesso=Não, statusHTTP={StatusCode}",
                routerId, response?.StatusCode);
            return (false, null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "[Router API] Resultado da tentativa de acesso ao router via API: RouterId={RouterId}, operação=AdicionarRota, sucesso=Não, erro={Error}",
                routerId, ex.Message);
            return (false, null);
        }
    }

    public async Task<bool> RemoveRouteAsync(
        Guid routerId,
        string routerOsRouteId,
        CancellationToken cancellationToken = default)
    {
        var request = new
        {
            router_id = routerId.ToString(),
            router_os_route_id = routerOsRouteId
        };

        _logger.LogInformation(
            "[Router API] Tentativa de acesso ao router via API: RouterId={RouterId}, operação=RemoverRota, RouterOsRouteId={RouterOsRouteId}",
            routerId, routerOsRouteId);

        try
        {
            // Buscar ServerEndpoint do router para construir URL dinâmica
            var serverEndpoint = await GetServerEndpointFromRouterAsync(routerId, cancellationToken);
            var baseUrl = GetBaseUrl(serverEndpoint);
            var fullUrl = $"{baseUrl.TrimEnd('/')}/api/v1/routeros/remove-route";

            var response = await _httpClient.PostAsJsonAsync(
                fullUrl,
                request,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<RouteOperationResponse>(cancellationToken: cancellationToken);
                var success = result?.Success ?? false;
                _logger.LogInformation(
                    "[Router API] Resultado da tentativa de acesso ao router via API: RouterId={RouterId}, operação=RemoverRota, sucesso={Result}",
                    routerId, success ? "Sim" : "Não");
                return success;
            }

            _logger.LogWarning(
                "[Router API] Resultado da tentativa de acesso ao router via API: RouterId={RouterId}, operação=RemoverRota, sucesso=Não, statusHTTP={StatusCode}",
                routerId, response.StatusCode);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "[Router API] Resultado da tentativa de acesso ao router via API: RouterId={RouterId}, operação=RemoverRota, sucesso=Não, erro={Error}",
                routerId, ex.Message);
            return false;
        }
    }

    public async Task<List<RouterOsWireGuardInterfaceDto>> ListWireGuardInterfacesAsync(
        Guid routerId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Router API] Tentativa de acesso ao router via API: RouterId={RouterId}, operação=ListarInterfacesWireGuard",
            routerId);

        try
        {
            // Buscar ServerEndpoint do router para construir URL dinâmica
            var serverEndpoint = await GetServerEndpointFromRouterAsync(routerId, cancellationToken);
            var baseUrl = GetBaseUrl(serverEndpoint);
            var fullUrl = $"{baseUrl.TrimEnd('/')}/api/v1/routeros/{routerId}/wireguard-interfaces";

            var response = await _httpClient.GetAsync(fullUrl, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<WireGuardInterfacesResponse>(cancellationToken: cancellationToken);
                if (result?.Success == true && result.Interfaces != null)
                {
                    _logger.LogInformation(
                        "[Router API] Resultado da tentativa de acesso ao router via API: RouterId={RouterId}, operação=ListarInterfacesWireGuard, sucesso=Sim, quantidade={Count}",
                        routerId, result.Interfaces.Count);
                    return result.Interfaces.Select(i => new RouterOsWireGuardInterfaceDto
                    {
                        Name = i.Name ?? string.Empty,
                        PublicKey = i.PublicKey ?? string.Empty,
                        ListenPort = i.ListenPort,
                        Mtu = i.Mtu,
                        Disabled = ConvertToBool(i.Disabled),
                        Running = ConvertToBool(i.Running)
                    }).ToList();
                }
            }

            _logger.LogWarning(
                "[Router API] Resultado da tentativa de acesso ao router via API: RouterId={RouterId}, operação=ListarInterfacesWireGuard, sucesso=Não, statusHTTP={StatusCode}",
                routerId, response?.StatusCode);
            return new List<RouterOsWireGuardInterfaceDto>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "[Router API] Resultado da tentativa de acesso ao router via API: RouterId={RouterId}, operação=ListarInterfacesWireGuard, sucesso=Não, erro={Error}",
                routerId, ex.Message);
            return new List<RouterOsWireGuardInterfaceDto>();
        }
    }

    /// <summary>
    /// Converte valor para bool (pode ser string "true"/"false" ou bool)
    /// </summary>
    private static bool ConvertToBool(object? value)
    {
        if (value == null) return false;
        if (value is bool boolValue) return boolValue;
        if (value is string strValue)
        {
            return strValue.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   strValue.Equals("1", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    // Classes auxiliares para deserialização
    private class RouteOperationResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("success")]
        public bool Success { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string? Message { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("router_os_id")]
        public string? RouterOsId { get; set; }
        
        /// <summary>
        /// Gateway realmente usado pelo RouterOS (pode ser a interface se gateway estava vazio)
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("gateway_used")]
        public string? GatewayUsed { get; set; }
    }

    private class WireGuardInterfacesResponse
    {
        public bool Success { get; set; }
        public List<WireGuardInterfaceItem>? Interfaces { get; set; }
    }

    private class WireGuardInterfaceItem
    {
        public string? Name { get; set; }
        public string? PublicKey { get; set; }
        public string? ListenPort { get; set; }
        public string? Mtu { get; set; }
        public object? Disabled { get; set; }
        public object? Running { get; set; }
    }
}
