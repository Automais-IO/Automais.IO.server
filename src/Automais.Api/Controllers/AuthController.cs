using Automais.Core.DTOs;
using Automais.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Automais.Api.Controllers;

/// <summary>
/// Autenticação: login e recuperação de senha.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
[Tags("Auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ITenantUserService _tenantUserService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IAuthService authService,
        ITenantUserService tenantUserService,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _tenantUserService = tenantUserService;
        _logger = logger;
    }

    /// <summary>
    /// Autentica um usuário e retorna um token JWT
    /// </summary>
    /// <response code="200">Login realizado com sucesso; retorna token e dados do usuário</response>
    /// <response code="400">Username ou password ausentes</response>
    /// <response code="401">Credenciais inválidas</response>
    /// <response code="500">Erro interno do servidor</response>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { message = "Username e password são obrigatórios" });
            }

            var response = await _authService.LoginAsync(request.Username, request.Password, cancellationToken);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Tentativa de login falhou: {Message}", ex.Message);
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro inesperado ao processar login");
            return StatusCode(500, new { message = "Erro interno do servidor ao processar login" });
        }
    }

    /// <summary>
    /// Envia uma nova senha temporária para o email do usuário
    /// </summary>
    /// <response code="200">Mensagem genérica de sucesso (não revela se o email existe)</response>
    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest(new { message = "Email é obrigatório" });
            }

            await _tenantUserService.ForgotPasswordAsync(request.Email, cancellationToken);

            // Sempre retornar sucesso para não revelar se o email existe
            return Ok(new { message = "Se o email estiver cadastrado, você receberá uma nova senha temporária em breve." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar forgot-password para email: {Email}", request.Email);
            // Retornar sucesso mesmo em caso de erro para não revelar informações
            return Ok(new { message = "Se o email estiver cadastrado, você receberá uma nova senha temporária em breve." });
        }
    }
}

/// <summary>
/// DTO para requisição de esqueci minha senha
/// </summary>
public class ForgotPasswordRequestDto
{
    public string Email { get; set; } = string.Empty;
}

