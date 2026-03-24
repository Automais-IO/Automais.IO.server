using Automais.Api.Extensions;
using Automais.Core.DTOs;
using Automais.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Automais.Api.Controllers;

/// <summary>
/// Endpoints internos para o serviço Python webdevice (X-Automais-Internal-Key ou localhost).
/// </summary>
[ApiController]
[Route("api/internal")]
[Produces("application/json")]
public class InternalWebDeviceController : ControllerBase
{
    private readonly IDeviceService _deviceService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InternalWebDeviceController> _logger;

    public InternalWebDeviceController(
        IDeviceService deviceService,
        IConfiguration configuration,
        ILogger<InternalWebDeviceController> logger)
    {
        _deviceService = deviceService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("devices/{deviceId:guid}/webdevice/validate-agent")]
    public async Task<ActionResult<ValidateWebDeviceAgentResponseDto>> ValidateAgent(
        Guid deviceId,
        [FromBody] ValidateWebDeviceAgentRequestDto body,
        CancellationToken cancellationToken)
    {
        if (!HttpContext.IsLocalRequest() && !HttpContext.IsInternalRequest(_configuration))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Acesso negado." });
        }

        if (body == null || string.IsNullOrWhiteSpace(body.Token))
        {
            return BadRequest(new { message = "Token obrigatório." });
        }

        var result = await _deviceService.ValidateWebDeviceAgentAsync(deviceId, body.Token.Trim(), cancellationToken);
        if (result == null)
        {
            return NotFound(new { message = "Device não encontrado." });
        }

        if (!result.Valid)
        {
            _logger.LogWarning("Validação WebDevice falhou para device {DeviceId}", deviceId);
        }

        return Ok(result);
    }
}
