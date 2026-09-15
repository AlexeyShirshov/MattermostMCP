using System.Security.Claims;

namespace MattermostMCP.Auth;

public static class AuthService
{
    /// <summary>
    /// Извлекает имя пользователя из ClaimsPrincipal.
    /// Сначала preferred_username (SSO / Keycloak), затем идентичность SSO
    /// (user_id), затем name, затем sub.
    /// </summary>
    public static string GetUsername(ClaimsPrincipal user)
    {
        return user.FindFirst("preferred_username")?.Value
            ?? user.FindFirst("user_id")?.Value
            ?? user.Identity?.Name
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "unknown";
    }

    /// <summary>
    /// Проверяет, входит ли пользователь в группу (реалм-роль Keycloak).
    /// </summary>
    public static bool HasRole(ClaimsPrincipal user, string role)
    {
        return user.HasClaim(c =>
            c.Type == "realm_access" &&
            c.Value.Contains(role, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Получает email пользователя из JWT.
    /// </summary>
    public static string? GetEmail(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.Email)?.Value
            ?? user.FindFirst("email")?.Value;
    }

    /// <summary>
    /// Получает MmDesktop-логин (AD login) пользователя.
    /// Обычно это часть email до @.
    /// </summary>
    public static string? GetMattermostLogin(ClaimsPrincipal user)
    {
        var email = GetEmail(user);
        if (email is not null && email.Contains('@'))
        {
            return email.Split('@')[0];
        }
        return GetUsername(user);
    }
}
