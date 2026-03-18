namespace Automais.Core.DTOs;

/// <summary>
/// DTO para requisição de login
/// </summary>
public class LoginRequestDto
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// DTO para resposta de login
/// </summary>
public class LoginResponseDto
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public UserInfoDto User { get; set; } = null!;

    /// <summary>True quando o login foi com senha temporária — o cliente deve exigir nova senha antes do restante do app.</summary>
    public bool MustChangePassword { get; set; }
}

/// <summary>Troca de senha (usuário autenticado, ex.: após senha temporária).</summary>
public class ChangePasswordRequestDto
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>
/// DTO com informações básicas do usuário
/// </summary>
public class UserInfoDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
}

/// <summary>
/// DTO para configuração VPN do usuário
/// </summary>
public class UserVpnConfigDto
{
    public string ConfigContent { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public bool VpnEnabled { get; set; }
    public string? VpnDeviceName { get; set; }
    public string? VpnPublicKey { get; set; }
    public string? VpnIpAddress { get; set; }
    public IEnumerable<UserAllowedRouteDto> AllowedRoutes { get; set; } = Enumerable.Empty<UserAllowedRouteDto>();
    public string? VpnGatewayIp { get; set; }
}

/// <summary>
/// DTO para rota permitida do usuário
/// </summary>
public class UserAllowedRouteDto
{
    public Guid Id { get; set; }
    public Guid RouterId { get; set; }
    public string RouterName { get; set; } = string.Empty;
    public string NetworkCidr { get; set; } = string.Empty;
    public string? Description { get; set; }
}

