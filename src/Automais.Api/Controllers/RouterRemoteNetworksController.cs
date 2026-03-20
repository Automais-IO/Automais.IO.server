using Automais.Core.DTOs;
using Automais.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Automais.Api.Controllers;

/// <summary>
/// LAN por trás do peer (remote_network) — servidor VPN / iptables.
/// </summary>
[ApiController]
[Route("api/routers/{routerId:guid}/remote-networks")]
[Produces("application/json")]
public class RouterRemoteNetworksController : ControllerBase
{
    private readonly IRemoteNetworkService _service;
    private readonly ILogger<RouterRemoteNetworksController> _logger;

    public RouterRemoteNetworksController(
        IRemoteNetworkService service,
        ILogger<RouterRemoteNetworksController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<RemoteNetworkDto>>> GetByRouter(Guid routerId, CancellationToken cancellationToken)
    {
        try
        {
            var list = await _service.GetByRouterIdAsync(routerId, cancellationToken);
            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar remote-networks do router {RouterId}", routerId);
            return StatusCode(500, new { message = "Erro ao listar redes remotas", detail = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RemoteNetworkDto>> GetById(Guid routerId, Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var item = await _service.GetByIdAsync(id, cancellationToken);
            if (item == null)
                return NotFound(new { message = "Rede remota não encontrada" });
            if (item.RouterId != routerId)
                return BadRequest(new { message = "A rede remota não pertence a este router" });
            return Ok(item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao obter remote-network {Id}", id);
            return StatusCode(500, new { message = "Erro ao obter rede remota", detail = ex.Message });
        }
    }

    [HttpPost]
    public async Task<ActionResult<RemoteNetworkDto>> Create(
        Guid routerId,
        [FromBody] CreateRemoteNetworkDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await _service.CreateAsync(routerId, dto, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { routerId, id = created.Id }, created);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar remote-network router {RouterId}", routerId);
            return StatusCode(500, new { message = "Erro ao criar rede remota", detail = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RemoteNetworkDto>> Update(
        Guid routerId,
        Guid id,
        [FromBody] UpdateRemoteNetworkDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _service.GetByIdAsync(id, cancellationToken);
            if (existing == null)
                return NotFound(new { message = "Rede remota não encontrada" });
            if (existing.RouterId != routerId)
                return BadRequest(new { message = "A rede remota não pertence a este router" });

            var updated = await _service.UpdateAsync(id, dto, cancellationToken);
            return Ok(updated);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Rede remota não encontrada" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar remote-network {Id}", id);
            return StatusCode(500, new { message = "Erro ao atualizar rede remota", detail = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid routerId, Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _service.GetByIdAsync(id, cancellationToken);
            if (existing == null)
                return NotFound(new { message = "Rede remota não encontrada" });
            if (existing.RouterId != routerId)
                return BadRequest(new { message = "A rede remota não pertence a este router" });

            await _service.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao remover remote-network {Id}", id);
            return StatusCode(500, new { message = "Erro ao remover rede remota", detail = ex.Message });
        }
    }
}
