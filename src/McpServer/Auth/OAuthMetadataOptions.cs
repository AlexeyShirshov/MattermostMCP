namespace MattermostMCP.Auth;

/// <summary>
/// Опции OAuth-дискавери MCP-сервера (секция конфигурации "OAuth").
/// Обычно issuer совпадает с <c>AuthOptions:AuthorityHost</c> (адрес SSO realm),
/// а client_id задаётся явно; при пустых значениях они подставляются автоматически
/// (см. <see cref="OAuthMetadata.Resolve"/>).
/// </summary>
public sealed record OAuthMetadataOptions
{
    public const string Section = "OAuth";

    /// <summary>
    /// Issuer (realm) Keycloak, напр. https://keycloak.example.com/auth/realms/dev.
    /// </summary>
    public string Issuer { get; init; } = string.Empty;

    /// <summary>
    /// Authorization endpoint (OAuth2 code flow), выводится из issuer при пустом значении.
    /// </summary>
    public string AuthorizationEndpoint { get; init; } = string.Empty;

    /// <summary>Token endpoint, выводится из issuer при пустом значении.</summary>
    public string TokenEndpoint { get; init; } = string.Empty;

    /// <summary>
    /// JWKS URI для проверки подписи токена, выводится из issuer при пустом значении.
    /// </summary>
    public string JwksUri { get; init; } = string.Empty;

    /// <summary>
    /// Endpoint динамической регистрации OAuth-клиентов; задаётся только если Keycloak
    /// его разрешает.
    /// </summary>
    public string RegistrationEndpoint { get; init; } = string.Empty;

    /// <summary>Идентификатор public-клиента Keycloak для OAuth-потока.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Доступные scope.</summary>
    public string[] Scopes { get; init; } = ["openid", "profile", "email", "offline_access"];
}
