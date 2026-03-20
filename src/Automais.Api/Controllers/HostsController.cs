using Automais.Api.Extensions;
using Automais.Core.DTOs;
using Automais.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Automais.Api.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
public class HostsController : ControllerBase
{
    private readonly IHostService _hostService;
    private readonly IAuthService _authService;
    private readonly ILogger<HostsController> _logger;
    private readonly IConfiguration _configuration;

    public HostsController(
        IHostService hostService,
        IAuthService authService,
        ILogger<HostsController> logger,
        IConfiguration configuration)
    {
        _hostService = hostService;
        _authService = authService;
        _logger = logger;
        _configuration = configuration;
    }

    [HttpGet("hosts")]
    public async Task<ActionResult<IEnumerable<HostDto>>> GetAll(CancellationToken cancellationToken)
    {
        try
        {
            var hosts = await _hostService.GetAllAsync(cancellationToken);
            return Ok(hosts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar hosts");
            return StatusCode(500, new { message = "Erro interno ao listar hosts", detail = ex.Message });
        }
    }

    [HttpGet("tenants/{tenantId:guid}/hosts")]
    public async Task<ActionResult<IEnumerable<HostDto>>> GetByTenant(Guid tenantId, CancellationToken cancellationToken)
    {
        var validationResult = this.ValidateTenantAccess(tenantId, _authService);
        if (validationResult != null)
            return validationResult;

        try
        {
            var hosts = await _hostService.GetByTenantIdAsync(tenantId, cancellationToken);
            return Ok(hosts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar hosts do tenant {TenantId}", tenantId);
            return StatusCode(500, new { message = "Erro interno ao listar hosts", detail = ex.Message });
        }
    }

    [HttpGet("hosts/{id:guid}")]
    public async Task<ActionResult<HostDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var host = await _hostService.GetByIdAsync(id, cancellationToken);
            if (host == null)
                return NotFound(new { message = $"Host com ID {id} não encontrado" });

            if (HttpContext.IsLocalRequest() || HttpContext.IsInternalRequest(_configuration))
                return Ok(host);

            var userTenantId = this.GetTenantId(_authService);
            if (!userTenantId.HasValue || host.TenantId != userTenantId.Value)
                return StatusCode(403, new { message = "Acesso negado ao host." });

            return Ok(host);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao obter host {HostId}", id);
            return StatusCode(500, new { message = "Erro interno ao obter host", detail = ex.Message });
        }
    }

    [HttpPost("tenants/{tenantId:guid}/hosts")]
    public async Task<ActionResult<HostDto>> Create(Guid tenantId, [FromBody] CreateHostDto dto, CancellationToken cancellationToken)
    {
        var validationResult = this.ValidateTenantAccess(tenantId, _authService);
        if (validationResult != null)
            return validationResult;

        try
        {
            var created = await _hostService.CreateAsync(tenantId, dto, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException dbEx)
        {
            _logger.LogError(dbEx, "Erro ao salvar host");
            return BadRequest(new { message = "Erro ao salvar no banco de dados", detail = dbEx.InnerException?.Message ?? dbEx.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao criar host");
            return StatusCode(500, new { message = "Erro interno ao criar host", detail = ex.Message });
        }
    }

    [HttpPut("hosts/{id:guid}")]
    public async Task<ActionResult<HostDto>> Update(Guid id, [FromBody] UpdateHostDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _hostService.GetByIdAsync(id, cancellationToken);
            if (existing == null)
                return NotFound(new { message = $"Host com ID {id} não encontrado" });

            if (!HttpContext.IsLocalRequest() && !HttpContext.IsInternalRequest(_configuration))
            {
                var userTenantId = this.GetTenantId(_authService);
                if (!userTenantId.HasValue || existing.TenantId != userTenantId.Value)
                    return StatusCode(403, new { message = "Acesso negado ao host." });
            }

            var updated = await _hostService.UpdateAsync(id, dto, cancellationToken);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar host {HostId}", id);
            return StatusCode(500, new { message = "Erro interno ao atualizar host", detail = ex.Message });
        }
    }

    [HttpDelete("hosts/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _hostService.GetByIdAsync(id, cancellationToken);
            if (existing == null)
                return NotFound(new { message = $"Host com ID {id} não encontrado" });

            if (!HttpContext.IsLocalRequest() && !HttpContext.IsInternalRequest(_configuration))
            {
                var userTenantId = this.GetTenantId(_authService);
                if (!userTenantId.HasValue || existing.TenantId != userTenantId.Value)
                    return StatusCode(403, new { message = "Acesso negado ao host." });
            }

            await _hostService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao deletar host {HostId}", id);
            return StatusCode(500, new { message = "Erro interno ao deletar host", detail = ex.Message });
        }
    }
}
