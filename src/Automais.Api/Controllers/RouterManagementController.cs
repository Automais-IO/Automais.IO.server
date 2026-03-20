using Automais.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Automais.Api.Controllers;

/// <summary>
/// Controller para gerenciamento RouterOS via WebSocket
/// </summary>
[ApiController]
[Route("api/routers/{routerId:guid}/management")]
[Produces("application/json")]
public class RouterManagementController : ControllerBase
{
    private readonly IRouterOsWebSocketClient _webSocketClient;
    private readonly ILogger<RouterManagementController> _logger;
    private const string DEPRECATED_MESSAGE = "Este endpoint foi desativado. Use o servidor VPN (WebSocket) para gerenciamento RouterOS.";

    public RouterManagementController(
        IRouterOsWebSocketClient webSocketClient,
        ILogger<RouterManagementController> logger)
    {
        _webSocketClient = webSocketClient;
        _logger = logger;
    }

    /// <summary>
    /// Obtém o status da conexão RouterOS
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<RouterOsConnectionStatusDto>> GetConnectionStatus(
        Guid routerId,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Verificando status da conexão RouterOS: RouterId={RouterId}", routerId);
            var status = await _webSocketClient.GetConnectionStatusAsync(routerId, cancellationToken);
            
            if (!status.Success)
            {
                return BadRequest(status);
            }

            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao verificar status da conexão RouterOS: RouterId={RouterId}", routerId);
            return StatusCode(500, new { message = "Erro interno do servidor", error = ex.Message });
        }
    }

    [HttpGet("firewall")]
    public ActionResult<object> GetFirewallRules(Guid routerId) =>
        StatusCode(501, new { message = DEPRECATED_MESSAGE });

    [HttpGet("nat")]
    public ActionResult<object> GetNatRules(Guid routerId) =>
        StatusCode(501, new { message = DEPRECATED_MESSAGE });

    [HttpGet("static-networks")]
    public ActionResult<object> GetStaticNetworks(Guid routerId) =>
        StatusCode(501, new { message = DEPRECATED_MESSAGE });

    [HttpGet("interfaces")]
    public ActionResult<object> GetInterfaces(Guid routerId) =>
        StatusCode(501, new { message = DEPRECATED_MESSAGE });

    [HttpPost("terminal")]
    public ActionResult<object> ExecuteTerminalCommand(Guid routerId, [FromBody] object dto) =>
        StatusCode(501, new { message = DEPRECATED_MESSAGE });

    [HttpPost("system-info/refresh")]
    public ActionResult<object> RefreshSystemInfo(Guid routerId) =>
        StatusCode(501, new { message = DEPRECATED_MESSAGE });
}
