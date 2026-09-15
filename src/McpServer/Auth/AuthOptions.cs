namespace MattermostMCP.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string? Authority { get; init; }

    public string? MetadataAddress { get; init; }

    public string? Audience { get; init; }

    public string? Issuer { get; init; }

    public bool RequireHttpsMetadata { get; init; } = true;
}
