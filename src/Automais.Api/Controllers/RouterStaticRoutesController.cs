using Automais.Core.DTOs;
using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Automais.Api.Controllers;

/// <summary>
/// Controller para gerenciamento de rotas estáticas dos Routers
/// </summary>
[ApiController]
[Route("api/routers/{routerId:guid}/routes")]
[Produces("application/json")]
public class RouterStaticRoutesController : ControllerBase
{
    private readonly IRouterStaticRouteService _routeService;
    private readonly IRouterOsServiceClient? _routerOsServiceClient;
    private readonly ILogger<RouterStaticRoutesController> _logger;

    public RouterStaticRoutesController(
        IRouterStaticRouteService routeService,
        IRouterOsServiceClient? routerOsServiceClient,
        ILogger<RouterStaticRoutesController> logger)
    {
        _routeService = routeService;
        _routerOsServiceClient = routerOsServiceClient;
        _logger = logger;
    }

    /// <summary>
    /// Lista todas as rotas estáticas de um router
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<RouterStaticRouteDto>>> GetByRouter(
        Guid routerId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Listando rotas estáticas do router {RouterId}", routerId);
            var routes = await _routeService.GetByRouterIdAsync(routerId, cancellationToken);
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
    public async Task<ActionResult<RouterStaticRouteDto>> GetById(
        Guid routerId,
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var route = await _routeService.GetByIdAsync(id, cancellationToken);
            if (route == null)
            {
                return NotFound(new { message = $"Rota com ID {id} não encontrada" });
            }

            // Verificar se a rota pertence ao router
            if (route.RouterId != routerId)
            {
                return BadRequest(new { message = "A rota não pertence a este router" });
            }

            return Ok(route);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao obter rota {RouteId} do router {RouterId}", id, routerId);
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
    public async Task<ActionResult<RouterStaticRouteDto>> Create(
        Guid routerId,
        [FromBody] CreateRouterStaticRouteDto dto,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Criando rota estática para router {RouterId}: Destination={Destination}, Gateway={Gateway}", 
            routerId, dto.Destination, dto.Gateway);

        try
        {
            var created = await _routeService.CreateAsync(routerId, dto, cancellationToken);
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
    public async Task<ActionResult<RouterStaticRouteDto>> Update(
        Guid routerId,
        Guid id,
        [FromBody] UpdateRouterStaticRouteDto dto,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Atualizando rota {RouteId} do router {RouterId}", id, routerId);

        try
        {
            // Verificar se a rota existe e pertence ao router
            var existing = await _routeService.GetByIdAsync(id, cancellationToken);
            if (existing == null)
            {
                return NotFound(new { message = $"Rota com ID {id} não encontrada" });
            }

            if (existing.RouterId != routerId)
            {
                return BadRequest(new { message = "A rota não pertence a este router" });
            }

            var updated = await _routeService.UpdateAsync(id, dto, cancellationToken);
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
            _logger.LogError(ex, "Erro ao atualizar rota {RouteId} do router {RouterId}", id, routerId);
            return StatusCode(500, new 
            { 
                message = "Erro interno do servidor ao atualizar rota",
                detail = ex.Message
            });
        }
    }

    /// <summary>
    /// Deleta uma rota estática
    /// Fluxo:
    /// 1. Marca a rota como PendingRemove (não deleta imediatamente)
    /// 2. Tenta remover do RouterOS
    /// 3. Se sucesso: deleta do banco
    /// 4. Se falha: marca como Error e deixa no banco para retry pelo sync periódico
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        Guid routerId,
        Guid id,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Solicitando remoção da rota {RouteId} do router {RouterId}", id, routerId);

        try
        {
            // Verificar se a rota existe e pertence ao router
            var existing = await _routeService.GetByIdAsync(id, cancellationToken);
            if (existing == null)
            {
                return NotFound(new { message = $"Rota com ID {id} não encontrada" });
            }

            if (existing.RouterId != routerId)
            {
                return BadRequest(new { message = "A rota não pertence a este router" });
            }

            // Se não tem RouterOsId, a rota nunca foi aplicada no RouterOS, pode deletar direto
            if (string.IsNullOrWhiteSpace(existing.RouterOsId))
            {
                _logger.LogInformation("Rota {RouteId} nunca foi aplicada no RouterOS, deletando diretamente do banco", id);
                await _routeService.DeleteAsync(id, cancellationToken);
                return NoContent();
            }

            // Marcar como PendingRemove primeiro
            await _routeService.UpdateRouteStatusAsync(new UpdateRouteStatusDto
            {
                RouteId = id,
                Status = RouterStaticRouteStatus.PendingRemove
            }, cancellationToken);

            // Tentar remover do RouterOS
            if (_routerOsServiceClient == null)
            {
                _logger.LogWarning("Serviço RouterOS não configurado, rota {RouteId} marcada como PendingRemove para processamento posterior", id);
                return Accepted(new { message = "Rota marcada para remoção. Será processada pelo serviço RouterOS." });
            }

            var success = await _routerOsServiceClient.RemoveRouteAsync(routerId, existing.RouterOsId, cancellationToken);
            
            if (success)
            {
                // Remoção bem-sucedida no RouterOS, deletar do banco
                _logger.LogInformation("Rota {RouteId} removida com sucesso do RouterOS, deletando do banco", id);
                await _routeService.DeleteAsync(id, cancellationToken);
                return NoContent();
            }
            else
            {
                // Falha ao remover do RouterOS, manter como PendingRemove para retry pelo sync periódico
                // O sync processará rotas PendingRemove e tentará remover novamente
                _logger.LogWarning("Falha ao remover rota {RouteId} do RouterOS, mantendo como PendingRemove para retry pelo sync periódico", id);
                await _routeService.UpdateRouteStatusAsync(new UpdateRouteStatusDto
                {
                    RouteId = id,
                    Status = RouterStaticRouteStatus.PendingRemove,
                    ErrorMessage = "Falha ao remover rota do RouterOS. Será tentado novamente pelo sync periódico."
                }, cancellationToken);
                
                return Accepted(new 
                { 
                    message = "Falha ao remover rota do RouterOS. A rota permanecerá marcada como PendingRemove e será tentada novamente pelo sync periódico.",
                    routeId = id,
                    status = "PendingRemove"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar remoção da rota {RouteId} do router {RouterId}", id, routerId);
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
        [FromBody] BatchUpdateRoutesDto dto,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Atualizando status em lote: Router={RouterId}, Add={AddCount}, Remove={RemoveCount}", 
            routerId, dto.RoutesToAdd.Count(), dto.RoutesToRemove.Count());

        try
        {
            await _routeService.BatchUpdateStatusAsync(routerId, dto, cancellationToken);
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
    public async Task<IActionResult> UpdateRouteStatus(
        [FromBody] UpdateRouteStatusDto dto,
        CancellationToken cancellationToken)
    {
        // Log detalhado do POST recebido
        _logger.LogInformation("📥 POST /api/routers/{RouterId}/routes/update-status recebido:", 
            dto.RouteId);
        _logger.LogInformation("   RouteId: {RouteId}", dto.RouteId);
        _logger.LogInformation("   Status: {Status}", dto.Status);
        _logger.LogInformation("   RouterOsId: '{RouterOsId}'", dto.RouterOsId ?? "null");
        _logger.LogInformation("   ErrorMessage: '{ErrorMessage}'", dto.ErrorMessage ?? "null");
        _logger.LogInformation("   Gateway: '{Gateway}' (tipo: {Type})", 
            dto.Gateway ?? "null", dto.Gateway?.GetType().Name ?? "null");

        try
        {
            await _routeService.UpdateRouteStatusAsync(dto, cancellationToken);
            _logger.LogInformation("✅ Status da rota {RouteId} atualizado com sucesso. Gateway: '{Gateway}'", 
                dto.RouteId, dto.Gateway ?? "não informado");
            return Ok(new { message = "Status atualizado com sucesso" });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Rota não encontrada para atualizar status");
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar status da rota {RouteId}", dto.RouteId);
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

            // Buscar rotas pendentes
            var routes = await _routeService.GetByRouterIdAsync(routerId, cancellationToken);
            var routesToAdd = routes.Where(r => r.Status == RouterStaticRouteStatus.PendingAdd).ToList();
            var routesToRemove = routes.Where(r => r.Status == RouterStaticRouteStatus.PendingRemove).ToList();

            var results = new List<object>();

            // Aplicar rotas para adicionar
            foreach (var route in routesToAdd)
            {
                try
                {
                    var (success, gatewayUsed) = await _routerOsServiceClient.AddRouteAsync(routerId, route, cancellationToken);
                    
                    if (success)
                    {
                        // Atualizar status para Applied, incluindo gateway usado pelo RouterOS
                        // gatewayUsed pode ser um IP ou o nome da interface VPN detectada automaticamente
                        // Sempre passar o gateway, mesmo que seja string vazia (para garantir sincronização)
                        var gatewayToUpdate = gatewayUsed ?? string.Empty;
                        _logger.LogInformation(
                            "Atualizando rota {RouteId} com status Applied. Gateway recebido do RouterOS: '{GatewayUsed}' (tipo: {Type}, será passado: '{GatewayToUpdate}')", 
                            route.Id, gatewayUsed ?? "null", gatewayUsed?.GetType().Name ?? "null", gatewayToUpdate);
                        
                        var updateDto = new UpdateRouteStatusDto
                        {
                            RouteId = route.Id,
                            Status = RouterStaticRouteStatus.Applied,
                            Gateway = gatewayToUpdate  // Gateway realmente usado pelo RouterOS (pode ser interface se gateway estava vazio)
                        };
                        
                        _logger.LogInformation(
                            "DTO criado para atualização: RouteId={RouteId}, Status={Status}, Gateway='{Gateway}' (tipo: {Type})", 
                            updateDto.RouteId, updateDto.Status, updateDto.Gateway ?? "null", updateDto.Gateway?.GetType().Name ?? "null");
                        
                        await _routeService.UpdateRouteStatusAsync(updateDto, cancellationToken);
                        
                        _logger.LogInformation(
                            "Rota {RouteId} aplicada com sucesso. Gateway usado: '{GatewayUsed}'", 
                            route.Id, gatewayUsed ?? "não informado");

                        results.Add(new
                        {
                            routeId = route.Id,
                            action = "add",
                            success = true
                        });
                    }
                    else
                    {
                        // Marcar como erro
                        await _routeService.UpdateRouteStatusAsync(new UpdateRouteStatusDto
                        {
                            RouteId = route.Id,
                            Status = RouterStaticRouteStatus.Error,
                            ErrorMessage = "Falha ao adicionar rota no RouterOS"
                        }, cancellationToken);

                        results.Add(new
                        {
                            routeId = route.Id,
                            action = "add",
                            success = false,
                            error = "Falha ao adicionar rota no RouterOS"
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao adicionar rota {RouteId}", route.Id);
                    await _routeService.UpdateRouteStatusAsync(new UpdateRouteStatusDto
                    {
                        RouteId = route.Id,
                        Status = RouterStaticRouteStatus.Error,
                        ErrorMessage = ex.Message
                    }, cancellationToken);

                    results.Add(new
                    {
                        routeId = route.Id,
                        action = "add",
                        success = false,
                        error = ex.Message
                    });
                }
            }

            // Aplicar rotas para remover
            foreach (var route in routesToRemove)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(route.RouterOsId))
                    {
                        // Se não tem RouterOsId, apenas deletar do banco
                        await _routeService.DeleteAsync(route.Id, cancellationToken);
                        results.Add(new
                        {
                            routeId = route.Id,
                            action = "remove",
                            success = true,
                            message = "Rota removida do banco (não estava no RouterOS)"
                        });
                        continue;
                    }

                    var success = await _routerOsServiceClient.RemoveRouteAsync(routerId, route.RouterOsId, cancellationToken);
                    
                    if (success)
                    {
                        // Deletar do banco após remoção bem-sucedida
                        await _routeService.DeleteAsync(route.Id, cancellationToken);
                        results.Add(new
                        {
                            routeId = route.Id,
                            action = "remove",
                            success = true
                        });
                    }
                    else
                    {
                        // Falha ao remover, manter como PendingRemove para retry pelo sync periódico
                        await _routeService.UpdateRouteStatusAsync(new UpdateRouteStatusDto
                        {
                            RouteId = route.Id,
                            Status = RouterStaticRouteStatus.PendingRemove,
                            ErrorMessage = "Falha ao remover rota do RouterOS. Será tentado novamente pelo sync periódico."
                        }, cancellationToken);

                        results.Add(new
                        {
                            routeId = route.Id,
                            action = "remove",
                            success = false,
                            error = "Falha ao remover rota do RouterOS. Será tentado novamente pelo sync periódico."
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao remover rota {RouteId}", route.Id);
                    await _routeService.UpdateRouteStatusAsync(new UpdateRouteStatusDto
                    {
                        RouteId = route.Id,
                        Status = RouterStaticRouteStatus.Error,
                        ErrorMessage = ex.Message
                    }, cancellationToken);

                    results.Add(new
                    {
                        routeId = route.Id,
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
    /// Este endpoint é chamado pelo serviço RouterOS após remover a rota com sucesso do RouterOS.
    /// Não tenta remover do RouterOS novamente, apenas deleta do banco.
    /// </summary>
    [HttpDelete("{id:guid}/force")]
    public async Task<IActionResult> ForceDelete(
        Guid routerId,
        Guid id,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deletando rota {RouteId} do banco (chamada interna do RouterOS)", id);

        try
        {
            // Verificar se a rota existe e pertence ao router
            var existing = await _routeService.GetByIdAsync(id, cancellationToken);
            if (existing == null)
            {
                return NotFound(new { message = $"Rota com ID {id} não encontrada" });
            }

            if (existing.RouterId != routerId)
            {
                return BadRequest(new { message = "A rota não pertence a este router" });
            }

            // Deletar diretamente do banco (já foi removida do RouterOS pelo serviço)
            await _routeService.DeleteAsync(id, cancellationToken);
            _logger.LogInformation("Rota {RouteId} deletada do banco com sucesso", id);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao deletar rota {RouteId} do banco", id);
            return StatusCode(500, new 
            { 
                message = "Erro interno do servidor ao deletar rota",
                detail = ex.Message
            });
        }
    }

}

