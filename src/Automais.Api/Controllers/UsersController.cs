using Automais.Core.DTOs;
using Automais.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Automais.Api.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly ITenantUserService _tenantUserService;
    private readonly IUserAllowedRouteRepository _userAllowedRouteRepository;
    private readonly IAllowedNetworkRepository _allowedNetworkRepository;
    private readonly IRouterRepository _routerRepository;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        ITenantUserService tenantUserService,
        IUserAllowedRouteRepository userAllowedRouteRepository,
        IAllowedNetworkRepository allowedNetworkRepository,
        IRouterRepository routerRepository,
        ILogger<UsersController> logger)
    {
        _tenantUserService = tenantUserService;
        _userAllowedRouteRepository = userAllowedRouteRepository;
        _allowedNetworkRepository = allowedNetworkRepository;
        _routerRepository = routerRepository;
        _logger = logger;
    }

    [HttpGet("tenants/{tenantId:guid}/users")]
    public async Task<ActionResult<IEnumerable<TenantUserDto>>> GetByTenant(Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Listando usuários do tenant {TenantId}", tenantId);
            var users = await _tenantUserService.GetByTenantAsync(tenantId, cancellationToken);
            return Ok(users);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar usuários do tenant {TenantId}", tenantId);
            return StatusCode(500, new { message = "Erro interno do servidor ao listar usuários", error = ex.Message });
        }
    }

    [HttpPost("tenants/{tenantId:guid}/users")]
    public async Task<ActionResult<TenantUserDto>> Create(Guid tenantId, [FromBody] CreateTenantUserDto dto, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Criando usuário {Email} para tenant {TenantId}", dto.Email, tenantId);

        try
        {
            var created = await _tenantUserService.CreateAsync(tenantId, dto, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Erro de validação ao criar usuário");
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Tenant não encontrado ao criar usuário");
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("users/{id:guid}")]
    public async Task<ActionResult<TenantUserDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var user = await _tenantUserService.GetByIdAsync(id, cancellationToken);
        if (user == null)
        {
            return NotFound(new { message = $"Usuário com ID {id} não encontrado" });
        }

        return Ok(user);
    }

    [HttpPut("users/{id:guid}")]
    public async Task<ActionResult<TenantUserDto>> Update(Guid id, [FromBody] UpdateTenantUserDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _tenantUserService.UpdateAsync(id, dto, cancellationToken);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Usuário não encontrado para atualização");
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("users/{id:guid}/networks")]
    public async Task<ActionResult<TenantUserDto>> UpdateNetworks(Guid id, [FromBody] UpdateUserNetworksDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _tenantUserService.UpdateNetworksAsync(id, dto, cancellationToken);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Usuário/rede não encontrado ao atualizar redes do usuário");
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Erro ao atualizar redes do usuário");
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("users/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _tenantUserService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("users/{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _tenantUserService.ResetPasswordAsync(id, cancellationToken);
            var message = result.EmailSent
                ? "Senha resetada com sucesso. Um email com a nova senha temporária foi enviado."
                : "Senha resetada com sucesso. O email não pôde ser enviado; informe a nova senha ao usuário manualmente.";
            return Ok(new { message, emailSent = result.EmailSent });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Usuário não encontrado para reset de senha");
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao resetar senha do usuário {UserId}. Tipo: {ExceptionType}, Mensagem: {Message}",
                id, ex.GetType().Name, ex.Message);
            return StatusCode(500, new
            {
                message = "Erro interno do servidor ao resetar senha",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Catálogo de redes permitidas (por peer) dos routers do tenant — para atribuição ao usuário VPN.
    /// </summary>
    [HttpGet("tenants/{tenantId:guid}/allowed-networks-for-users")]
    public async Task<ActionResult<IEnumerable<AllowedNetworkForUserDto>>> GetAllowedNetworksForUsersCatalog(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        try
        {
            var networks = await _allowedNetworkRepository.GetAllByTenantIdAsync(tenantId, cancellationToken);
            var routers = await _routerRepository.GetByTenantIdAsync(tenantId, cancellationToken);
            var routerByPeer = routers.Where(x => x.VpnPeerId != null).ToDictionary(x => x.VpnPeerId!.Value);

            var list = networks.Select(n =>
            {
                routerByPeer.TryGetValue(n.VpnPeerId, out var rt);
                return new AllowedNetworkForUserDto
                {
                    AllowedNetworkId = n.Id,
                    RouterId = rt?.Id ?? Guid.Empty,
                    RouterName = rt?.Name ?? "Unknown",
                    NetworkCidr = n.NetworkCidr,
                    Description = n.Description
                };
            }).ToList();

            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar redes permitidas do tenant {TenantId}", tenantId);
            return StatusCode(500, new { message = "Erro interno do servidor", error = ex.Message });
        }
    }

    /// <summary>
    /// Redes permitidas atribuídas ao usuário (VPN).
    /// </summary>
    [HttpGet("users/{id:guid}/allowed-networks")]
    public async Task<ActionResult<IEnumerable<AllowedNetworkForUserDto>>> GetUserAllowedNetworks(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = await _userAllowedRouteRepository.GetByUserIdAsync(id, cancellationToken);
            var dto = rows.Select(r => new AllowedNetworkForUserDto
            {
                AllowedNetworkId = r.AllowedNetworkId,
                RouterId = r.RouterId,
                RouterName = r.Router?.Name ?? "Unknown",
                NetworkCidr = r.NetworkCidr,
                Description = r.Description
            }).ToList();

            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao listar redes permitidas do usuário {UserId}", id);
            return StatusCode(500, new { message = "Erro interno do servidor", error = ex.Message });
        }
    }

    /// <summary>
    /// Atualiza quais redes permitidas o usuário pode usar na VPN.
    /// </summary>
    [HttpPut("users/{id:guid}/allowed-networks")]
    public async Task<IActionResult> UpdateUserAllowedNetworks(
        Guid id,
        [FromBody] UpdateUserAllowedNetworksDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            await _userAllowedRouteRepository.ReplaceUserRoutesAsync(id, dto.AllowedNetworkIds, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Erro ao atualizar redes permitidas do usuário {UserId}", id);
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao atualizar redes permitidas do usuário {UserId}", id);
            return StatusCode(500, new { message = "Erro interno do servidor", error = ex.Message });
        }
    }
}


