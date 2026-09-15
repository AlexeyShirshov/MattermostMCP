using System.Text.Json;
using MattermostMCP.MmDesktop;
using MattermostMCP.MmDesktop.Models;

namespace MattermostMCP.Handlers;

/// <summary>
/// Обработчик MCP tools (инструментов).
/// </summary>
public sealed partial class ToolsHandler(
    ThreadSearchService threadSearchService,
    ILogger<ToolsHandler> logger)
{
    private const int DefaultSearchLimit = 50;
    private const int DefaultFollowedLimit = 50;
    private const int MaxLimit = 1000;
    private const int MaxPage = 10000;

    private const string ToolSearch = "mattermost_search_threads";
    private const string ToolListFollowed = "mattermost_list_followed_threads";
    private const string ToolGetThread = "mattermost_get_thread";

    private static readonly JsonElement EmptyArguments = JsonDocument.Parse("{}").RootElement;

    private readonly ThreadSearchService _threadSearchService = threadSearchService;
    private readonly ILogger<ToolsHandler> _logger = logger;

    /// <summary>
    /// Возвращает список доступных инструментов с input/output-схемами и аннотациями.
    /// </summary>
    public IReadOnlyList<McpJsonRpc.McpTool> GetTools()
    {
        string[] searchRequired = ["query"];
        string[] threadRequired = ["threadId"];
        string[] noRequired = [];

        return new List<McpJsonRpc.McpTool>
        {
            new()
            {
                Name = ToolSearch,
                Description =
                    "Полнотекстовый поиск по отслеживаемым (followed) тредам пользователя в Mattermost. " +
                    "Сопоставление по словам: тред подходит, если ВСЕ слова запроса встречаются " +
                    "в сообщении (в любом порядке, не обязательно подряд). " +
                    "Возвращает пермалинк на найденное сообщение.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["query"] = new
                        {
                            type = "string",
                            description = "Слова для поиска в сообщениях тредов (например, \"релиз v11\")"
                        },
                        ["limit"] = new
                        {
                            type = "number",
                            description = "Максимум тредов для сканирования и выдачи (по умолчанию 50, максимум 1000)",
                            @default = DefaultSearchLimit
                        },
                        ["searchTitleOnly"] = new
                        {
                            type = "boolean",
                            description = "Искать только в первом сообщении треда (заголовке)",
                            @default = false
                        },
                        ["response_format"] = new
                        {
                            type = "string",
                            description = "Формат ответа: 'markdown' (по умолчанию) или 'json'",
                            @enum = new[] { "markdown", "json" },
                            @default = "markdown"
                        }
                    },
                    required = searchRequired
                },
                OutputSchema = ThreadsOutputSchema,
                Annotations = ReadOnlyAnnotations("Поиск по followed threads")
            },
            new()
            {
                Name = ToolListFollowed,
                Description =
                    "Возвращает список отслеживаемых (followed) тредов пользователя в Mattermost " +
                    "с пагинацией.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["limit"] = new
                        {
                            type = "number",
                            description = "Размер страницы (по умолчанию 50, максимум 1000)",
                            @default = DefaultFollowedLimit
                        },
                        ["page"] = new
                        {
                            type = "number",
                            description = "Номер страницы, начиная с 1",
                            @default = 1
                        },
                        ["response_format"] = new
                        {
                            type = "string",
                            description = "Формат ответа: 'markdown' (по умолчанию) или 'json'",
                            @enum = new[] { "markdown", "json" },
                            @default = "markdown"
                        }
                    },
                    required = noRequired
                },
                OutputSchema = ThreadsOutputSchema,
                Annotations = ReadOnlyAnnotations("Список followed threads")
            },
            new()
            {
                Name = ToolGetThread,
                Description =
                    "Возвращает полное содержимое конкретного треда (все сообщения) по его ID.",
                InputSchema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["threadId"] = new
                        {
                            type = "string",
                            description = "ID треда в Mattermost (root post id)"
                        },
                        ["response_format"] = new
                        {
                            type = "string",
                            description = "Формат ответа: 'markdown' (по умолчанию) или 'json'",
                            @enum = new[] { "markdown", "json" },
                            @default = "markdown"
                        }
                    },
                    required = threadRequired
                },
                OutputSchema = ThreadDetailsOutputSchema,
                Annotations = ReadOnlyAnnotations("Содержимое треда")
            }
        };
    }

    private static McpJsonRpc.McpToolAnnotations ReadOnlyAnnotations(string title)
        => new()
        {
            Title = title,
            ReadOnlyHint = true,
            DestructiveHint = false,
            IdempotentHint = true,
            OpenWorldHint = true
        };

    private static readonly object ThreadsOutputSchema = new
    {
        type = "object",
        properties = new
        {
            query = new { type = "string" },
            page = new { type = "integer" },
            limit = new { type = "integer" },
            total = new { type = "integer" },
            totalUnread = new { type = "integer" },
            hasMore = new { type = "boolean" },
            threads = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        threadId = new { type = "string" },
                        channelId = new { type = "string" },
                        channelName = new { type = "string" },
                        matchedMessage = new { type = "string" },
                        rootMessage = new { type = "string" },
                        url = new { type = "string" }
                    }
                }
            }
        }
    };

    private static readonly object ThreadDetailsOutputSchema = new
    {
        type = "object",
        properties = new
        {
            threadId = new { type = "string" },
            messageCount = new { type = "integer" },
            posts = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string" },
                        userId = new { type = "string" },
                        createdAt = new { type = "string" },
                        url = new { type = "string" },
                        message = new { type = "string" }
                    }
                }
            }
        }
    };

    /// <summary>
    /// Обрабатывает вызов инструмента.
    /// </summary>
    public async Task<JsonDocument> HandleCallAsync(
        JsonElement parameters,
        object? id,
        string? username,
        string? userId,
        CancellationToken ct = default)
    {
        if (parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("name", out var nameProperty) ||
            nameProperty.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(nameProperty.GetString()))
        {
            return McpJsonRpc.CreateError(
                id, -32602, "Invalid params: 'name' is required and must be a string.");
        }

        var toolName = nameProperty.GetString()!;
        var arguments =
            parameters.TryGetProperty("arguments", out var args) &&
            args.ValueKind == JsonValueKind.Object
                ? args
                : EmptyArguments;

        try
        {
            return toolName switch
            {
                ToolSearch => await HandleSearchFollowedThreads(arguments, id, userId, ct)
                    .ConfigureAwait(false),
                ToolListFollowed => await HandleGetFollowedThreads(arguments, id, userId, ct)
                    .ConfigureAwait(false),
                ToolGetThread => await HandleGetThreadDetails(arguments, id, ct)
                    .ConfigureAwait(false),
                _ => McpJsonRpc.CreateToolError(
                    id,
                    $"Неизвестный инструмент '{toolName}'. Доступные: " +
                    $"{ToolSearch}, {ToolListFollowed}, {ToolGetThread}.")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Детали исключения — только в лог; клиенту — actionable, но не раскрывающий
            // внутренности текст.
            LogToolFailed(_logger, ex, toolName);
            return McpJsonRpc.CreateToolError(
                id,
                "Не удалось выполнить инструмент: Mattermost недоступен или вернул ошибку. " +
                "Повторите попытку позже.");
        }
    }

    private async Task<JsonDocument> HandleSearchFollowedThreads(
        JsonElement args,
        object? id,
        string? userId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return McpJsonRpc.CreateToolError(
                id,
                "Mattermost-пользователь не найден. Проверьте связку аккаунта " +
                "(MmDesktop:Username / DeveloperUserName).");
        }

        if (!args.TryGetProperty("query", out var queryProperty) ||
            queryProperty.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(queryProperty.GetString()))
        {
            return McpJsonRpc.CreateToolError(
                id, "'query' обязателен и должен быть непустой строкой.");
        }

        var query = queryProperty.GetString()!;

        if (!TryGetLimit(args, DefaultSearchLimit, out var limit, out var limitError))
        {
            return McpJsonRpc.CreateToolError(id, limitError!);
        }

        if (!TryGetBoolean(args, "searchTitleOnly", defaultValue: false, out var searchTitleOnly, out var boolError))
        {
            return McpJsonRpc.CreateToolError(id, boolError!);
        }

        if (!TryGetResponseFormat(args, out var jsonFormat, out var formatError))
        {
            return McpJsonRpc.CreateToolError(id, formatError!);
        }

        var searchRequest = new ThreadSearchRequest
        {
            Query = query,
            Limit = limit,
            SearchTitleOnly = searchTitleOnly
        };

        var results = await _threadSearchService.SearchFollowedThreadsAsync(
            userId, searchRequest, ct).ConfigureAwait(false);

        LogSearch(_logger, userId, query, results.Count);

        var returned = results.Count > limit ? results.Take(limit).ToList() : results;

        var structured = new
        {
            query,
            page = 1,
            limit,
            total = results.Count,
            hasMore = false,
            threads = returned.Select(static r => new
            {
                threadId = r.ThreadId,
                channelId = r.ChannelId,
                channelName = r.ChannelName,
                matchedMessage = r.MatchedMessage,
                rootMessage = r.RootMessage,
                url = r.MattermostUrl
            }).ToList()
        };

        if (jsonFormat)
        {
            return McpJsonRpc.CreateStructuredResult(
                id, JsonSerializer.Serialize(structured, McpJsonRpc.JsonOptions), structured);
        }

        var lines = new List<string>
        {
            $"Найдено: {results.Count} тред(ов) по запросу \"{query}\"" +
            (returned.Count < results.Count ? $" (показано {returned.Count})" : string.Empty)
        };
        foreach (var r in returned)
        {
            var urlPart = string.IsNullOrEmpty(r.MattermostUrl)
                ? string.Empty
                : $" ({r.MattermostUrl})";
            // Ссылка ведёт на найденное сообщение; показываем и его текст, чтобы не искать вручную.
            lines.Add($"- [{r.ThreadId}] {r.ChannelName}{urlPart}: {r.MatchedMessage}");
        }

        return McpJsonRpc.CreateStructuredResult(id, string.Join("\n", lines), structured);
    }

    private async Task<JsonDocument> HandleGetFollowedThreads(
        JsonElement args,
        object? id,
        string? userId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return McpJsonRpc.CreateToolError(
                id,
                "Mattermost-пользователь не найден. Проверьте связку аккаунта " +
                "(MmDesktop:Username / DeveloperUserName).");
        }

        if (!TryGetLimit(args, DefaultFollowedLimit, out var limit, out var limitError))
        {
            return McpJsonRpc.CreateToolError(id, limitError!);
        }

        if (!TryGetPage(args, out var page, out var pageError))
        {
            return McpJsonRpc.CreateToolError(id, pageError!);
        }

        if (!TryGetResponseFormat(args, out var jsonFormat, out var formatError))
        {
            return McpJsonRpc.CreateToolError(id, formatError!);
        }

        var threads = await _threadSearchService.GetFollowedThreadsAsync(
            userId, limit, page, ct).ConfigureAwait(false);

        var hasMore = threads.Threads.Count < threads.Total;
        var items = threads.Threads.Select(t => new
        {
            threadId = t.Id,
            channelId = t.ChannelId ?? string.Empty,
            summary = Truncate(t.Post?.Message ?? string.Empty, 200),
            unreadReplies = t.UnreadReplies,
            lastReplyAt = t.LastReplyAt,
            url = _threadSearchService.BuildThreadUrl(t.Id)
        }).ToList();

        var structured = new
        {
            page,
            limit,
            total = threads.Total,
            totalUnread = threads.TotalUnread,
            hasMore,
            nextPage = hasMore ? page + 1 : (int?)null,
            threads = items
        };

        if (jsonFormat)
        {
            return McpJsonRpc.CreateStructuredResult(
                id, JsonSerializer.Serialize(structured, McpJsonRpc.JsonOptions), structured);
        }

        var lines = new List<string>
        {
            $"Followed threads: {threads.Total} (непрочитанных: {threads.TotalUnread}), " +
            $"страница {page}, размер {limit}, есть ещё: {(hasMore ? "да" : "нет")}"
        };
        foreach (var t in items)
        {
            var urlPart = string.IsNullOrEmpty(t.url) ? string.Empty : $" ({t.url})";
            lines.Add(
                $"- [{t.threadId}] (channel: {t.channelId}, replies: {t.unreadReplies})" +
                $"{urlPart}: {t.summary}");
        }

        return McpJsonRpc.CreateStructuredResult(id, string.Join("\n", lines), structured);
    }

    private async Task<JsonDocument> HandleGetThreadDetails(
        JsonElement args,
        object? id,
        CancellationToken ct)
    {
        if (!args.TryGetProperty("threadId", out var threadIdProperty) ||
            threadIdProperty.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(threadIdProperty.GetString()))
        {
            return McpJsonRpc.CreateToolError(
                id, "'threadId' обязателен и должен быть непустой строкой.");
        }

        if (!TryGetResponseFormat(args, out var jsonFormat, out var formatError))
        {
            return McpJsonRpc.CreateToolError(id, formatError!);
        }

        var threadId = threadIdProperty.GetString()!;

        var posts = await _threadSearchService
            .GetThreadPostsAsync(threadId, ct)
            .ConfigureAwait(false);
        if (posts is null || posts.Count == 0)
        {
            return McpJsonRpc.CreateToolError(id, $"Тред '{threadId}' не найден или недоступен.");
        }

        var items = posts.Select(post => new
        {
            id = post.Id,
            userId = post.UserId ?? string.Empty,
            createdAt = post.CreatedAt,
            url = _threadSearchService.BuildThreadUrl(post.Id),
            message = Truncate(post.Message ?? string.Empty, 500)
        }).ToList();

        var structured = new
        {
            threadId,
            messageCount = posts.Count,
            posts = items
        };

        if (jsonFormat)
        {
            return McpJsonRpc.CreateStructuredResult(
                id, JsonSerializer.Serialize(structured, McpJsonRpc.JsonOptions), structured);
        }

        var lines = new List<string> { $"Тред {threadId}: сообщений {posts.Count}" };
        foreach (var post in items)
        {
            var urlPart = string.IsNullOrEmpty(post.url) ? string.Empty : $" ({post.url})";
            var author = string.IsNullOrEmpty(post.userId) ? "unknown" : post.userId;
            lines.Add(
                $"- [{post.id}] {author} @ {post.createdAt:u}{urlPart}: {post.message}");
        }

        return McpJsonRpc.CreateStructuredResult(id, string.Join("\n", lines), structured);
    }

    /// <summary>
    /// Читает и валидирует необязательный параметр <c>limit</c>.
    /// </summary>
    private static bool TryGetLimit(
        JsonElement args, int defaultValue, out int limit, out string? error)
    {
        limit = defaultValue;
        error = null;

        if (!args.TryGetProperty("limit", out var limitProperty))
        {
            return true;
        }

        if (limitProperty.ValueKind != JsonValueKind.Number ||
            !limitProperty.TryGetInt32(out var value))
        {
            error = "'limit' должен быть целым числом.";
            return false;
        }

        if (value < 1 || value > MaxLimit)
        {
            error = $"'limit' должен быть в диапазоне от 1 до {MaxLimit}.";
            return false;
        }

        limit = value;
        return true;
    }

    /// <summary>
    /// Читает и валидирует необязательный параметр <c>page</c> (1-based).
    /// </summary>
    private static bool TryGetPage(JsonElement args, out int page, out string? error)
    {
        page = 1;
        error = null;

        if (!args.TryGetProperty("page", out var pageProperty))
        {
            return true;
        }

        if (pageProperty.ValueKind != JsonValueKind.Number ||
            !pageProperty.TryGetInt32(out var value))
        {
            error = "'page' должен быть целым числом.";
            return false;
        }

        if (value < 1 || value > MaxPage)
        {
            error = $"'page' должен быть в диапазоне от 1 до {MaxPage}.";
            return false;
        }

        page = value;
        return true;
    }

    private static bool TryGetBoolean(
        JsonElement args, string name, bool defaultValue, out bool value, out string? error)
    {
        value = defaultValue;
        error = null;

        if (!args.TryGetProperty(name, out var property))
        {
            return true;
        }

        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            error = $"'{name}' должен быть boolean.";
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool TryGetResponseFormat(
        JsonElement args, out bool jsonFormat, out string? error)
    {
        jsonFormat = false;
        error = null;

        if (!args.TryGetProperty("response_format", out var formatProperty))
        {
            return true;
        }

        if (formatProperty.ValueKind != JsonValueKind.String)
        {
            error = "'response_format' должен быть строкой: 'markdown' или 'json'.";
            return false;
        }

        switch (formatProperty.GetString()?.Trim().ToLowerInvariant())
        {
            case "markdown":
                return true;
            case "json":
                jsonFormat = true;
                return true;
            default:
                error = "Недопустимое 'response_format'. Используйте 'markdown' или 'json'.";
                return false;
        }
    }

    private static string Truncate(string text, int max)
        => text.Length > max ? text[..max] + "..." : text;

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "User {User} searched for '{Query}': found {Count} results")]
    private static partial void LogSearch(ILogger logger, string? user, string query, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Tool '{Tool}' failed")]
    private static partial void LogToolFailed(ILogger logger, Exception exception, string tool);
}
