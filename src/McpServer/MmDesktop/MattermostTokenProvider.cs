using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MattermostMCP.MmDesktop;

/// <summary>
/// Хранит bearer-токен MmDesktop и лениво выполняет логин.
/// </summary>
public sealed partial class MattermostTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<MmDesktopOptions> options,
    ILogger<MattermostTokenProvider> logger)
{
    /// <summary>
    /// Имя named-клиента без auth-хендлера — используется только для логина,
    /// чтобы не уйти в рекурсию через <see cref="MattermostAuthHandler"/>.
    /// </summary>
    public const string AuthClientName = "MmDesktop.Auth";

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<MattermostTokenProvider> _logger = logger;
    private readonly MmDesktopOptions _options = options.Value;
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private string? _token;

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is not null)
        {
            return _token;
        }

        await _loginLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_token is not null)
            {
                return _token;
            }

            if (!string.IsNullOrWhiteSpace(_options.Token))
            {
                _token = _options.Token;
                return _token;
            }

            if (string.IsNullOrWhiteSpace(_options.Username) ||
                string.IsNullOrWhiteSpace(_options.Password))
            {
                throw new InvalidOperationException(
                    "MmDesktop credentials are not configured (Token or Username/Password).");
            }

            LogLoggingIn(_logger, _options.Username);
            _token = await LoginAsync(_options.Username, _options.Password, ct)
                .ConfigureAwait(false);
            return _token;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private async Task<string> LoginAsync(string loginId, string password, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient(AuthClientName);
        using var response = await client
            .PostAsJsonAsync(
                "api/v4/users/login", new { login_id = loginId, password }, SerializerOptions, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // Токен MmDesktop приходит в заголовке `Token`, не в теле.
        if (!response.Headers.TryGetValues("Token", out var values))
        {
            throw new InvalidOperationException("MmDesktop login response has no Token header.");
        }

        return values.First();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Logging into MmDesktop as {Username}")]
    private static partial void LogLoggingIn(ILogger logger, string? username);
}
