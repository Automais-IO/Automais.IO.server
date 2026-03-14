using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Automais.Core.Interfaces;

namespace Automais.Api.Extensions;

/// <summary>
/// Extensões para ControllerBase para facilitar acesso a informações do usuário autenticado
/// </summary>
public static class ControllerExtensions
{
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
            return new ForbidResult();
        }

        return null; // Acesso permitido
    }
}
