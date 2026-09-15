using System.Security.Claims;
using System.Text.Json;
using MattermostMCP.Auth;
using MattermostMCP.Handlers;
using MattermostMCP.MmDesktop;

namespace MattermostMCP;

/// <summary>
/// MCP Server — обрабатывает MCP-запросы по протоколу JSON-RPC.
/// Поддерживает SSE и HTTP POST.
/// </summary>
public sealed partial class McpServer(
    ToolsHandler toolsHandler,
    ThreadSearchService threadSearchService,
    IConfiguration configuration,
    ILogger<McpServer> logger)
{
    private readonly ToolsHandler _toolsHandler = toolsHandler;
    private readonly ThreadSearchService _threadSearchService = threadSearchService;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<McpServer> _logger = logger;

    /// <summary>
    /// Обрабатывает MCP-запрос в формате JSON-RPC.
    /// </summary>
    public async Task<JsonDocument> HandleRequestAsync(
        JsonElement request,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var method = request.TryGetProperty("method", out var m)
            ? m.GetString()
            : null;

        // Сохраняем id запроса как есть (string или number), чтобы корректно его эхо-вернуть.
        var id = request.TryGetProperty("id", out var i) && i.ValueKind != JsonValueKind.Undefined
            ? JsonSerializer.Deserialize<object>(i.GetRawText())
            : null;

        var parameters = request.TryGetProperty("params", out var p)
            ? p
            : JsonDocument.Parse("{}").RootElement;

        var username = AuthService.GetUsername(user);

        // Локальная разработка (Local, без SSO): principal не аутентифицирован,
        // реального claim'а нет — GetUsername вернёт "unknown". Для читаемого лога
        // показываем пользователя из конфига (MmDesktop:DeveloperUserName).
        if (!(user.Identity?.IsAuthenticated ?? false))
        {
            username = _configuration["MmDesktop:DeveloperUserName"] ?? username;
        }

        LogMcpMethod(_logger, method, username);

        // Получаем MmDesktop userId для пользователя.
        // Логин/email выводим из реального principal тем же способом, что и в REST-контроллере.
        string? userId = null;
        var login = AuthService.GetMattermostLogin(user);
        var email = AuthService.GetEmail(user);

        // Локальная разработка (Local, без SSO): principal не аутентифицирован —
        // берём пользователя из конфига (MmDesktop:DeveloperUserName), чтобы инструменты
        // разрабатывались в MM. Проверяем именно отсутствие аутентификации, а не пустые
        // строки: при пустом principal GetMattermostLogin возвращает "unknown".
        if (!(user.Identity?.IsAuthenticated ?? false))
        {
            login = _configuration["MmDesktop:DeveloperUserName"];
        }

        if (!string.IsNullOrEmpty(login) || !string.IsNullOrEmpty(email))
        {
            userId = await _threadSearchService
                .ResolveUserIdAsync(login, email, ct)
                .ConfigureAwait(false);
        }

        return method switch
        {
            "initialize" => HandleInitialize(id),
            "tools/list" => HandleListTools(id),
            "tools/call" => await _toolsHandler
                .HandleCallAsync(parameters, id, username, userId, ct)
                .ConfigureAwait(false),
            _ => McpJsonRpc.CreateError(id, -32601, $"Method '{method}' not found")
        };
    }

    private JsonDocument HandleInitialize(object? id)
        => McpJsonRpc.CreateResult(id, GetServerInfo());

    /// <summary>
    /// Данные сервера для <c>initialize</c> и, в сущности, Streamable HTTP (<c>GET /mcp</c>):
    /// используемая версия протокола, capabilities и информация о сервере.
    /// </summary>
    public object GetServerInfo()
    {
        return new
        {
            protocolVersion = "2024-11-05",
            capabilities = new
            {
                tools = new { }
            },
            serverInfo = new
            {
                name = "mattermost-mcp-server",
                version = "1.0.0"
            }
        };
    }

    private JsonDocument HandleListTools(object? id)
    {
        var tools = _toolsHandler.GetTools();
        return McpJsonRpc.CreateResult(id, new { tools });
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "MCP method: {Method}, from {Username}")]
    private static partial void LogMcpMethod(ILogger logger, string? method, string username);
}
