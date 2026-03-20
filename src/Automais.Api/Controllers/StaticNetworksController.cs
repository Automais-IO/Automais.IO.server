using Automais.Core.DTOs;
using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Automais.Api.Controllers;

/// <summary>
/// Controller para gerenciamento de rotas estáticas dos Routers
/// </summary>
[ApiController]
[Route("api/routers/{routerId:guid}/static-networks")]
[Produces("application/json")]
public class StaticNetworksController : ControllerBase
{
    private readonly IStaticNetworkService _staticNetworkService;
    private readonly IRouterOsServiceClient? _routerOsServiceClient;
    private readonly ILogger<StaticNetworksController> _logger;

    public StaticNetworksController(
        IStaticNetworkService staticNetworkService,
        IRouterOsServiceClient? routerOsServiceClient,
        ILogger<StaticNetworksController> logger)
    {
        _staticNetworkService = staticNetworkService;
        _routerOsServiceClient = routerOsServiceClient;
        _logger = logger;
    }

    /// <summary>
    /// Lista todas as rotas estáticas de um router
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StaticNetworkDto>>> GetByRouter(
        Guid routerId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Listando rotas estáticas do router {RouterId}", routerId);
            var routes = await _staticNetworkService.GetByRouterIdAsync(routerId, cancellationToken);
            return Ok(routes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar rotas do router {RouterId}", routerId);
            return StatusCode(500, new
            {
                message = "Erro interno do servidor ao listar rotas",
                detail = ex.Message
            });
        }
    }

    /// <summary>
    /// Obtém uma rota estática por ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StaticNetworkDto>> GetById(
        Guid routerId,
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var route = await _staticNetworkService.GetByIdAsync(id, cancellationToken);
            if (route == null)
            {
                return NotFound(new { message = $"Rota com ID {id} não encontrada" });
            }

            if (route.RouterId != routerId)
            {
                return BadRequest(new { message = "A rota não pertence a este router" });
            }

