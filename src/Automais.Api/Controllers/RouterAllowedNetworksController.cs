using Automais.Core.DTOs;
using Automais.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Automais.Api.Controllers;

/// <summary>
/// Controller para gerenciamento de redes destino dos routers (redes para as quais o tráfego VPN é encaminhado).
/// </summary>
[ApiController]
[Route("api/routers/{routerId:guid}/destination-networks")]
[Produces("application/json")]
public class RouterAllowedNetworksController : ControllerBase
{
    private readonly IRouterAllowedNetworkService _service;
    private readonly ILogger<RouterAllowedNetworksController> _logger;

    public RouterAllowedNetworksController(
        IRouterAllowedNetworkService service,
        ILogger<RouterAllowedNetworksController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Lista todas as redes destino de um router.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<RouterAllowedNetworkDto>>> GetByRouter(
        Guid routerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var list = await _service.GetByRouterIdAsync(routerId, cancellationToken);
            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar redes destino do router {RouterId}", routerId);
            return StatusCode(500, new { message = "Erro ao listar redes destino", detail = ex.Message });
        }
    }

    /// <summary>
    /// Obtém uma rede destino por ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RouterAllowedNetworkDto>> GetById(
        Guid routerId,
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var item = await _service.GetByIdAsync(id, cancellationToken);
            if (item == null)
                return NotFound(new { message = "Rede destino não encontrada" });
            if (item.RouterId != routerId)
                return BadRequest(new { message = "A rede destino não pertence a este router" });
            return Ok(item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao obter rede destino {Id} do router {RouterId}", id, routerId);
            return StatusCode(500, new { message = "Erro ao obter rede destino", detail = ex.Message });
        }
    }

    /// <summary>
    /// Cria uma nova rede destino para o router.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<RouterAllowedNetworkDto>> Create(
        Guid routerId,
        [FromBody] CreateRouterAllowedNetworkDto dto,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Criando rede destino para router {RouterId}: {Cidr}", routerId, dto.NetworkCidr);
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
            _logger.LogError(ex, "Erro ao criar rede destino para router {RouterId}", routerId);
            return StatusCode(500, new { message = "Erro ao criar rede destino", detail = ex.Message });
        }
    }

    /// <summary>
    /// Atualiza uma rede destino.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RouterAllowedNetworkDto>> Update(
        Guid routerId,
        Guid id,
        [FromBody] UpdateRouterAllowedNetworkDto dto,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Atualizando rede destino {Id} do router {RouterId}", id, routerId);
        try
        {
            var existing = await _service.GetByIdAsync(id, cancellationToken);
            if (existing == null)
                return NotFound(new { message = "Rede destino não encontrada" });
            if (existing.RouterId != routerId)
                return BadRequest(new { message = "A rede destino não pertence a este router" });

            var updated = await _service.UpdateAsync(id, dto, cancellationToken);
            return Ok(updated);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Rede destino não encontrada" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar rede destino {Id} do router {RouterId}", id, routerId);
            return StatusCode(500, new { message = "Erro ao atualizar rede destino", detail = ex.Message });
        }
    }

    /// <summary>
    /// Remove uma rede destino.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid routerId,
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _service.GetByIdAsync(id, cancellationToken);
            if (existing == null)
                return NotFound(new { message = "Rede destino não encontrada" });
            if (existing.RouterId != routerId)
                return BadRequest(new { message = "A rede destino não pertence a este router" });

            await _service.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao remover rede destino {Id} do router {RouterId}", id, routerId);
            return StatusCode(500, new { message = "Erro ao remover rede destino", detail = ex.Message });
        }
    }
}
