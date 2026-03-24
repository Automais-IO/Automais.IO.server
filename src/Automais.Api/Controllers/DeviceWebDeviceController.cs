using Automais.Api.Extensions;
using Automais.Core.DTOs;
using Automais.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Automais.Api.Controllers;

/// <summary>
/// Gestão do túnel WebDevice (token no firmware) por device.
/// </summary>
[ApiController]
[Route("api/devices")]
[Produces("application/json")]
public class DeviceWebDeviceController : ControllerBase
{
    private readonly IDeviceService _deviceService;
    private readonly IAuthService _authService;
    private readonly ILogger<DeviceWebDeviceController> _logger;

    public DeviceWebDeviceController(
        IDeviceService deviceService,
        IAuthService authService,
        ILogger<DeviceWebDeviceController> logger)
    {
        _deviceService = deviceService;
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("{deviceId:guid}/web-device/enable")]
    public async Task<ActionResult<WebDeviceTokenIssuedDto>> Enable(Guid deviceId, CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId(_authService);
        if (!tenantId.HasValue)
            return Unauthorized(new { message = "Não autenticado." });

        try
        {
            var issued = await _deviceService.EnableWebDeviceAsync(deviceId, tenantId.Value, cancellationToken);
            return Ok(issued);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao habilitar WebDevice {DeviceId}", deviceId);
            return StatusCode(500, new { message = "Erro interno." });
        }
    }

    [HttpPost("{deviceId:guid}/web-device/regenerate-token")]
    public async Task<ActionResult<WebDeviceTokenIssuedDto>> RegenerateToken(Guid deviceId, CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId(_authService);
        if (!tenantId.HasValue)
            return Unauthorized(new { message = "Não autenticado." });

        try
        {
            var issued = await _deviceService.RegenerateWebDeviceTokenAsync(deviceId, tenantId.Value, cancellationToken);
            return Ok(issued);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao regenerar token WebDevice {DeviceId}", deviceId);
            return StatusCode(500, new { message = "Erro interno." });
        }
    }

    [HttpPost("{deviceId:guid}/web-device/disable")]
    public async Task<ActionResult<DeviceDto>> Disable(Guid deviceId, CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId(_authService);
        if (!tenantId.HasValue)
            return Unauthorized(new { message = "Não autenticado." });

        try
        {
            var dto = await _deviceService.DisableWebDeviceAsync(deviceId, tenantId.Value, cancellationToken);
            return Ok(dto);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao desabilitar WebDevice {DeviceId}", deviceId);
            return StatusCode(500, new { message = "Erro interno." });
        }
    }
}