            return Ok(route);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao obter rota {StaticNetworkId} do router {RouterId}", id, routerId);
            return StatusCode(500, new
            {
                message = "Erro interno do servidor ao obter rota",
                detail = ex.Message
            });
        }
    }

    /// <summary>
    /// Cria uma nova rota estática para um router
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<StaticNetworkDto>> Create(
        Guid routerId,
        [FromBody] CreateStaticNetworkDto dto,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Criando rota estática para router {RouterId}: Destination={Destination}, Gateway={Gateway}",
            routerId, dto.Destination, dto.Gateway);

        try
        {
            var created = await _staticNetworkService.CreateAsync(routerId, dto, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { routerId, id = created.Id }, created);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Router não encontrado ao criar rota");
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Erro ao criar rota");
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar rota para router {RouterId}", routerId);
            return StatusCode(500, new
            {
                message = "Erro interno do servidor ao criar rota",
                detail = ex.Message
            });
        }
    }

    /// <summary>
    /// Atualiza uma rota estática
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StaticNetworkDto>> Update(
        Guid routerId,
        Guid id,
        [FromBody] UpdateStaticNetworkDto dto,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Atualizando rota {StaticNetworkId} do router {RouterId}", id, routerId);

        try
        {
            var existing = await _staticNetworkService.GetByIdAsync(id, cancellationToken);
            if (existing == null)
            {
                return NotFound(new { message = $"Rota com ID {id} não encontrada" });
            }

            if (existing.RouterId != routerId)
            {
                return BadRequest(new { message = "A rota não pertence a este router" });
            }

            var updated = await _staticNetworkService.UpdateAsync(id, dto, cancellationToken);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Rota não encontrada para atualização");
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Erro ao atualizar rota");
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar rota {StaticNetworkId} do router {RouterId}", id, routerId);
            return StatusCode(500, new
            {
                message = "Erro interno do servidor ao atualizar rota",
                detail = ex.Message
            });
        }
    }

    /// <summary>
    /// Deleta uma rota estática
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid routerId,
        Guid id,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Solicitando remoção da rota {StaticNetworkId} do router {RouterId}", id, routerId);

        try
        {
            var existing = await _staticNetworkService.GetByIdAsync(id, cancellationToken);
            if (existing == null)
            {
                return NotFound(new { message = $"Rota com ID {id} não encontrada" });
            }

            if (existing.RouterId != routerId)
            {
                return BadRequest(new { message = "A rota não pertence a este router" });
            }

            if (string.IsNullOrWhiteSpace(existing.RouterOsId))
            {
                _logger.LogInformation("Rota {StaticNetworkId} nunca foi aplicada no RouterOS, deletando diretamente do banco", id);
                await _staticNetworkService.DeleteAsync(id, cancellationToken);
                return NoContent();
            }

            await _staticNetworkService.UpdateStaticNetworkStatusAsync(new UpdateStaticNetworkStatusDto
            {
                StaticNetworkId = id,
                Status = StaticNetworkStatus.PendingRemove
            }, cancellationToken);

            if (_routerOsServiceClient == null)
            {
                _logger.LogWarning("Serviço RouterOS não configurado, rota {StaticNetworkId} marcada como PendingRemove para processamento posterior", id);
                return Accepted(new { message = "Rota marcada para remoção. Será processada pelo serviço RouterOS." });
            }

            var success = await _routerOsServiceClient.RemoveRouteAsync(routerId, existing.RouterOsId, cancellationToken);

            if (success)
            {
                _logger.LogInformation("Rota {StaticNetworkId} removida com sucesso do RouterOS, deletando do banco", id);
                await _staticNetworkService.DeleteAsync(id, cancellationToken);
                return NoContent();
            }

            _logger.LogWarning("Falha ao remover rota {StaticNetworkId} do RouterOS, mantendo como PendingRemove para retry pelo sync periódico", id);
            await _staticNetworkService.UpdateStaticNetworkStatusAsync(new UpdateStaticNetworkStatusDto
            {
                StaticNetworkId = id,
                Status = StaticNetworkStatus.PendingRemove,
                ErrorMessage = "Falha ao remover rota do RouterOS. Será tentado novamente pelo sync periódico."
            }, cancellationToken);

            return Accepted(new
            {
                message = "Falha ao remover rota do RouterOS. A rota permanecerá marcada como PendingRemove e será tentada novamente pelo sync periódico.",
                staticNetworkId = id,
                status = "PendingRemove"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar remoção da rota {StaticNetworkId} do router {RouterId}", id, routerId);
            return StatusCode(500, new
            {
                message = "Erro interno do servidor ao processar remoção da rota",
                detail = ex.Message
            });
        }
    }

    /// <summary>
    /// Marca rotas para adicionar/remover (atualiza status no banco)
    /// </summary>
    [HttpPost("batch-status")]
    public async Task<IActionResult> BatchUpdateStatus(
        Guid routerId,
        [FromBody] BatchUpdateStaticNetworksDto dto,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Atualizando status em lote: Router={RouterId}, Add={AddCount}, Remove={RemoveCount}",
            routerId, dto.StaticNetworkIdsToAdd.Count(), dto.StaticNetworkIdsToRemove.Count());

        try
        {
            await _staticNetworkService.BatchUpdateStatusAsync(routerId, dto, cancellationToken);
            return Ok(new { message = "Status atualizado com sucesso" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar status em lote do router {RouterId}", routerId);
            return StatusCode(500, new
            {
                message = "Erro interno do servidor ao atualizar status",
                detail = ex.Message
            });
        }
    }

    /// <summary>
    /// Atualiza status de uma rota após aplicação no RouterOS
    /// </summary>
    [HttpPost("update-status")]
    public async Task<IActionResult> UpdateStaticNetworkStatus(
        [FromBody] UpdateStaticNetworkStatusDto dto,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("📥 POST update-status recebido: RouteId={StaticNetworkId}", dto.StaticNetworkId);
        _logger.LogInformation("   Status: {Status}", dto.Status);
        _logger.LogInformation("   RouterOsId: '{RouterOsId}'", dto.RouterOsId ?? "null");
        _logger.LogInformation("   ErrorMessage: '{ErrorMessage}'", dto.ErrorMessage ?? "null");
        _logger.LogInformation("   Gateway: '{Gateway}' (tipo: {Type})",
            dto.Gateway ?? "null", dto.Gateway?.GetType().Name ?? "null");

        try
        {
            await _staticNetworkService.UpdateStaticNetworkStatusAsync(dto, cancellationToken);
            _logger.LogInformation("✅ Status da rota {StaticNetworkId} atualizado com sucesso. Gateway: '{Gateway}'",
                dto.StaticNetworkId, dto.Gateway ?? "não informado");
            return Ok(new { message = "Status atualizado com sucesso" });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Rota não encontrada para atualizar status");
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar status da rota {StaticNetworkId}", dto.StaticNetworkId);
            return StatusCode(500, new
            {
                message = "Erro interno do servidor ao atualizar status",
                detail = ex.Message
            });
        }
    }

    /// <summary>
    /// Aplica rotas pendentes no RouterOS via VPN server
    /// </summary>
    [HttpPost("apply")]
    public async Task<ActionResult<object>> ApplyRoutes(
        Guid routerId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Aplicando rotas pendentes do router {RouterId}", routerId);

        try
        {
            if (_routerOsServiceClient == null)
            {
                return BadRequest(new { message = "Serviço RouterOS não configurado" });
            }

            var routes = await _staticNetworkService.GetByRouterIdAsync(routerId, cancellationToken);
            var routesToAdd = routes.Where(r => r.Status == StaticNetworkStatus.PendingAdd).ToList();
            var routesToRemove = routes.Where(r => r.Status == StaticNetworkStatus.PendingRemove).ToList();

            var results = new List<object>();

            foreach (var route in routesToAdd)
            {
                try
                {
                    var (success, gatewayUsed) = await _routerOsServiceClient.AddRouteAsync(routerId, route, cancellationToken);

                    if (success)
                    {
                        var gatewayToUpdate = gatewayUsed ?? string.Empty;
                        _logger.LogInformation(
                            "Atualizando rota {StaticNetworkId} com status Applied. Gateway recebido do RouterOS: '{GatewayUsed}' (tipo: {Type}, será passado: '{GatewayToUpdate}')",
                            route.Id, gatewayUsed ?? "null", gatewayUsed?.GetType().Name ?? "null", gatewayToUpdate);

                        var updateDto = new UpdateStaticNetworkStatusDto
                        {
                            StaticNetworkId = route.Id,
                            Status = StaticNetworkStatus.Applied,
                            Gateway = gatewayToUpdate
                        };

                        await _staticNetworkService.UpdateStaticNetworkStatusAsync(updateDto, cancellationToken);

                        _logger.LogInformation(
                            "Rota {StaticNetworkId} aplicada com sucesso. Gateway usado: '{GatewayUsed}'",
                            route.Id, gatewayUsed ?? "não informado");

                        results.Add(new
                        {
                            staticNetworkId = route.Id,
                            action = "add",
                            success = true
                        });
                    }
                    else
                    {
                        await _staticNetworkService.UpdateStaticNetworkStatusAsync(new UpdateStaticNetworkStatusDto
                        {
                            StaticNetworkId = route.Id,
                            Status = StaticNetworkStatus.Error,
                            ErrorMessage = "Falha ao adicionar rota no RouterOS"
                        }, cancellationToken);

                        results.Add(new
                        {
                            staticNetworkId = route.Id,
                            action = "add",
                            success = false,
                            error = "Falha ao adicionar rota no RouterOS"
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao adicionar rota {StaticNetworkId}", route.Id);
                    await _staticNetworkService.UpdateStaticNetworkStatusAsync(new UpdateStaticNetworkStatusDto
                    {
                        StaticNetworkId = route.Id,
                        Status = StaticNetworkStatus.Error,
                        ErrorMessage = ex.Message
                    }, cancellationToken);

                    results.Add(new
                    {
                        staticNetworkId = route.Id,
                        action = "add",
                        success = false,
                        error = ex.Message
                    });
                }
            }

            foreach (var route in routesToRemove)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(route.RouterOsId))
                    {
                        await _staticNetworkService.DeleteAsync(route.Id, cancellationToken);
                        results.Add(new
                        {
                            staticNetworkId = route.Id,
                            action = "remove",
                            success = true,
                            message = "Rota removida do banco (não estava no RouterOS)"
                        });
                        continue;
                    }

                    var success = await _routerOsServiceClient.RemoveRouteAsync(routerId, route.RouterOsId, cancellationToken);

                    if (success)
                    {
                        await _staticNetworkService.DeleteAsync(route.Id, cancellationToken);
                        results.Add(new
                        {
                            staticNetworkId = route.Id,
                            action = "remove",
                            success = true
                        });
                    }
                    else
                    {
                        await _staticNetworkService.UpdateStaticNetworkStatusAsync(new UpdateStaticNetworkStatusDto
                        {
                            StaticNetworkId = route.Id,
                            Status = StaticNetworkStatus.PendingRemove,
                            ErrorMessage = "Falha ao remover rota do RouterOS. Será tentado novamente pelo sync periódico."
                        }, cancellationToken);

                        results.Add(new
                        {
                            staticNetworkId = route.Id,
                            action = "remove",
                            success = false,
                            error = "Falha ao remover rota do RouterOS. Será tentado novamente pelo sync periódico."
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao remover rota {StaticNetworkId}", route.Id);
                    await _staticNetworkService.UpdateStaticNetworkStatusAsync(new UpdateStaticNetworkStatusDto
                    {
                        StaticNetworkId = route.Id,
                        Status = StaticNetworkStatus.Error,
                        ErrorMessage = ex.Message
                    }, cancellationToken);

                    results.Add(new
                    {
                        staticNetworkId = route.Id,
                        action = "remove",
                        success = false,
                        error = ex.Message
                    });
                }
            }

            return Ok(new
            {
                message = "Aplicação de rotas concluída",
                results = results,
                summary = new
                {
                    total = routesToAdd.Count + routesToRemove.Count,
                    added = results.Count(r => ((dynamic)r).action == "add" && ((dynamic)r).success == true),
                    removed = results.Count(r => ((dynamic)r).action == "remove" && ((dynamic)r).success == true),
                    errors = results.Count(r => ((dynamic)r).success == false)
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao aplicar rotas do router {RouterId}", routerId);
            return StatusCode(500, new
            {
                message = "Erro interno do servidor ao aplicar rotas",
                detail = ex.Message
            });
        }
    }

    /// <summary>Lista interfaces de túnel VPN no RouterOS (dedução automática).</summary>
    [HttpGet("vpn-tunnel-interfaces")]
    public async Task<ActionResult<List<RouterOsVpnTunnelInterfaceDto>>> GetVpnTunnelInterfaces(
        Guid routerId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Listando interfaces VPN do router {RouterId}", routerId);

        try
        {
            if (_routerOsServiceClient == null)
            {
                return BadRequest(new { message = "Serviço RouterOS não configurado" });
            }

            var interfaces = await _routerOsServiceClient.ListVpnTunnelInterfacesAsync(routerId, cancellationToken);
            return Ok(interfaces);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar interfaces VPN do router {RouterId}", routerId);
            return StatusCode(500, new
            {
                message = "Erro interno do servidor ao listar interfaces VPN",
                detail = ex.Message
            });
        }
    }

    /// <summary>
    /// Deleta uma rota do banco de dados diretamente (endpoint interno usado pelo serviço RouterOS)
    /// </summary>
    [HttpDelete("{id:guid}/force")]
    public async Task<IActionResult> ForceDelete(
        Guid routerId,
        Guid id,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deletando rota {StaticNetworkId} do banco (chamada interna do RouterOS)", id);

        try
        {
            var existing = await _staticNetworkService.GetByIdAsync(id, cancellationToken);
            if (existing == null)
            {
                return NotFound(new { message = $"Rota com ID {id} não encontrada" });
            }

            if (existing.RouterId != routerId)
            {
                return BadRequest(new { message = "A rota não pertence a este router" });
            }

            await _staticNetworkService.DeleteAsync(id, cancellationToken);
            _logger.LogInformation("Rota {StaticNetworkId} deletada do banco com sucesso", id);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao deletar rota {StaticNetworkId} do banco", id);
            return StatusCode(500, new
            {
                message = "Erro interno do servidor ao deletar rota",
                detail = ex.Message
            });
        }
    }
}
