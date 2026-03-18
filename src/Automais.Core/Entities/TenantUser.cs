namespace Automais.Core.Entities;

/// <summary>
/// Representa um usuário vinculado a um tenant.
/// </summary>
public class TenantUser
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string? TemporaryPassword { get; set; }
    public DateTime? TemporaryPasswordExpiresAt { get; set; }
    public TenantUserRole Role { get; set; } = TenantUserRole.Viewer;
    public TenantUserStatus Status { get; set; } = TenantUserStatus.Invited;
    public DateTime? LastLoginAt { get; set; }
    public bool VpnEnabled { get; set; }
    public string? VpnDeviceName { get; set; }
    public string? VpnPublicKey { get; set; }
    public string? VpnPrivateKey { get; set; }
    public string? VpnIpAddress { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Quando a última tentativa de envio de e-mail (boas-vindas ou reset) falhou.</summary>
    public DateTime? EmailDeliveryFailedAt { get; set; }

    /// <summary>Resumo do erro SMTP ou motivo (ex.: serviço não configurado).</summary>
    public string? EmailDeliveryFailureMessage { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public ICollection<VpnNetworkMembership> VpnMemberships { get; set; } = new List<VpnNetworkMembership>();
}

public enum TenantUserRole
{
    Owner = 1,
    Admin = 2,
    Operator = 3,
    Viewer = 4
}

public enum TenantUserStatus
{
    Invited = 1,
    Active = 2,
    Suspended = 3,
    Disabled = 4
}


