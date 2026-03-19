using Automais.Core.DTOs;
using Automais.Core.Hubs;
using Automais.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Security.Cryptography;
using System.Text;
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
    private readonly IHubContext<RouterStatusHub> _routerStatusHub;
    private readonly ILogger<RoutersController> _logger;
    private readonly IConfiguration _configuration;

    public RoutersController(
        IRouterService routerService,
        IAuthService authService,
        IHubContext<RouterStatusHub> routerStatusHub,
        ILogger<RoutersController> logger,
        IConfiguration configuration)
    {
        _routerService = routerService;
        _authService = authService;
        _routerStatusHub = routerStatusHub;
        _logger = logger;
        _configuration = configuration;
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

            // Requisições de localhost ou com chave interna (ex.: routeros-service) dispensam autenticação
            if (HttpContext.IsLocalRequest() || HttpContext.IsInternalRequest(_configuration))
                return Ok(router);

            // Validar se o router pertence ao tenant do usuário autenticado
            var userTenantId = this.GetTenantId(_authService);
            if (!userTenantId.HasValue || router.TenantId != userTenantId.Value)
            {
                return StatusCode(403, new { message = "Acesso negado ao router." });
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
            const string provisionUsername = "automais-io";
            var provisionPassword = GenerateTemporaryRouterPassword();
            var created = await _routerService.CreateAsync(tenantId, dto, provisionUsername, provisionPassword, cancellationToken);
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
            _logger.LogInformation("📥 [API] Recebida requisição PUT para atualizar router {RouterId}", id);
            _logger.LogInformation("   Status: {Status}, LastSeenAt: {LastSeenAt}, Latency: {Latency}", dto.Status, dto.LastSeenAt, dto.Latency);
            _logger.LogInformation("   HardwareInfo: {HardwareInfo}, FirmwareVersion: {FirmwareVersion}, Model: {Model}",
                dto.HardwareInfo != null ? $"presente ({dto.HardwareInfo.Length} chars)" : "null", dto.FirmwareVersion, dto.Model);
            if (dto.RouterOsApiAuthStatus.HasValue || dto.RouterOsApiAuthCheckedAt.HasValue || dto.RouterOsApiAuthMessage != null)
            {
                _logger.LogInformation(
                    "   [Router API] Atualização de status da API RouterOS recebida: RouterOsApiAuthStatus={Status}, RouterOsApiAuthCheckedAt={CheckedAt}, RouterOsApiAuthMessage={Message}",
                    dto.RouterOsApiAuthStatus, dto.RouterOsApiAuthCheckedAt, dto.RouterOsApiAuthMessage ?? "(nula)");
            }
            
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

            if (dto.Status.HasValue || dto.LastSeenAt.HasValue || dto.Latency.HasValue
                || dto.RouterOsApiAuthStatus.HasValue || dto.RouterOsApiAuthCheckedAt.HasValue
                || dto.RouterOsApiAuthMessage != null)
            {
                try
                {
                    await _routerStatusHub.Clients.All.SendAsync(
                        "RouterStatusChanged",
                        new
                        {
                            routerId = updated.Id,
                            status = updated.Status.ToString(),
                            lastSeenAt = updated.LastSeenAt,
                            latency = updated.Latency,
                            routerOsApiAuthStatus = updated.RouterOsApiAuthStatus.ToString(),
                            routerOsApiAuthCheckedAt = updated.RouterOsApiAuthCheckedAt,
                            routerOsApiAuthMessage = updated.RouterOsApiAuthMessage,
                        },
                        cancellationToken);
                }
                catch (Exception hubEx)
                {
                    _logger.LogWarning(hubEx, "Falha ao notificar SignalR RouterStatusChanged para router {RouterId}", id);
                }
            }

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

    /// <summary>Senha temporária forte para API RouterOS até rotação automática.</summary>
    private static string GenerateTemporaryRouterPassword(int length = 28)
    {
        const string charset = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!#%+-_=?@^";
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
            sb.Append(charset[bytes[i] % charset.Length]);
        return sb.ToString();
    }
}

/// <summary>
/// DTO para atualização de senha do router
/// </summary>
public class UpdatePasswordDto
{
    public string Password { get; set; } = string.Empty;
}

