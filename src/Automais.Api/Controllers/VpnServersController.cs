using Automais.Api.Extensions;
using Automais.Core.DTOs;
using Automais.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Automais.Api.Controllers;

/// <summary>
/// Controller para gerenciamento de servidores VPN
/// </summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public class VpnServersController : ControllerBase
{
    private readonly IVpnNetworkRepository _vpnNetworkRepository;
    private readonly IRouterRepository _routerRepository;
    private readonly IVpnPeerRepository _peerRepository;
    private readonly IHostRepository _hostRepository;
    private readonly ILogger<VpnServersController> _logger;
    private readonly IConfiguration _configuration;

    public VpnServersController(
        IVpnNetworkRepository vpnNetworkRepository,
        IRouterRepository routerRepository,
        IVpnPeerRepository peerRepository,
        IHostRepository hostRepository,
        ILogger<VpnServersController> logger,
        IConfiguration configuration)
    {
        _vpnNetworkRepository = vpnNetworkRepository;
        _routerRepository = routerRepository;
        _peerRepository = peerRepository;
        _hostRepository = hostRepository;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Endpoint usado pelo serviço VPN Python para descobrir quais recursos ele deve gerenciar.
    /// O serviço Python consulta este endpoint usando seu VPN_SERVER_ENDPOINT (variável de ambiente).
    /// Busca todas as VpnNetworks que têm o ServerEndpoint correspondente.
    /// </summary>
    /// <param name="endpoint">Endpoint do servidor VPN (ex: automais.io) - deve corresponder ao ServerEndpoint das VpnNetworks</param>
    /// <returns>Lista de VpnNetworks e Routers que este servidor VPN deve gerenciar</returns>
    [HttpGet("vpn/networks/{endpoint}/resources")]
    public async Task<ActionResult<object>> GetNetworkResources(string endpoint, CancellationToken cancellationToken = default)
    {
        if (!HttpContext.IsLocalRequest() && !HttpContext.IsInternalRequest(_configuration))
        {
            return StatusCode(403, new { message = "Acesso negado. Use autenticação (Bearer) ou chave de serviço (X-Automais-Internal-Key)." });
        }

        try
        {
            _logger.LogInformation("Serviço VPN com endpoint '{Endpoint}' consultando seus recursos", endpoint);

            // Buscar todas as VpnNetworks que têm este ServerEndpoint
            var allVpnNetworks = await _vpnNetworkRepository.GetAllAsync(cancellationToken);
            
            // Filtrar VpnNetworks pelo ServerEndpoint
            var vpnNetworks = allVpnNetworks
                .Where(vpn => vpn.ServerEndpoint != null && vpn.ServerEndpoint.Equals(endpoint, StringComparison.OrdinalIgnoreCase))
                .Select(vpn => new
                {
                    id = vpn.Id.ToString(),
                    name = vpn.Name,
                    cidr = vpn.Cidr,
                    server_endpoint = vpn.ServerEndpoint,
                    listen_port = vpn.ListenPort > 0 ? vpn.ListenPort : 51820,
                    server_private_key = vpn.ServerPrivateKey,
                    server_public_key = vpn.ServerPublicKey,
                    dns_servers = vpn.DnsServers,
                    tenant_id = vpn.TenantId.ToString()
                })
                .ToList();

            var vpnPortById = vpnNetworks.ToDictionary(v => v.id, v => v.listen_port);

            // Buscar todos os Routers que pertencem às VpnNetworks deste servidor
            var vpnNetworkIds = vpnNetworks.Select(v => Guid.Parse(v.id)).ToList();
            var allRouters = await _routerRepository.GetAllAsync(cancellationToken);
            
            var routerList = allRouters
                .Where(r => r.VpnNetworkId.HasValue && vpnNetworkIds.Contains(r.VpnNetworkId.Value))
                .ToList();
            
            // Buscar peers para cada router
            var routers = new List<object>();
            foreach (var router in routerList)
            {
                var peers = await _peerRepository.GetByRouterIdAsync(router.Id, cancellationToken);
                var peersList = peers
                    .Where(p => p.IsEnabled && !string.IsNullOrEmpty(p.PublicKey) && !string.IsNullOrEmpty(p.PeerIp))
                    .Select(p => new
                    {
                        id = p.Id.ToString(),
                        router_id = router.Id.ToString(),
                        host_id = (string?)null,
                        vpn_network_id = p.VpnNetworkId.ToString(),
                        public_key = p.PublicKey,
                        peer_ip = p.PeerIp,
                        allowed_ips = p.PeerIp,
                        endpoint = p.Endpoint,
                        listen_port = vpnPortById.GetValueOrDefault(p.VpnNetworkId.ToString(), 51820),
                        is_enabled = p.IsEnabled
                    })
                    .ToList();
                
                routers.Add(new
                {
                    id = router.Id.ToString(),
                    name = router.Name,
                    vpn_network_id = router.VpnNetworkId?.ToString(),
                    router_os_api_url = router.RouterOsApiUrl,
                    status = router.Status.ToString(),
                    peers = peersList
                });
            }

            var hostsManaged = await _hostRepository.GetByVpnNetworkIdsAsync(vpnNetworkIds, cancellationToken);
            var hostsPayload = new List<object>();
            foreach (var host in hostsManaged)
            {
                if (!host.VpnPeerId.HasValue)
                    continue;
                var hp = await _peerRepository.GetByIdAsync(host.VpnPeerId.Value, cancellationToken);
                if (hp == null || !hp.IsEnabled || string.IsNullOrEmpty(hp.PublicKey) || string.IsNullOrEmpty(hp.PeerIp))
                    continue;
                hostsPayload.Add(new
                {
                    id = host.Id.ToString(),
                    name = host.Name,
                    vpn_network_id = host.VpnNetworkId?.ToString(),
                    peers = new[]
                    {
                        new
                        {
                            id = hp.Id.ToString(),
                            router_id = (string?)null,
                            host_id = host.Id.ToString(),
                            vpn_network_id = hp.VpnNetworkId.ToString(),
                            public_key = hp.PublicKey,
                            peer_ip = hp.PeerIp,
                            allowed_ips = hp.PeerIp,
                            endpoint = hp.Endpoint,
                            listen_port = vpnPortById.GetValueOrDefault(hp.VpnNetworkId.ToString(), 51820),
                            is_enabled = hp.IsEnabled
                        }
                    }
                });
            }

            _logger.LogInformation(
                "Servidor VPN com endpoint '{Endpoint}' gerencia {VpnCount} VPNs, {RouterCount} Routers e {HostCount} Hosts",
                endpoint, vpnNetworks.Count, routers.Count, hostsPayload.Count);

            return Ok(new
            {
                endpoint = endpoint,
                vpn_networks = vpnNetworks,
                routers = routers,
                hosts = hostsPayload,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao consultar recursos do servidor VPN com endpoint '{Endpoint}'", endpoint);
            return StatusCode(500, new
            {
                message = "Erro ao consultar recursos do servidor VPN",
                detail = ex.Message
            });
        }
    }

    /// <summary>
    /// Health check do servidor VPN
    /// </summary>
    [HttpGet("vpn/networks/{endpoint}/health")]
    public async Task<ActionResult<object>> GetNetworkHealth(string endpoint, CancellationToken cancellationToken = default)
    {
        try
        {
            // Verificar se existem VpnNetworks com este endpoint
            var allVpnNetworks = await _vpnNetworkRepository.GetAllAsync(cancellationToken);
            var hasNetworks = allVpnNetworks.Any(vpn => 
                vpn.ServerEndpoint != null && vpn.ServerEndpoint.Equals(endpoint, StringComparison.OrdinalIgnoreCase));

            if (!hasNetworks)
            {
                return NotFound(new { message = $"Nenhuma VpnNetwork encontrada com endpoint '{endpoint}'" });
            }

            return Ok(new
            {
                endpoint = endpoint,
                status = "active",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao verificar health do servidor VPN com endpoint '{Endpoint}'", endpoint);
            return StatusCode(500, new
            {
                message = "Erro ao verificar health do servidor VPN",
                detail = ex.Message
            });
        }
    }
}

