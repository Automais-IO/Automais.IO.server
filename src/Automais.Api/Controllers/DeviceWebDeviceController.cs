using Automais.Api.Extensions;
using Automais.Core;
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
    private readonly IDeviceRepository _deviceRepository;
    private readonly IAuthService _authService;
    private readonly ILogger<DeviceWebDeviceController> _logger;

    public DeviceWebDeviceController(
        IDeviceService deviceService,
        IDeviceRepository deviceRepository,
        IAuthService authService,
        ILogger<DeviceWebDeviceController> logger)
    {
        _deviceService = deviceService;
        _deviceRepository = deviceRepository;
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("{devEui}/web-device/enable")]
    public async Task<ActionResult<WebDeviceTokenIssuedDto>> Enable(string devEui, CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId(_authService);
        if (!tenantId.HasValue)
            return Unauthorized(new { message = "Não autenticado." });

        if (!DevEuiNormalizer.TryNormalize(devEui, out var norm))
            return BadRequest(new { message = "DevEUI inválido." });

        var device = await _deviceRepository.GetByDevEuiAsync(tenantId.Value, norm, cancellationToken);
        if (device == null)
            return NotFound(new { message = "Device não encontrado." });

        try
        {
            var issued = await _deviceService.EnableWebDeviceAsync(device.Id, tenantId.Value, cancellationToken);
            return Ok(issued);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao habilitar WebDevice {DevEui}", norm);
            return StatusCode(500, new { message = "Erro interno." });
        }
    }

    [HttpPost("{devEui}/web-device/regenerate-token")]
    public async Task<ActionResult<WebDeviceTokenIssuedDto>> RegenerateToken(string devEui, CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId(_authService);
        if (!tenantId.HasValue)
            return Unauthorized(new { message = "Não autenticado." });

        if (!DevEuiNormalizer.TryNormalize(devEui, out var norm))
            return BadRequest(new { message = "DevEUI inválido." });

        var device = await _deviceRepository.GetByDevEuiAsync(tenantId.Value, norm, cancellationToken);
        if (device == null)
            return NotFound(new { message = "Device não encontrado." });

        try
        {
            var issued = await _deviceService.RegenerateWebDeviceTokenAsync(device.Id, tenantId.Value, cancellationToken);
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
            _logger.LogError(ex, "Erro ao regenerar token WebDevice {DevEui}", norm);
            return StatusCode(500, new { message = "Erro interno." });
        }
    }

    [HttpPost("{devEui}/web-device/disable")]
    public async Task<ActionResult<DeviceDto>> Disable(string devEui, CancellationToken cancellationToken)
    {
        var tenantId = this.GetTenantId(_authService);
        if (!tenantId.HasValue)
            return Unauthorized(new { message = "Não autenticado." });

        if (!DevEuiNormalizer.TryNormalize(devEui, out var norm))
            return BadRequest(new { message = "DevEUI inválido." });

        var device = await _deviceRepository.GetByDevEuiAsync(tenantId.Value, norm, cancellationToken);
        if (device == null)
            return NotFound(new { message = "Device não encontrado." });

        try
        {
            var dto = await _deviceService.DisableWebDeviceAsync(device.Id, tenantId.Value, cancellationToken);
            return Ok(dto);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao desabilitar WebDevice {DevEui}", norm);
            return StatusCode(500, new { message = "Erro interno." });
        }
    }
}
