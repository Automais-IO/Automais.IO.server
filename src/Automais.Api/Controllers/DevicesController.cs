using Automais.Core.DTOs;
using Automais.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Automais.Api.Controllers;

[ApiController]
[Route("api")]
[Produces("application/json")]
public class DevicesController : ControllerBase
{
    private readonly IDeviceService _deviceService;
    private readonly ILogger<DevicesController> _logger;

    public DevicesController(
        IDeviceService deviceService,
        ILogger<DevicesController> logger)
    {
        _deviceService = deviceService;
        _logger = logger;
    }

    [HttpGet("tenants/{tenantId:guid}/devices")]
    public async Task<ActionResult<IEnumerable<DeviceDto>>> GetByTenant(Guid tenantId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Listando devices do tenant {TenantId}", tenantId);
        var devices = await _deviceService.GetByTenantAsync(tenantId, cancellationToken);
        return Ok(devices);
    }

    [HttpGet("applications/{applicationId:guid}/devices")]
    public async Task<ActionResult<IEnumerable<DeviceDto>>> GetByApplication(Guid applicationId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Listando devices da application {ApplicationId}", applicationId);
        var devices = await _deviceService.GetByApplicationAsync(applicationId, cancellationToken);
        return Ok(devices);
    }

    [HttpPost("tenants/{tenantId:guid}/devices")]
    public async Task<ActionResult<DeviceDto>> Create(Guid tenantId, [FromBody] CreateDeviceDto dto, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Criando device {Name} para tenant {TenantId}", dto.Name, tenantId);

        try
        {
            var created = await _deviceService.CreateAsync(tenantId, dto, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Erro de validação ao criar device");
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Tenant/application não encontrado ao criar device");
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("devices/{id:guid}")]
    public async Task<ActionResult<DeviceDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var device = await _deviceService.GetByIdAsync(id, cancellationToken);
        if (device == null)
        {
            return NotFound(new { message = $"Device com ID {id} não encontrado" });
        }

        return Ok(device);
    }

    [HttpPut("devices/{id:guid}")]
    public async Task<ActionResult<DeviceDto>> Update(Guid id, [FromBody] UpdateDeviceDto dto, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _deviceService.UpdateAsync(id, dto, cancellationToken);
            return Ok(updated);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Device não encontrado para atualização");
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Erro ao atualizar device");
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("devices/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _deviceService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Device não encontrado para exclusão");
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Não foi possível excluir device {DeviceId}", id);
            return BadRequest(new { message = ex.Message });
        }
    }
}


