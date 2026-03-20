using Automais.Core.DTOs;
using Automais.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text;

namespace Automais.Api.Controllers;

/// <summary>API de peers (<c>vpn_peers</c>). Rotas mantêm <c>wireguard</c> na URL por compatibilidade com clientes.</summary>
[ApiController]
[Route("api")]
[Produces("application/json")]
public class VpnPeersController : ControllerBase
{
    private readonly IVpnPeerService _vpnPeerService;
    private readonly ILogger<VpnPeersController> _logger;

    public VpnPeersController(
        IVpnPeerService vpnPeerService,
        ILogger<VpnPeersController> logger)
    {
        _vpnPeerService = vpnPeerService;
        _logger = logger;
    }

    [HttpGet("routers/{routerId:guid}/wireguard/peers")]
    public async Task<ActionResult<IEnumerable<VpnPeerDto>>> GetPeers(Guid routerId, CancellationToken cancellationToken)
    {
        var peers = await _vpnPeerService.GetByRouterIdAsync(routerId, cancellationToken);
        return Ok(peers);
    }

    [HttpGet("wireguard/peers/{id:guid}")]
    public async Task<ActionResult<VpnPeerDto>> GetPeerById(Guid id, CancellationToken cancellationToken)
    {
        var peer = await _vpnPeerService.GetByIdAsync(id, cancellationToken);
        if (peer == null)
        {
            return NotFound(new { message = $"Peer VPN com ID {id} não encontrado" });
        }
        return Ok(peer);
    }

    [HttpPost("routers/{routerId:guid}/wireguard/peers")]
    public async Task<ActionResult<VpnPeerDto>> CreatePeer(Guid routerId, [FromBody] CreateVpnPeerDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var created = await _vpnPeerService.CreatePeerAsync(routerId, dto, cancellationToken);
            return CreatedAtAction(nameof(GetPeerById), new { id = created.Id }, created);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Erro ao criar peer VPN");
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Erro ao criar peer VPN");
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("wireguard/peers/{id:guid}")]
    public async Task<ActionResult<VpnPeerDto>> UpdatePeer(Guid id, [FromBody] CreateVpnPeerDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _vpnPeerService.UpdatePeerAsync(id, dto, cancellationToken);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Peer VPN não encontrado");
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpDelete("wireguard/peers/{id:guid}")]
    public async Task<IActionResult> DeletePeer(Guid id, CancellationToken cancellationToken)
    {
        await _vpnPeerService.DeletePeerAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("wireguard/peers/{id:guid}/config")]
    public async Task<ActionResult<VpnPeerConfigDto>> GetConfig(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var config = await _vpnPeerService.GetConfigAsync(id, cancellationToken);
            return Ok(config);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Peer VPN não encontrado");
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>Download da configuração WireGuard do router (arquivo .conf)</summary>
    [HttpGet("routers/{routerId:guid}/wireguard/config/download")]
    public async Task<IActionResult> DownloadConfig(
        Guid routerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var peers = await _vpnPeerService.GetByRouterIdAsync(routerId, cancellationToken);
            var peer = peers.FirstOrDefault();
            if (peer == null)
            {
                return NotFound(new { message = $"Router {routerId} não possui peer VPN configurado" });
            }
            
            var config = await _vpnPeerService.GetConfigAsync(peer.Id, cancellationToken);
            
            if (string.IsNullOrEmpty(config.ConfigContent))
            {
                _logger.LogWarning("Configuração VPN vazia para router {RouterId}", routerId);
                return BadRequest(new { 
                    message = "Configuração VPN não disponível. O router precisa ter uma rede VPN configurada.",
                    detail = "Certifique-se de que o router foi criado com uma rede VPN (vpnNetworkId)."
                });
            }
            
            var bytes = Encoding.UTF8.GetBytes(config.ConfigContent);
            
            var fileName = config.FileName
                .Replace("\"", "")
                .Replace("\\", "")
                .Replace("/", "")
                .Replace(":", "")
                .Replace("*", "")
                .Replace("?", "")
                .Replace("<", "")
                .Replace(">", "")
                .Replace("|", "");
            
            if (!fileName.EndsWith(".conf", StringComparison.OrdinalIgnoreCase))
            {
                if (fileName.EndsWith(".conf_", StringComparison.OrdinalIgnoreCase))
                {
                    fileName = fileName.Substring(0, fileName.Length - 1);
                }
                else if (!fileName.Contains("."))
                {
                    fileName = fileName + ".conf";
                }
            }
            
            var contentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = fileName
            };
            Response.Headers.ContentDisposition = contentDisposition.ToString();
            
            return new FileContentResult(bytes, "text/plain");
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Router não encontrado para download de config: {RouterId}", routerId);
            return NotFound(new { 
                message = "Router não encontrado",
                detail = ex.Message 
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Erro ao gerar configuração VPN para router {RouterId}: {Error}", routerId, ex.Message);
            return BadRequest(new { 
                message = "Erro ao gerar configuração VPN",
                detail = ex.Message,
                hint = "Certifique-se de que o router possui uma rede VPN configurada e que o peer foi criado corretamente."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao baixar configuração VPN para router {RouterId}", routerId);
            return StatusCode(500, new { 
                message = "Erro interno do servidor ao baixar configuração VPN",
                detail = ex.Message,
                innerException = ex.InnerException?.Message
            });
        }
    }

    [HttpPost("wireguard/peers/{id:guid}/regenerate-keys")]
    public async Task<ActionResult<VpnPeerDto>> RegenerateKeys(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _vpnPeerService.RegenerateKeysAsync(id, cancellationToken);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Peer VPN não encontrado");
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Erro ao renovar chaves do peer");
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Atualiza estatísticas do peer (serviço de monitoramento Python).</summary>
    [HttpPatch("wireguard/peers/{id:guid}/stats")]
    public async Task<IActionResult> UpdatePeerStats(Guid id, [FromBody] UpdatePeerStatsDto dto, CancellationToken cancellationToken)
    {
        try
        {
            await _vpnPeerService.UpdatePeerStatsAsync(id, dto, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Peer VPN não encontrado para atualizar stats");
            return NotFound(new { message = ex.Message });
        }
    }
}
