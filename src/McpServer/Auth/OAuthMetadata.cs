using System.Text.Json.Nodes;

namespace MattermostMCP.Auth;

/// <summary>
/// Строит OAuth-дискавери MCP-сервера по RFC 8414 (/.well-known/oauth-authorization-server)
/// и MCP OAuth-расширению: клиенты (Claude Code и др.) по этому документу сами выполняют
/// Authorization Code + PKCE против SSO Keycloak, чтобы получить/обновлять токен.
/// </summary>
public static class OAuthMetadata
{
    /// <summary>
    /// Достраивает <see cref="OAuthMetadataOptions"/> из секции "OAuth",
    /// подставляя issuer из платформенной секции "AuthOptions:AuthorityHost"
    /// (адрес SSO realm), если issuer не задан явно.
    /// </summary>
    public static OAuthMetadataOptions Resolve(IConfiguration configuration)
    {
        var o = configuration.GetSection(OAuthMetadataOptions.Section)
            .Get<OAuthMetadataOptions>()
            ?? new OAuthMetadataOptions();

        var issuer = (string.IsNullOrEmpty(o.Issuer)
                ? configuration["AuthOptions:AuthorityHost"]
                    ?? configuration["AuthOptions:Authority"]
                : o.Issuer)?.TrimEnd('/') ?? string.Empty;

        return o with
        {
            Issuer = issuer,
            AuthorizationEndpoint = NonEmpty(
                o.AuthorizationEndpoint, issuer, "/protocol/openid-connect/auth"),
            TokenEndpoint = NonEmpty(o.TokenEndpoint, issuer, "/protocol/openid-connect/token"),
            JwksUri = NonEmpty(o.JwksUri, issuer, "/protocol/openid-connect/certs"),
            ClientId = string.IsNullOrEmpty(o.ClientId)
                ? configuration["AuthOptions:ClientId"] ?? string.Empty
                : o.ClientId
        };
    }

    /// <summary>
    /// Возвращает RFC 8414-совместимую метадату в виде JSON-строки.
    /// Имена полей snake_case сохраняются как есть (без camelCase-политики).
    /// Нестандартные/недоступные поля (registration_endpoint) включаются только при их настройке.
    /// </summary>
    public static string BuildJson(OAuthMetadataOptions o)
    {
        var root = new JsonObject
        {
            ["issuer"] = o.Issuer,
            ["authorization_endpoint"] = o.AuthorizationEndpoint,
            ["token_endpoint"] = o.TokenEndpoint,
            ["jwks_uri"] = o.JwksUri,
            ["response_types_supported"] = new JsonArray("code"),
            ["grant_types_supported"] = new JsonArray("authorization_code", "refresh_token"),
            ["token_endpoint_auth_methods_supported"] = new JsonArray("none"),
            ["code_challenge_methods_supported"] = new JsonArray("S256"),
            ["scopes_supported"] = new JsonArray(
                [.. o.Scopes.Select(static s => (JsonNode?)s)])
        };

        if (!string.IsNullOrEmpty(o.RegistrationEndpoint))
        {
            root["registration_endpoint"] = o.RegistrationEndpoint;
        }

        return root.ToJsonString();
    }

    private static string NonEmpty(string value, string issuer, string suffix)
        => !string.IsNullOrEmpty(value)
            ? value
            : !string.IsNullOrEmpty(issuer)
                ? issuer + suffix
                : string.Empty;
}
