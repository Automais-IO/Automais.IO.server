using Automais.Core.DTOs;
using Automais.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Automais.Api.Extensions;

namespace Automais.Api.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
public class RoutersController : ControllerBase
{
    private readonly IRouterService _routerService;
    private readonly IAuthService _authService;
    private readonly ILogger<RoutersController> _logger;

    public RoutersController(
        IRouterService routerService,
        IAuthService authService,
        ILogger<RoutersController> logger)
    {
        _routerService = routerService;
        _authService = authService;
        _logger = logger;
    }

    [HttpGet("routers")]
    public async Task<ActionResult<IEnumerable<RouterDto>>> GetAll(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Listando todos os routers");
            var routers = await _routerService.GetAllAsync(cancellationToken);
            return Ok(routers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar todos os routers");
            return StatusCode(500, new 
            { 
                message = "Erro interno do servidor ao listar routers",
                detail = ex.Message,
                innerException = ex.InnerException?.Message,
                exceptionType = ex.GetType().Name
            });
        }
    }

    [HttpGet("tenants/{tenantId:guid}/routers")]
    public async Task<ActionResult<IEnumerable<RouterDto>>> GetByTenant(Guid tenantId, CancellationToken cancellationToken)
    {
        // Validar se o usuário tem acesso a este tenant
        var validationResult = this.ValidateTenantAccess(tenantId, _authService);
        if (validationResult != null)
        {
            return validationResult;
        }

        try
        {
            _logger.LogInformation("Listando routers do tenant {TenantId}", tenantId);
            var routers = await _routerService.GetByTenantIdAsync(tenantId, cancellationToken);
            return Ok(routers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar routers do tenant {TenantId}", tenantId);
            return StatusCode(500, new 
            { 
                message = "Erro interno do servidor ao listar routers",
                detail = ex.Message,
                innerException = ex.InnerException?.Message,
                exceptionType = ex.GetType().Name
            });
        }
    }

    [HttpGet("routers/{id:guid}")]
    public async Task<ActionResult<RouterDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var router = await _routerService.GetByIdAsync(id, cancellationToken);
            if (router == null)
            {
                return NotFound(new { message = $"Router com ID {id} não encontrado" });
            }

            // Validar se o router pertence ao tenant do usuário autenticado
            var userTenantId = this.GetTenantId(_authService);
            if (!userTenantId.HasValue || router.TenantId != userTenantId.Value)
            {
                return new ForbidResult();
            }

            return Ok(router);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao obter router {RouterId}", id);
            return StatusCode(500, new 
            { 
                message = "Erro interno do servidor ao obter router",
                detail = ex.Message,
                innerException = ex.InnerException?.Message,
                exceptionType = ex.GetType().Name
            });
        }
    }

    [HttpPost("tenants/{tenantId:guid}/routers")]
    public async Task<ActionResult<RouterDto>> Create(Guid tenantId, [FromBody] CreateRouterDto dto, CancellationToken cancellationToken)
    {
        // Validar se o usuário tem acesso a este tenant
        var validationResult = this.ValidateTenantAccess(tenantId, _authService);
        if (validationResult != null)
        {
            return validationResult;
        }

        _logger.LogInformation("Criando router {Name} para tenant {TenantId}", dto.Name, tenantId);

        try
        {
            var created = await _routerService.CreateAsync(tenantId, dto, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Tenant não encontrado ao criar router");
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Erro ao criar router");
            return BadRequest(new { message = ex.Message });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
        {
            // Erro específico do Entity Framework
            var innerException = dbEx.InnerException;
            var errorDetails = new
            {
                message = "Erro ao salvar no banco de dados",
                detail = dbEx.Message,
                innerException = innerException?.Message,
                innerExceptionType = innerException?.GetType().Name,
                stackTrace = dbEx.StackTrace
            };

            _logger.LogError(dbEx, "Erro do Entity Framework ao criar router {Name} para tenant {TenantId}. Inner: {InnerException}", 
                dto.Name, tenantId, innerException?.Message);

            // Verificar se é erro de foreign key
            if (innerException?.Message?.Contains("foreign key") == true || 
                innerException?.Message?.Contains("violates foreign key constraint") == true)
            {
                return BadRequest(new
                {
                    message = "Erro de validação: Rede VPN não encontrada",
                    detail = $"A rede VPN com ID '{dto.VpnNetworkId}' não existe no banco de dados. Verifique se o VpnNetworkId está correto.",
                    innerException = innerException.Message
                });
            }

            // Verificar se é erro de constraint única
            if (innerException?.Message?.Contains("unique constraint") == true ||
                innerException?.Message?.Contains("duplicate key") == true)
            {
                return BadRequest(new
                {
                    message = "Erro de validação: Dados duplicados",
                    detail = "Já existe um registro com esses dados. Verifique se o serial number ou outros campos únicos não estão duplicados.",
                    innerException = innerException.Message
                });
            }

            return StatusCode(500, errorDetails);
        }
        catch (Exception ex)
        {
            // Logar erro completo incluindo inner exception
            var errorDetails = new
            {
                message = "Erro interno do servidor ao criar router",
                detail = ex.Message,
                innerException = ex.InnerException?.Message,
                innerExceptionType = ex.InnerException?.GetType().Name,
                exceptionType = ex.GetType().Name,
                stackTrace = ex.StackTrace
            };

            if (ex.InnerException != null)
            {
                _logger.LogError(ex, "Erro ao criar router {Name} para tenant {TenantId}. Inner: {InnerException}", 
                    dto.Name, tenantId, ex.InnerException.Message);
            }
            else
            {
                _logger.LogError(ex, "Erro ao criar router {Name} para tenant {TenantId}", dto.Name, tenantId);
            }
            
            return StatusCode(500, errorDetails);
        }
    }

    [HttpPut("routers/{id:guid}")]
    public async Task<ActionResult<RouterDto>> Update(Guid id, [FromBody] UpdateRouterDto dto, CancellationToken cancellationToken)
    {
        try
        {
            // Log detalhado do que foi recebido
            _logger.LogInformation($"📥 [API] Recebida requisição PUT para atualizar router {id}");
            _logger.LogInformation($"   Status: {dto.Status}");
            _logger.LogInformation($"   LastSeenAt: {dto.LastSeenAt}");
            _logger.LogInformation($"   Latency: {dto.Latency}");
            _logger.LogInformation($"   HardwareInfo: {(dto.HardwareInfo != null ? $"presente ({dto.HardwareInfo.Length} chars)" : "null")}");
            _logger.LogInformation($"   FirmwareVersion: {dto.FirmwareVersion}");
            _logger.LogInformation($"   Model: {dto.Model}");
            
            // Log do JSON completo recebido
            try
            {
                var jsonPayload = JsonSerializer.Serialize(dto, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                _logger.LogInformation($"   📋 Payload completo recebido (JSON):\n{jsonPayload}");
            }
            catch (Exception jsonEx)
            {
                _logger.LogWarning(jsonEx, "   ⚠️ Erro ao serializar DTO para log");
            }
            
            var updated = await _routerService.UpdateAsync(id, dto, cancellationToken);
            _logger.LogInformation($"✅ [API] Router {id} atualizado com sucesso");
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Router não encontrado para atualização");
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Erro ao atualizar router");
            return BadRequest(new { message = ex.Message, detail = ex.InnerException?.Message });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
        {
            var innerException = dbEx.InnerException;
            _logger.LogError(dbEx, "Erro do Entity Framework ao atualizar router {RouterId}", id);
            return StatusCode(500, new
            {
                message = "Erro ao salvar no banco de dados",
                detail = dbEx.Message,
                innerException = innerException?.Message,
                innerExceptionType = innerException?.GetType().Name
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar router {RouterId}", id);
            return StatusCode(500, new
            {
                message = "Erro interno do servidor ao atualizar router",
                detail = ex.Message,
                innerException = ex.InnerException?.Message,
                exceptionType = ex.GetType().Name
            });
        }
    }

    [HttpDelete("routers/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _routerService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Router não encontrado para exclusão");
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao deletar router {RouterId}", id);
            return StatusCode(500, new 
            { 
                message = "Erro interno do servidor ao deletar router",
                detail = ex.Message,
                innerException = ex.InnerException?.Message,
                exceptionType = ex.GetType().Name
            });
        }
    }

    [HttpPost("routers/{id:guid}/test-connection")]
    public async Task<ActionResult<RouterDto>> TestConnection(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var router = await _routerService.TestConnectionAsync(id, cancellationToken);
            return Ok(router);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Router não encontrado para teste de conexão");
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Erro ao testar conexão");
            return BadRequest(new { message = ex.Message, detail = ex.InnerException?.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao testar conexão do router {RouterId}", id);
            return StatusCode(500, new
            {
                message = "Erro interno do servidor ao testar conexão",
                detail = ex.Message,
                innerException = ex.InnerException?.Message,
                exceptionType = ex.GetType().Name
            });
        }
    }

    /// <summary>
    /// Atualiza a senha do router e marca PasswordChanged como true.
    /// Usado quando a senha é alterada automaticamente na primeira conexão.
    /// </summary>
    [HttpPut("routers/{id:guid}/password")]
    public async Task<IActionResult> UpdatePassword(Guid id, [FromBody] UpdatePasswordDto dto, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Atualizando senha do router {RouterId}", id);
            await _routerService.UpdatePasswordAsync(id, dto.Password, cancellationToken);
            return Ok(new { message = "Senha atualizada com sucesso" });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Router não encontrado para atualização de senha");
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar senha do router {RouterId}", id);
            return StatusCode(500, new
            {
                message = "Erro interno do servidor ao atualizar senha",
                detail = ex.Message,
                innerException = ex.InnerException?.Message,
                exceptionType = ex.GetType().Name
            });
        }
    }
}

/// <summary>
/// DTO para atualização de senha do router
/// </summary>
public class UpdatePasswordDto
{
    public string Password { get; set; } = string.Empty;
}

