namespace MattermostMCP.MmDesktop;

public sealed class MmDesktopOptions
{
    public const string SectionName = "MmDesktop";

    public string BaseUrl { get; init; } = "http://localhost:8080";

    public string? Username { get; init; }

    public string? Password { get; init; }

    public string? Token { get; init; }

    public int PerPage { get; init; } = 50;

    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Настройки OAuth внешнего сервиса
    /// (см. MmDesktop:AuthenticationOptions в appsettings.Local.json).
    /// </summary>
    public MmDesktopAuthenticationOptions? AuthenticationOptions { get; init; }
}

public sealed class MmDesktopAuthenticationOptions
{
    public string? AuthorityHost { get; init; }

    public string? ClientId { get; init; }

    public string? Scope { get; init; }
}
