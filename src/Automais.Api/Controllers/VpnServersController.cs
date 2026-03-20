using Automais.Api.Extensions;
using Automais.Core;
using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Automais.Core.Services;
using Automais.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    private readonly ApplicationDbContext _db;
    private readonly ILogger<VpnServersController> _logger;
    private readonly IConfiguration _configuration;

    public VpnServersController(
        IVpnNetworkRepository vpnNetworkRepository,
        IRouterRepository routerRepository,
        IVpnPeerRepository peerRepository,
        IHostRepository hostRepository,
        ApplicationDbContext db,
        ILogger<VpnServersController> logger,
        IConfiguration configuration)
    {
        _vpnNetworkRepository = vpnNetworkRepository;
        _routerRepository = routerRepository;
        _peerRepository = peerRepository;
        _hostRepository = hostRepository;
        _db = db;
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

            var allVpnNetworks = await _vpnNetworkRepository.GetAllAsync(cancellationToken);

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

            var vpnNetworkIds = vpnNetworks.Select(v => Guid.Parse(v.id)).ToList();
            var allRouters = await _routerRepository.GetAllAsync(cancellationToken);

            var routerList = allRouters
                .Where(r => r.VpnNetworkId.HasValue && vpnNetworkIds.Contains(r.VpnNetworkId.Value))
                .ToList();

            var allPeersForNetworks = await _peerRepository.GetByVpnNetworkIdsAsync(vpnNetworkIds, cancellationToken);
            var activePeerIds = allPeersForNetworks
                .Where(p => p.IsEnabled && !string.IsNullOrEmpty(p.PublicKey) && !string.IsNullOrEmpty(p.PeerIp))
                .Select(p => p.Id)
                .ToList();

            var allowedRows = await _db.AllowedNetworks.AsNoTracking()
                .Where(n => activePeerIds.Contains(n.VpnPeerId))
                .ToListAsync(cancellationToken);
            var allowedByPeer = allowedRows
                .GroupBy(n => n.VpnPeerId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.NetworkCidr).ToList());

            var remoteRows = await _db.RemoteNetworks.AsNoTracking()
                .Where(n => activePeerIds.Contains(n.VpnPeerId))
                .ToListAsync(cancellationToken);
            var remoteByPeer = remoteRows
                .GroupBy(n => n.VpnPeerId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.NetworkCidr).ToList());

            var staticRows = await _db.StaticNetworks.AsNoTracking()
                .Where(s => activePeerIds.Contains(s.VpnPeerId))
                .ToListAsync(cancellationToken);
            var staticByPeer = staticRows
                .GroupBy(s => s.VpnPeerId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var routers = new List<object>();
            foreach (var router in routerList)
            {
                var peers = await _peerRepository.GetByRouterIdAsync(router.Id, cancellationToken);
                var peersList = peers
                    .Where(p => p.IsEnabled && !string.IsNullOrEmpty(p.PublicKey) && !string.IsNullOrEmpty(p.PeerIp))
                    .Select(p =>
                    {
                        var allowedList = allowedByPeer.GetValueOrDefault(p.Id) ?? new List<string>();
                        var remoteList = remoteByPeer.GetValueOrDefault(p.Id) ?? new List<string>();
                        var staticList = staticByPeer.GetValueOrDefault(p.Id) ?? new List<StaticNetwork>();
                        var staticDtos = staticList
                            .Select(x => new
                            {
                                destination = x.Destination,
                                gateway = x.Gateway,
                                interface_name = x.Interface
                            })
                            .ToList();
                        var tunnelPart = p.PeerIp.Split(',')[0].Trim();
                        var allowedIpsWg = VpnPeerRoutingHelper.ComposeServerAllowedIps(p.PeerIp, remoteList);
                        return new
                        {
                            id = p.Id.ToString(),
                            router_id = router.Id.ToString(),
                            host_id = (string?)null,
                            vpn_network_id = p.VpnNetworkId.ToString(),
                            public_key = p.PublicKey,
                            peer_ip = tunnelPart,
                            allowed_ips = allowedIpsWg,
                            allowed_networks = allowedList,
                            remote_networks = remoteList,
                            static_networks = staticDtos,
                            endpoint = p.Endpoint,
                            listen_port = vpnPortById.GetValueOrDefault(p.VpnNetworkId.ToString(), 51820),
                            is_enabled = p.IsEnabled
                        };
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
            var hostSkippedNoVpnPeerId = 0;
            var hostSkippedInvalidPeer = 0;
            foreach (var host in hostsManaged)
            {
                if (!host.VpnPeerId.HasValue)
                {
                    hostSkippedNoVpnPeerId++;
                    continue;
                }

                var hp = await _peerRepository.GetByIdAsync(host.VpnPeerId.Value, cancellationToken);
                if (hp == null || !hp.IsEnabled || string.IsNullOrEmpty(hp.PublicKey) || string.IsNullOrEmpty(hp.PeerIp))
                {
                    hostSkippedInvalidPeer++;
                    _logger.LogWarning(
                        "Host {HostId} tem VpnPeerId {PeerId} mas o peer não existe, está desabilitado ou sem chave/IP — não entra em vpn_peers.",
                        host.Id,
                        host.VpnPeerId.Value);
                    continue;
                }

                var allowedListH = allowedByPeer.GetValueOrDefault(hp.Id) ?? new List<string>();
                var remoteListH = remoteByPeer.GetValueOrDefault(hp.Id) ?? new List<string>();
                var staticListH = staticByPeer.GetValueOrDefault(hp.Id) ?? new List<StaticNetwork>();
                var staticDtosH = staticListH
                    .Select(x => new
                    {
                        destination = x.Destination,
                        gateway = x.Gateway,
                        interface_name = x.Interface
                    })
                    .ToList();
                var tunnelPartH = hp.PeerIp.Split(',')[0].Trim();
                var allowedIpsWgH = VpnPeerRoutingHelper.ComposeServerAllowedIps(hp.PeerIp, remoteListH);

                hostsPayload.Add(new
                {
                    id = host.Id.ToString(),
                    name = HostDisplayName.ForUi(host, hp),
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
                            peer_ip = tunnelPartH,
                            allowed_ips = allowedIpsWgH,
                            allowed_networks = allowedListH,
                            remote_networks = remoteListH,
                            static_networks = staticDtosH,
                            endpoint = hp.Endpoint,
                            listen_port = vpnPortById.GetValueOrDefault(hp.VpnNetworkId.ToString(), 51820),
                            is_enabled = hp.IsEnabled
                        }
                    }
                });
            }

            var routerByPeerId = routerList
                .Where(r => r.VpnPeerId.HasValue)
                .ToDictionary(r => r.VpnPeerId!.Value, r => r);
            var hostByPeerId = hostsManaged
                .Where(h => h.VpnPeerId.HasValue)
                .ToDictionary(h => h.VpnPeerId!.Value, h => h);

            var vpnPeersFlat = new List<object>();
            foreach (var p in allPeersForNetworks)
            {
                if (!p.IsEnabled || string.IsNullOrWhiteSpace(p.PublicKey) || string.IsNullOrWhiteSpace(p.PeerIp))
                    continue;

                string? rid = null;
                string? hid = null;
                string? rname = null;
                string? hname = null;
                string? hostProvisioning = null;

                if (routerByPeerId.TryGetValue(p.Id, out var rt))
                {
                    rid = rt.Id.ToString();
                    rname = rt.Name;
                }

                if (hostByPeerId.TryGetValue(p.Id, out var ht))
                {
                    hid = ht.Id.ToString();
                    hname = HostDisplayName.ForUi(ht, p);
                    hostProvisioning = ht.ProvisioningStatus.ToString();
                }

                var resName = !string.IsNullOrEmpty(hname) ? hname : (!string.IsNullOrEmpty(rname) ? rname : "unknown");

                var allowedList = allowedByPeer.GetValueOrDefault(p.Id) ?? new List<string>();
                var remoteList = remoteByPeer.GetValueOrDefault(p.Id) ?? new List<string>();
                var staticList = staticByPeer.GetValueOrDefault(p.Id) ?? new List<StaticNetwork>();
                var staticDtos = staticList
                    .Select(x => new
                    {
                        destination = x.Destination,
                        gateway = x.Gateway,
                        interface_name = x.Interface
                    })
                    .ToList();
                var tunnelPart = p.PeerIp.Split(',')[0].Trim();
                var allowedIpsWg = VpnPeerRoutingHelper.ComposeServerAllowedIps(p.PeerIp, remoteList);

                vpnPeersFlat.Add(new
                {
                    id = p.Id.ToString(),
                    vpn_network_id = p.VpnNetworkId.ToString(),
                    public_key = p.PublicKey,
                    peer_ip = tunnelPart,
                    allowed_ips = allowedIpsWg,
                    allowed_networks = allowedList,
                    remote_networks = remoteList,
                    static_networks = staticDtos,
                    endpoint = p.Endpoint,
                    listen_port = vpnPortById.GetValueOrDefault(p.VpnNetworkId.ToString(), 51820),
                    is_enabled = p.IsEnabled,
                    router_id = rid,
                    host_id = hid,
                    resource_name = resName,
                    host_provisioning_status = hostProvisioning
                });
            }

            if (hostSkippedNoVpnPeerId > 0)
            {
                _logger.LogWarning(
                    "Endpoint '{Endpoint}': {Count} host(s) na rede VPN sem VpnPeerId — ficam de fora de hosts[] e de vpn_peers até associar um peer (cada host precisa de linha em vpn_peers e FK em hosts).",
                    endpoint,
                    hostSkippedNoVpnPeerId);
            }

            if (hostSkippedInvalidPeer > 0)
            {
                _logger.LogWarning(
                    "Endpoint '{Endpoint}': {Count} host(s) com VpnPeerId apontando para peer inexistente, desabilitado ou sem chave/IP.",
                    endpoint,
                    hostSkippedInvalidPeer);
            }

            _logger.LogInformation(
                "Endpoint '{Endpoint}': {VpnCount} rede(s) VPN; {RouterCount} dispositivo(s) MikroTik (routers[]); {HostOk} host(s) com peer OK (hosts[]); {FlatPeerCount} entrada(s) em vpn_peers (lista plana, peers habilitados); {DbPeerCount} linha(s) total em vpn_peers nessas redes.",
                endpoint,
                vpnNetworks.Count,
                routers.Count,
                hostsPayload.Count,
                vpnPeersFlat.Count,
                allPeersForNetworks.Count());

            return Ok(new
            {
                endpoint = endpoint,
                vpn_networks = vpnNetworks,
                vpn_peers = vpnPeersFlat,
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
