using Automais.Core.Entities;

namespace Automais.Core.DTOs;

public class TenantUserDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public TenantUserRole Role { get; set; }
    public TenantUserStatus Status { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool VpnEnabled { get; set; }
    public string? VpnDeviceName { get; set; }
    public string? VpnPublicKey { get; set; }
    public string? VpnIpAddress { get; set; }
    public IEnumerable<VpnNetworkSummaryDto> Networks { get; set; } = Enumerable.Empty<VpnNetworkSummaryDto>();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Indica falha recente ao enviar e-mail ao usuário (boas-vindas ou senha).</summary>
    public DateTime? EmailDeliveryFailedAt { get; set; }

    public string? EmailDeliveryFailureMessage { get; set; }
}

public class VpnNetworkSummaryDto
{
    public Guid NetworkId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Cidr { get; set; } = string.Empty;
}

public class CreateTenantUserDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public TenantUserRole Role { get; set; } = TenantUserRole.Viewer;
    public bool VpnEnabled { get; set; }
    public string? VpnDeviceName { get; set; }
    public string? VpnPublicKey { get; set; }
    public string? VpnIpAddress { get; set; }
    public IEnumerable<Guid> NetworkIds { get; set; } = Enumerable.Empty<Guid>();
}

public class UpdateTenantUserDto
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public TenantUserRole? Role { get; set; }
    public TenantUserStatus? Status { get; set; }
    public bool? VpnEnabled { get; set; }
    public string? VpnDeviceName { get; set; }
    public string? VpnPublicKey { get; set; }
    public string? VpnIpAddress { get; set; }
}

public class UpdateUserNetworksDto
{
    public IEnumerable<Guid> NetworkIds { get; set; } = Enumerable.Empty<Guid>();
}

/// <summary>
/// Resultado do reset de senha (indica se o email foi enviado).
/// </summary>
public class ResetPasswordResultDto
{
    public bool EmailSent { get; set; }
}


