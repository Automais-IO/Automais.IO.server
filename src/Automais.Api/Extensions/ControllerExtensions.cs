using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Automais.Core.Interfaces;

namespace Automais.Api.Extensions;

/// <summary>
/// Extensões para ControllerBase para facilitar acesso a informações do usuário autenticado
/// </summary>
public static class ControllerExtensions
{
    /// <summary>
    /// Indica se a requisição veio de localhost (127.0.0.1 ou ::1).
    /// Usado para permitir serviços internos (ex.: routeros-service) sem Bearer token.
    /// Considera X-Forwarded-For quando atrás de proxy confiável.
    /// </summary>
    public static bool IsLocalRequest(this HttpContext context)
    {
        if (context?.Connection?.RemoteIpAddress == null)
            return false;
        var remote = context.Connection.RemoteIpAddress;
        if (System.Net.IPAddress.IsIPv4MappedToIPv6(remote))
            remote = System.Net.IPAddress.MapToIPv4(remote);
        if (remote.Equals(System.Net.IPAddress.Loopback) || remote.Equals(System.Net.IPAddress.IPv6Loopback))
            return true;
        var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',')[0].Trim();
            if (System.Net.IPAddress.TryParse(first, out var forwardedIp))
            {
                if (System.Net.IPAddress.IsIPv4MappedToIPv6(forwardedIp))
                    forwardedIp = System.Net.IPAddress.MapToIPv4(forwardedIp);
                if (forwardedIp.Equals(System.Net.IPAddress.Loopback) || forwardedIp.Equals(System.Net.IPAddress.IPv6Loopback))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Indica se a requisição é de um serviço interno com chave configurada (ex.: routeros-service atrás de proxy).
    /// Requer configuração "InternalApiKey", "Automais:InternalApiKey" ou env AUTOMAIS_INTERNAL_API_KEY,
    /// e header "X-Automais-Internal-Key" com o mesmo valor.
    /// </summary>
    public static bool IsInternalRequest(this HttpContext context, IConfiguration configuration)
    {
        if (context == null) return false;
        var key = configuration?["InternalApiKey"]
            ?? configuration?["Automais:InternalApiKey"]
            ?? Environment.GetEnvironmentVariable("AUTOMAIS_INTERNAL_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) return false;
        var header = context.Request.Headers["X-Automais-Internal-Key"].FirstOrDefault();
        return !string.IsNullOrWhiteSpace(header) && string.Equals(header.Trim(), key.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Obtém o TenantId do usuário autenticado a partir do token JWT
    /// Tenta primeiro via Claims (se JWT estiver configurado), depois via validação manual do token
    /// </summary>
    public static Guid? GetTenantId(this ControllerBase controller, IAuthService? authService = null)
    {
        // Tentar obter do User.Claims (se JWT estiver configurado)
        var tenantIdClaim = controller.User?.FindFirst("tenantId")?.Value;
        if (!string.IsNullOrEmpty(tenantIdClaim) && Guid.TryParse(tenantIdClaim, out var tenantIdFromClaims))
        {
            return tenantIdFromClaims;
        }

        // Se não tiver no Claims, tentar validar o token manualmente
        if (authService != null)
        {
            var authHeader = controller.Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
            {
                var token = authHeader.Substring("Bearer ".Length).Trim();
                try
                {
                    var userInfo = authService.ValidateTokenAsync(token).GetAwaiter().GetResult();
                    if (userInfo != null)
                    {
                        return userInfo.TenantId;
                    }
                }
                catch
                {
                    // Ignorar erros de validação
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Obtém o UserId do usuário autenticado a partir do token JWT
    /// </summary>
    public static Guid? GetUserId(this ControllerBase controller, IAuthService? authService = null)
    {
        // Tentar obter do User.Claims (se JWT estiver configurado)
        var userIdClaim = controller.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value 
            ?? controller.User?.FindFirst("userId")?.Value;
        if (!string.IsNullOrEmpty(userIdClaim) && Guid.TryParse(userIdClaim, out var userIdFromClaims))
        {
            return userIdFromClaims;
        }

        // Se não tiver no Claims, tentar validar o token manualmente
        if (authService != null)
        {
            var authHeader = controller.Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
            {
                var token = authHeader.Substring("Bearer ".Length).Trim();
                try
                {
                    var userInfo = authService.ValidateTokenAsync(token).GetAwaiter().GetResult();
                    if (userInfo != null)
                    {
                        return userInfo.Id;
                    }
                }
                catch
                {
                    // Ignorar erros de validação
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Valida se o tenantId fornecido corresponde ao tenantId do usuário autenticado
    /// Retorna ActionResult com Forbidden se não corresponder, ou null se estiver OK
    /// </summary>
    public static ActionResult? ValidateTenantAccess(this ControllerBase controller, Guid requestedTenantId, IAuthService? authService = null)
    {
        var userTenantId = controller.GetTenantId(authService);
        
        if (!userTenantId.HasValue)
        {
            return new UnauthorizedObjectResult(new { message = "Token de autenticação inválido ou expirado" });
        }

        if (userTenantId.Value != requestedTenantId)
        {
            return new ObjectResult(new { message = "Acesso negado ao tenant." }) { StatusCode = StatusCodes.Status403Forbidden };
        }

        return null; // Acesso permitido
    }
}
