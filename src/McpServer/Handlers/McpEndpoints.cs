using System.Text.Json;
using MattermostMCP.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MattermostMCP.Handlers;

/// <summary>
/// Маппинг MCP-эндпоинтов (Streamable HTTP + legacy SSE + OAuth-discovery).
/// Вызывается из пайплайна Program.cs после <c>UseAuthentication()</c>/<c>UseAuthorization()</c>.
/// </summary>
public static partial class McpEndpoints
{
    public static IEndpointRouteBuilder MapMcpEndpoints(
        this IEndpointRouteBuilder endpoints,
        IHostEnvironment? env = null)
    {
        // SSE-аутентификация обязательна вне Local: локальная разработка
        // подключается напрямую (MCP-клиент без OAuth), поэтому auth отключаем.
        var requireAuth = !(env?.IsEnvironment("Local") ?? false);

        // OAuth-discovery (RFC 8414) за балансером — публичный.
        // В Local MCP-авторизация отключена — скрываем discovery, чтобы клиент
        // не пытался пройти OAuth и подключался анонимно.
        endpoints.MapGet("/.well-known/oauth-authorization-server", (IConfiguration config) =>
        {
            if (!requireAuth)
            {
                return Results.NotFound();
            }

            var metadata = OAuthMetadata.Resolve(config);
            return string.IsNullOrEmpty(metadata.Issuer)
                ? Results.NotFound()
                : Results.Text(
                    OAuthMetadata.BuildJson(metadata),
                    "application/json; charset=utf-8");
        });

        // Streamable HTTP: сигнальный GET /mcp отдаёт данные инициализации
        // для клиентов Streamable HTTP (Accept: application/json, text/event-stream).
        // Для legacy SSE-клиентов — редирект на /mcp/sse.
        endpoints.MapGet("/mcp", (HttpContext context, McpServer mcpServer) =>
        {
            var accepts = context.Request.Headers.Accept.ToString() ?? string.Empty;
            if (accepts.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers["Mcp-Session-Id"] = Guid.NewGuid().ToString("N");
                return Results.Json(mcpServer.GetServerInfo());
            }

            return Results.Redirect("/mcp/sse");
        });

        // Legacy SSE endpoint.
        var sseEndpoint = endpoints
            .MapGet("/mcp/sse", async (HttpContext context, McpServer mcpServer) =>
            {
                if (requireAuth && !(context.User.Identity?.IsAuthenticated ?? true))
                {
                    context.Response.StatusCode = 401;
                    await context.Response
                        .WriteAsync("Unauthorized", context.RequestAborted)
                        .ConfigureAwait(false);
                    return;
                }

                var username = AuthService.GetUsername(context.User);
                var logger = context.RequestServices.GetRequiredService<ILogger<McpServer>>();
                LogSseConnected(logger, username);

                context.Response.ContentType = "text/event-stream";
                context.Response.Headers["Cache-Control"] = "no-cache";
                context.Response.Headers["Connection"] = "keep-alive";
                context.Response.Headers["X-Accel-Buffering"] = "no";

                // Отправляем endpoint для сообщений.
                var endpointInfo = new
                {
                    jsonrpc = "2.0",
                    method = "endpoint",
                    @params = new { uri = "/mcp/message" }
                };
                await context.Response.WriteAsync(
                    $"data: {JsonSerializer.Serialize(endpointInfo, McpJsonRpc.JsonOptions)}\n\n",
                    context.RequestAborted).ConfigureAwait(false);
                await context.Response.Body
                    .FlushAsync(context.RequestAborted)
                    .ConfigureAwait(false);

                // Держим соединение открытым.
                var ct = context.RequestAborted;
                try
                {
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    LogSseDisconnected(logger, username);
                }
            });
        if (requireAuth)
        {
            sseEndpoint.RequireAuthorization(Startup.AUTHENTICATED_USER_POLICY);
        }

        // MCP POST endpoint (JSON-RPC). Streamable HTTP использует единый
        // endpoint /mcp (POST) — именно так подключается Claude Code;
        // legacy SSE использует /mcp/message. Логика общая.
        Task<IResult> HandlePost(HttpContext context, McpServer mcpServer)
        {
            if (requireAuth && !(context.User.Identity?.IsAuthenticated ?? true))
            {
                return Task.FromResult(Results.Unauthorized());
            }

            var username = AuthService.GetUsername(context.User);

            // Streamable HTTP: пробрасываем/выдаём идентификатор сессии.
            var sessionId = context.Request.Headers["Mcp-Session-Id"].ToString();
            if (string.IsNullOrEmpty(sessionId))
            {
                sessionId = Guid.NewGuid().ToString("N");
            }
            context.Response.Headers["Mcp-Session-Id"] = sessionId;

            return HandleMcpPostAsync(context, mcpServer, username);
        }

        // Streamable HTTP POST - единый endpoint /mcp (то, что шлёт Claude Code).
        var mcpPost = endpoints.MapPost("/mcp", HandlePost);
        if (requireAuth)
        {
            mcpPost.RequireAuthorization(Startup.AUTHENTICATED_USER_POLICY);
        }

        // Legacy SSE: JSON-RPC POST на отдельный endpoint.
        var messagePost = endpoints.MapPost("/mcp/message", HandlePost);
        if (requireAuth)
        {
            messagePost.RequireAuthorization(Startup.AUTHENTICATED_USER_POLICY);
        }

        return endpoints;
    }

    private static async Task<IResult> HandleMcpPostAsync(
        HttpContext context,
        McpServer mcpServer,
        string username)
    {
        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);

        if (string.IsNullOrEmpty(body))
        {
            return Results.BadRequest("Empty request body");
        }

        try
        {
            var request = JsonDocument.Parse(body);
            var response = await mcpServer.HandleRequestAsync(
                request.RootElement, context.User, context.RequestAborted).ConfigureAwait(false);

            return Results.Content(
                JsonSerializer.Serialize(response, McpJsonRpc.JsonOptions),
                "application/json");
        }
        catch (JsonException ex)
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<McpServer>>();
            LogInvalidJson(logger, ex, username);

            return Results.BadRequest(new
            {
                jsonrpc = "2.0",
                error = new { code = -32700, message = "Parse error" }
            });
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "MCP SSE connected: {User}")]
    private static partial void LogSseConnected(ILogger logger, string user);

    [LoggerMessage(Level = LogLevel.Information, Message = "MCP SSE disconnected: {User}")]
    private static partial void LogSseDisconnected(ILogger logger, string user);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid JSON from {User}")]
    private static partial void LogInvalidJson(ILogger logger, Exception exception, string user);
}
