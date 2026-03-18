using Automais.Core.DTOs;
using Automais.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Automais.Api.Extensions;

namespace Automais.Api.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
public class VpnNetworksController : ControllerBase
{
    private readonly IVpnNetworkService _vpnNetworkService;
    private readonly IAuthService _authService;
    private readonly ILogger<VpnNetworksController> _logger;

    public VpnNetworksController(
        IVpnNetworkService vpnNetworkService,
        IAuthService authService,
        ILogger<VpnNetworksController> logger)
    {
        _vpnNetworkService = vpnNetworkService;
        _authService = authService;
        _logger = logger;
    }

    [HttpGet("tenants/{tenantId:guid}/vpn/networks")]
    public async Task<ActionResult<IEnumerable<VpnNetworkDto>>> GetByTenant(Guid tenantId, CancellationToken cancellationToken)
    {
        // Validar se o usuário tem acesso a este tenant
        var validationResult = this.ValidateTenantAccess(tenantId, _authService);
        if (validationResult != null)
        {
            return validationResult;
        }

        try
        {
            _logger.LogInformation("Listando redes VPN do tenant {TenantId}", tenantId);
            var networks = await _vpnNetworkService.GetByTenantAsync(tenantId, cancellationToken);
            return Ok(networks);
        }
        catch (Npgsql.NpgsqlException ex)
        {
            _logger.LogError(ex, "Erro de conexão com banco de dados ao listar redes VPN do tenant {TenantId}", tenantId);
            return StatusCode(503, new 
            { 
                message = "Resource temporarily unavailable",
                detail = "Erro ao conectar com o banco de dados. Tente novamente em alguns instantes.",
                innerException = ex.InnerException?.Message
            });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
        {
            _logger.LogError(ex, "Erro do Entity Framework ao listar redes VPN do tenant {TenantId}", tenantId);
            return StatusCode(503, new 
            { 
                message = "Resource temporarily unavailable",
                detail = "Erro ao acessar o banco de dados. Tente novamente em alguns instantes.",
                innerException = ex.InnerException?.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao listar redes VPN do tenant {TenantId}", tenantId);
            return StatusCode(500, new 
            { 
                message = "Erro interno do servidor",
                detail = ex.Message,
                innerException = ex.InnerException?.Message
            });
        }
    }

    [HttpPost("tenants/{tenantId:guid}/vpn/networks")]
    public async Task<ActionResult<VpnNetworkDto>> Create(Guid tenantId, [FromBody] CreateVpnNetworkDto dto, CancellationToken cancellationToken)
    {
        // Validar se o usuário tem acesso a este tenant
        var validationResult = this.ValidateTenantAccess(tenantId, _authService);
        if (validationResult != null)
        {
            return validationResult;
        }

        _logger.LogInformation("Criando rede VPN {Slug} para tenant {TenantId}", dto.Slug, tenantId);

        try
        {
            var created = await _vpnNetworkService.CreateAsync(tenantId, dto, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Erro ao criar rede VPN");
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Tenant não encontrado ao criar rede VPN");
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("vpn/networks/{id:guid}")]
    public async Task<ActionResult<VpnNetworkDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var network = await _vpnNetworkService.GetByIdAsync(id, cancellationToken);
        if (network == null)
        {
            return NotFound(new { message = $"Rede VPN com ID {id} não encontrada" });
        }

        // Validar se a rede VPN pertence ao tenant do usuário autenticado
        var userTenantId = this.GetTenantId(_authService);
        if (!userTenantId.HasValue || network.TenantId != userTenantId.Value)
        {
            return new ForbidResult();
        }

        return Ok(network);
    }

    [HttpPut("vpn/networks/{id:guid}")]
    public async Task<ActionResult<VpnNetworkDto>> Update(Guid id, [FromBody] UpdateVpnNetworkDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _vpnNetworkService.UpdateAsync(id, dto, cancellationToken);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Rede VPN não encontrada para atualização");
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Validação ao atualizar rede VPN");
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("vpn/networks/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _vpnNetworkService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("vpn/networks/{id:guid}/users")]
    public async Task<ActionResult<IEnumerable<TenantUserDto>>> GetUsers(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var users = await _vpnNetworkService.GetUsersAsync(id, cancellationToken);
            return Ok(users);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Rede VPN não encontrada ao listar usuários");
            return NotFound(new { message = ex.Message });
        }
    }
}


