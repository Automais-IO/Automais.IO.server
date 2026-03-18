using Automais.Core.DTOs;
using Automais.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Automais.Infrastructure.Services;

/// <summary>
/// Configuração do serviço RouterOS WebSocket
/// </summary>
public class RouterOsWebSocketOptions
{
    public string WebSocketUrl { get; set; } = "ws://localhost:8765";
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Cliente WebSocket para comunicação com o serviço RouterOS WebSocket Python
/// </summary>
public class RouterOsWebSocketClient : IRouterOsWebSocketClient
{
    private readonly RouterOsWebSocketOptions _options;
    private readonly ILogger<RouterOsWebSocketClient> _logger;
    private readonly IRouterRepository _routerRepository;
    private readonly IRouterWireGuardPeerRepository _peerRepository;
    private readonly IVpnNetworkRepository? _vpnNetworkRepository;

    public RouterOsWebSocketClient(
        IOptions<RouterOsWebSocketOptions> options,
        ILogger<RouterOsWebSocketClient> logger,
        IRouterRepository routerRepository,
        IRouterWireGuardPeerRepository peerRepository,
        IVpnNetworkRepository? vpnNetworkRepository = null)
    {
        _options = options.Value;
        _logger = logger;
        _routerRepository = routerRepository;
        _peerRepository = peerRepository;
        _vpnNetworkRepository = vpnNetworkRepository;
    }

    public async Task<RouterOsConnectionStatusDto> GetConnectionStatusAsync(
        Guid routerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Buscar router
            var router = await _routerRepository.GetByIdAsync(routerId, cancellationToken);
            if (router == null)
            {
                return new RouterOsConnectionStatusDto
                {
                    Success = false,
                    Connected = false,
                    Error = "Router não encontrado"
                };
            }

            // Obter IP do router
            var routerIp = await GetRouterIpAsync(router, cancellationToken);
            if (string.IsNullOrEmpty(routerIp))
            {
                return new RouterOsConnectionStatusDto
                {
                    Success = false,
                    Connected = false,
                    Error = "IP do router não encontrado. Configure RouterOsApiUrl ou crie um peer WireGuard."
                };
            }

            // Buscar ServerEndpoint da VpnNetwork do router para construir URL dinâmica
            // IMPORTANTE: O ServerEndpoint identifica qual servidor VPN físico gerencia a rede.
            // Cada router pode estar associado a uma VpnNetwork diferente, permitindo múltiplos servidores VPN.
            string? serverEndpoint = null;
            if (router.VpnNetworkId.HasValue && _vpnNetworkRepository != null)
            {
                try
                {
                    var vpnNetwork = await _vpnNetworkRepository.GetByIdAsync(router.VpnNetworkId.Value, cancellationToken);
                    serverEndpoint = vpnNetwork?.ServerEndpoint;
                }
                catch
                {
                    // Se falhar, usar URL padrão
                }
            }

            // Construir URL do WebSocket baseada no ServerEndpoint
            var wsUrl = GetWebSocketUrl(serverEndpoint);

            // Criar timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            
            // Conectar via WebSocket
            using var client = new ClientWebSocket();
            var uri = new Uri(wsUrl);
            
            await client.ConnectAsync(uri, linkedCts.Token);

            try
            {
                // Enviar mensagem de status
                var request = new
                {
                    action = "get_status",
                    router_id = routerId.ToString(),
                    router_ip = routerIp
                };

                var requestJson = JsonSerializer.Serialize(request);
                var requestBytes = Encoding.UTF8.GetBytes(requestJson);
                await client.SendAsync(
                    new ArraySegment<byte>(requestBytes),
                    WebSocketMessageType.Text,
                    true,
                    linkedCts.Token);

                // Receber resposta (pode vir em múltiplos chunks)
                var buffer = new List<byte>();
                var receiveBuffer = new byte[4096];
                
                WebSocketReceiveResult result;
                do
                {
                    result = await client.ReceiveAsync(
                        new ArraySegment<byte>(receiveBuffer),
                        linkedCts.Token);
                    
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        buffer.AddRange(receiveBuffer.Take(result.Count));
                    }
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var responseJson = Encoding.UTF8.GetString(buffer.ToArray());
                    var response = JsonSerializer.Deserialize<RouterOsConnectionStatusDto>(
                        responseJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (response != null)
                    {
                        return response;
                    }
                }

                return new RouterOsConnectionStatusDto
                {
                    Success = false,
                    Connected = false,
                    Error = "Resposta inválida do servidor WebSocket"
                };
            }
            finally
            {
                if (client.State == WebSocketState.Open)
                {
                    try
                    {
                        await client.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "Request completed",
                            CancellationToken.None);
                    }
                    catch
                    {
                        // Ignorar erros ao fechar
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao obter status da conexão RouterOS via WebSocket");
            return new RouterOsConnectionStatusDto
            {
                Success = false,
                Connected = false,
                Error = ex.Message
            };
        }
    }

    private async Task<string?> GetRouterIpAsync(Core.Entities.Router router, CancellationToken cancellationToken)
    {
        // Tentar obter do RouterOsApiUrl
        if (!string.IsNullOrWhiteSpace(router.RouterOsApiUrl))
        {
            var parts = router.RouterOsApiUrl.Split(':');
            if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
            {
                return parts[0].Trim();
            }
        }

        // Se não tiver, tentar buscar do peer WireGuard
        var peers = await _peerRepository.GetByRouterIdAsync(router.Id, cancellationToken);
        var activePeer = peers.FirstOrDefault();
        if (activePeer != null && !string.IsNullOrWhiteSpace(activePeer.PeerIp))
        {
            var first = activePeer.PeerIp.Split(',')[0].Trim();
            if (first.Contains('/'))
                return first.Split('/')[0].Trim();
            return first;
        }

        return null;
    }

    /// <summary>
    /// Constrói a URL do WebSocket baseada no ServerEndpoint.
    /// IMPORTANTE: O ServerEndpoint identifica qual servidor VPN físico gerencia a rede.
    /// Se o ServerEndpoint não for fornecido, usa a URL padrão da configuração.
    /// </summary>
    private string GetWebSocketUrl(string? serverEndpoint = null)
    {
        if (string.IsNullOrWhiteSpace(serverEndpoint))
        {
            // Fallback para URL padrão se não houver ServerEndpoint
            return _options.WebSocketUrl;
        }

        // Construir URL baseada no ServerEndpoint
        // Formato esperado: ws://{ServerEndpoint}:8765 ou wss://{ServerEndpoint}:8765
        // Se o ServerEndpoint já contém protocolo, usar; senão, adicionar ws://
        if (serverEndpoint.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
            serverEndpoint.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            // Se já tem porta, usar como está; senão, adicionar porta padrão
            if (serverEndpoint.Contains(':', StringComparison.Ordinal) && 
                !serverEndpoint.EndsWith("://", StringComparison.OrdinalIgnoreCase))
            {
                return serverEndpoint;
            }
            return $"{serverEndpoint}:8765";
        }

        // Adicionar protocolo e porta padrão
        return $"ws://{serverEndpoint}:8765";
    }
}
