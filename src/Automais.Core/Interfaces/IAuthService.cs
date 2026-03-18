using Automais.Core.DTOs;

namespace Automais.Core.Interfaces;

/// <summary>
/// Serviço para autenticação de usuários
/// </summary>
public interface IAuthService
{
    Task<LoginResponseDto> LoginAsync(string username, string password, CancellationToken cancellationToken = default);
    Task<LoginResponseDto> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
    Task<UserInfoDto?> ValidateTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<(bool Valid, bool MustChangePassword)> GetTokenPasswordChangeStateAsync(string token, CancellationToken cancellationToken = default);
    Guid? GetUserIdFromToken(string token);
    string GenerateToken(Guid userId, string email, Guid tenantId, bool mustChangePassword = false);
}

