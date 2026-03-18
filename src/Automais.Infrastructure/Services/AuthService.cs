using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Automais.Core.DTOs;
using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Automais.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly ITenantUserRepository _userRepository;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService>? _logger;

    public AuthService(
        ITenantUserRepository userRepository,
        IConfiguration configuration,
        ILogger<AuthService>? logger = null)
    {
        _userRepository = userRepository;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<LoginResponseDto> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByEmailAsync(username, cancellationToken);

        if (user == null)
        {
            _logger?.LogWarning("Tentativa de login com email não encontrado: {Email}", username);
            throw new UnauthorizedAccessException("Credenciais inválidas");
        }

        if (user.Status != TenantUserStatus.Active)
        {
            _logger?.LogWarning("Tentativa de login com usuário inativo: {Email} (Status: {Status})", username, user.Status);
            throw new UnauthorizedAccessException("Usuário não está ativo");
        }

        if (!string.IsNullOrWhiteSpace(user.TemporaryPassword) && user.TemporaryPasswordExpiresAt.HasValue)
        {
            if (DateTime.UtcNow > user.TemporaryPasswordExpiresAt.Value)
            {
                _logger?.LogWarning("Tentativa de login com senha temporária expirada: {Email}", username);
                throw new UnauthorizedAccessException("Sua senha temporária expirou. Por favor, solicite uma nova senha.");
            }
        }

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            _logger?.LogWarning("Usuário sem senha configurada: {Email}", username);
            throw new UnauthorizedAccessException("Credenciais inválidas");
        }

        var providedPasswordHash = HashPassword(password);
        if (providedPasswordHash != user.PasswordHash)
        {
            _logger?.LogWarning("Tentativa de login com senha incorreta: {Email}", username);
            throw new UnauthorizedAccessException("Credenciais inválidas");
        }

        var usedTemporaryPassword = SafeEqualString(user.TemporaryPassword, password);

        user.LastLoginAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user, cancellationToken);

        var token = GenerateToken(user.Id, user.Email, user.TenantId, usedTemporaryPassword);
        var expiresAt = DateTime.UtcNow.AddHours(24);

        _logger?.LogInformation("Login bem-sucedido para usuário {Email} (ID: {UserId}){Temp}",
            user.Email, user.Id, usedTemporaryPassword ? " [senha temporária — troca obrigatória]" : "");

        return new LoginResponseDto
        {
            Token = token,
            ExpiresAt = expiresAt,
            MustChangePassword = usedTemporaryPassword,
            User = new UserInfoDto
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                TenantId = user.TenantId
            }
        };
    }

    public async Task<LoginResponseDto> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            throw new InvalidOperationException("A nova senha deve ter pelo menos 8 caracteres.");

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user == null || user.Status != TenantUserStatus.Active)
            throw new UnauthorizedAccessException("Sessão inválida.");

        if (string.IsNullOrWhiteSpace(user.PasswordHash) || HashPassword(currentPassword) != user.PasswordHash)
            throw new UnauthorizedAccessException("Senha atual incorreta.");

        user.PasswordHash = HashPassword(newPassword);
        user.TemporaryPassword = null;
        user.TemporaryPasswordExpiresAt = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user, cancellationToken);

        var token = GenerateToken(user.Id, user.Email, user.TenantId, false);
        return new LoginResponseDto
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            MustChangePassword = false,
            User = new UserInfoDto
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                TenantId = user.TenantId
            }
        };
    }

    public async Task<(bool Valid, bool MustChangePassword)> GetTokenPasswordChangeStateAsync(string token,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        try
        {
            var principal = ValidateTokenToPrincipal(token);
            if (principal == null)
                return (false, false);
            var must = string.Equals(principal.FindFirst("must_chpwd")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            return (true, must);
        }
        catch
        {
            return (false, false);
        }
    }

    public Guid? GetUserIdFromToken(string token)
    {
        var principal = ValidateTokenToPrincipal(token);
        if (principal == null)
            return null;
        var id = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("userId")?.Value;
        return Guid.TryParse(id, out var g) ? g : null;
    }

    public async Task<UserInfoDto?> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        try
        {
            var principal = ValidateTokenToPrincipal(token);
            if (principal == null)
                return null;

            var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("userId")?.Value;
            if (userIdClaim == null || !Guid.TryParse(userIdClaim, out var userId))
                return null;

            var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
            if (user == null || user.Status != TenantUserStatus.Active)
                return null;

            return new UserInfoDto
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                TenantId = user.TenantId
            };
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Erro ao validar token: {Error}", ex.Message);
            return null;
        }
    }

    public string GenerateToken(Guid userId, string email, Guid tenantId, bool mustChangePassword = false)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = GetSigningKey();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Email, email),
            new("tenantId", tenantId.ToString()),
            new("userId", userId.ToString())
        };
        if (mustChangePassword)
            claims.Add(new Claim("must_chpwd", "true"));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(24),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature)
        };

        var jwt = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(jwt);
    }

    private ClaimsPrincipal? ValidateTokenToPrincipal(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = GetSigningKey();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
        var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
        return principal;
    }

    private static bool SafeEqualString(string? a, string b)
    {
        if (string.IsNullOrEmpty(a) || a.Length != b.Length)
            return false;
        var r = 0;
        for (var i = 0; i < a.Length; i++)
            r |= a[i] ^ b[i];
        return r == 0;
    }

    private SymmetricSecurityKey GetSigningKey()
    {
        var secretKey = _configuration["Jwt:SecretKey"]
                        ?? _configuration["JWT_SECRET_KEY"]
                        ?? "AutomaisSecretKey_ChangeInProduction_Minimum32Characters";
        if (secretKey.Length < 32)
            secretKey = secretKey.PadRight(32, '0');
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
    }

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hashedBytes);
    }
}
