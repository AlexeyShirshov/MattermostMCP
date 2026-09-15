using System.Text.RegularExpressions;
using MattermostMCP.MmDesktop.Models;

namespace MattermostMCP.MmDesktop;

/// <summary>
/// Сервис поиска по followed threads, построенный поверх локального MmDesktop-клиента.
/// Заменяет собой прежний MattermostClient, сохраняя прежнее поведение и точки вызова.
/// </summary>
public sealed partial class ThreadSearchService(
    MattermostClient clients,
    MattermostTokenProvider tokenProvider,
    IConfiguration configuration,
    ILogger<ThreadSearchService> logger)
{
    private readonly MattermostClient _clients = clients;
    private readonly MattermostTokenProvider _tokenProvider = tokenProvider;

    // UI-адрес для построения корректных пермалинков. API не отдаёт имя команды (team),
    // поэтому в значении можно указать её, например https://mmdesktop.example.com/<team>.
    private readonly string _uiBaseUrl =
        configuration["MmDesktop:UiBaseUrl"]?.TrimEnd('/') ?? string.Empty;

    // Версия API followed threads: "v7" (глобальный роут, по умолчанию) | "v11" (per-team).
    // Значение по умолчанию выставляется в GetFollowedThreadsAsync.
    private readonly string _threadsApiVersion =
        configuration["MmDesktop:ThreadsApiVersion"] ?? string.Empty;

    private readonly ILogger<ThreadSearchService> _logger = logger;

    // Защита от повторного логина при конкурентных запросах: логинимся ровно один раз.
    private readonly SemaphoreSlim _authLock = new(1, 1);
    private bool _loggedIn;

    /// <summary>
    /// Гарантирует, что клиент авторизован (для password-режима логинится до первого запроса).
    /// </summary>
    private async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        if (_loggedIn)
        {
            return;
        }

        await _authLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_loggedIn)
            {
                return;
            }

            await _tokenProvider.GetTokenAsync(ct).ConfigureAwait(false);
            _loggedIn = true;
            LogAuthenticated(_logger);
        }
        finally
        {
            _authLock.Release();
        }
    }

    /// <summary>
    /// Сопоставляет логин/email пользователя с идентификатором пользователя MmDesktop.
    /// Сначала ищет по имени (логину), затем по email — как при резолве в прежнем клиенте.
    /// </summary>
    public async Task<string?> ResolveUserIdAsync(
        string? login,
        string? email,
        CancellationToken ct = default)
    {
        try
        {
            await EnsureAuthenticatedAsync(ct).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(login))
            {
                var userByName = await _clients
                    .FindUserByNameAsync(login, ct)
                    .ConfigureAwait(false);
                if (userByName is not null)
                {
                    return userByName.Id;
                }
            }

            if (!string.IsNullOrEmpty(email))
            {
                var userByEmail = await _clients
                    .FindUserByEmailAsync(email, ct)
                    .ConfigureAwait(false);
                if (userByEmail is not null)
                {
                    return userByEmail.Id;
                }
            }
        }
        catch (Exception ex)
        {
            LogResolveFailed(_logger, ex, login ?? string.Empty);
        }

        return null;
    }

    /// <summary>
    /// Получает followed threads пользователя.
    /// Версия API выбирается настройкой <c>MmDesktop:ThreadsApiVersion</c>:
    /// <list type="bullet">
    ///   <item><c>v7</c> (по умолчанию) — глобальный роут GET /users/{id}/threads;</item>
    ///   <item><c>v11</c> — per-team роут GET /users/{id}/teams/{team_id}/threads
    ///   с агрегацией по всем командам.</item>
    /// </list>
    /// </summary>
    public async Task<UserThreadsResponse> GetFollowedThreadsAsync(
        string userId,
        int limit = 100,
        int page = 1,
        CancellationToken ct = default)
    {
        var version = string.IsNullOrWhiteSpace(_threadsApiVersion)
            ? "v7"
            : _threadsApiVersion;

        var result = version.Equals("v11", StringComparison.OrdinalIgnoreCase)
            ? await GetFollowedThreadsV11Async(userId, limit, page, ct).ConfigureAwait(false)
            : await _clients
                .GetUserThreadsAsync(userId, limit, teamId: null, page, ct)
                .ConfigureAwait(false);

        LogFetched(_logger, result.Threads.Count, version, userId);
        return result;
    }

    /// <summary>
    /// v11 (per-team): собирает followed threads по всем командам пользователя и агрегирует.
    /// </summary>
    private async Task<UserThreadsResponse> GetFollowedThreadsV11Async(
        string userId,
        int limit,
        int page,
        CancellationToken ct)
    {
        var aggregated = new UserThreadsResponse();

        IReadOnlyList<MattermostTeam> teams;
        try
        {
            teams = await _clients.GetUserTeamsAsync(userId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogListTeamsFailed(_logger, ex, userId);
            throw;
        }

        foreach (var team in teams)
        {
            var perTeam = await _clients
                .GetUserThreadsAsync(userId, limit, team.Id, page, ct)
                .ConfigureAwait(false);
            aggregated.Threads.AddRange(perTeam.Threads);
            aggregated.Total += perTeam.Total;
            // В v11 unread лежит в total_unread_threads (не total_unread).
            aggregated.TotalUnread += perTeam.TotalUnreadThreads;
        }

        return aggregated;
    }

    /// <summary>
    /// Поиск по followed threads.
    /// </summary>
    public async Task<IReadOnlyList<ThreadSearchResult>> SearchFollowedThreadsAsync(
        string userId,
        ThreadSearchRequest searchRequest,
        CancellationToken ct = default)
    {
        var results = new List<ThreadSearchResult>();

        // Полнотекстовый поиск: разбиваем запрос на слова (токены) и требуем,
        // чтобы ВСЕ они встречались в сообщении — в любом порядке, не обязательно подряд.
        var terms = ExtractSearchTerms(searchRequest.Query);
        if (terms.Count == 0)
        {
            return results;
        }

        // Получаем все followed threads.
        var threads = await GetFollowedThreadsAsync(userId, searchRequest.Limit, page: 1, ct)
            .ConfigureAwait(false);

        foreach (var thread in threads.Threads)
        {
            // searchTitleOnly=true -> ищем только в корневом сообщении (заголовке) треда,
            // иначе -> по всем сообщениям (корень + ответы).
            IReadOnlyList<MattermostPost> searchable = searchRequest.SearchTitleOnly
                ? thread.Post is not null
                    ? new List<MattermostPost> { thread.Post }
                    : []
                : await GetSearchablePostsAsync(thread, ct).ConfigureAwait(false);

            // Находим конкретное сообщение, в котором есть ВСЕ слова запроса.
            // Ссылку строим именно на него, а не на корень треда.
            var matched = searchable.FirstOrDefault(p =>
                !string.IsNullOrEmpty(p.Message) &&
                ContainsAllTerms(p.Message, terms));

            if (matched is null)
            {
                continue;
            }

            var channelId = thread.ChannelId ?? thread.Post?.ChannelId;
            var channelName = string.IsNullOrEmpty(channelId)
                ? string.Empty
                : await GetChannelNameAsync(channelId, ct).ConfigureAwait(false);

            results.Add(new ThreadSearchResult
            {
                ThreadId = thread.Id,
                ChannelName = channelName,
                ChannelId = thread.ChannelId ?? thread.Post?.ChannelId ?? string.Empty,
                RootMessage = thread.Post is { Message.Length: > 500 }
                    ? thread.Post.Message[..500] + "..."
                    : thread.Post?.Message ?? string.Empty,
                MatchedMessage = Truncate(matched.Message ?? string.Empty, 500),
                ReplyCount = thread.UnreadReplies,
                CreatedAt = thread.Post?.CreatedAt ?? DateTimeOffset.MinValue,
                LastReplyAt = thread.LastReplyAt,
                // Пермалинк на найденное сообщение (pl/ принимает id любого поста).
                MattermostUrl = BuildThreadUrl(matched.Id)
            });
        }

        LogSearchFound(_logger, results.Count, searchRequest.Query);

        return results;
    }

    /// <summary>
    /// Обрезает текст до максимума символов с многоточием.
    /// </summary>
    private static string Truncate(string text, int max)
        => text.Length > max ? text[..max] + "..." : text;

    /// <summary>
    /// Разбивает запрос на слова (токены). Разделители — любые не-буквенно-цифровые
    /// символы (regex \w и классы, чтобы учитывать кириллицу и цифры). Пустые отбрасываются.
    /// </summary>
    private static List<string> ExtractSearchTerms(string query)
        => [.. Regex.Split(query.ToLowerInvariant(), @"[^\p{L}\p{N}]+")
            .Select(static t => t.Trim())
            .Where(static t => t.Length > 0)];

    /// <summary>
    /// True, если сообщение содержит каждое слово запроса (регистронезависимо, как подстроку).
    /// </summary>
    private static bool ContainsAllTerms(string message, IReadOnlyList<string> terms)
    {
        var lower = message.ToLowerInvariant();
        return terms.All(lower.Contains);
    }

    // Возвращает сообщения треда для поиска; при ошибке получения — только корневой пост.
    private async Task<IReadOnlyList<MattermostPost>> GetSearchablePostsAsync(
        UserThread thread,
        CancellationToken ct)
    {
        var posts = await GetThreadPostsAsync(thread.Id, ct).ConfigureAwait(false);
        if (posts is { Count: > 0 })
        {
            return posts;
        }

        return thread.Post is not null
            ? new List<MattermostPost> { thread.Post }
            : new List<MattermostPost>();
    }

    private async Task<string> GetChannelNameAsync(
        string channelId,
        CancellationToken ct)
    {
        try
        {
            var channel = await _clients.FindChannelByIdAsync(channelId, ct).ConfigureAwait(false);
            if (channel is not null)
            {
                if (!string.IsNullOrEmpty(channel.DisplayName))
                {
                    return channel.DisplayName;
                }

                if (!string.IsNullOrEmpty(channel.Name))
                {
                    return channel.Name;
                }
            }
        }
        catch (Exception ex)
        {
            LogChannelFailed(_logger, ex, channelId);
        }

        return channelId;
    }

    /// <summary>
    /// Получает все посты треда (корень + ответы) в порядке, который отдаёт API.
    /// </summary>
    public async Task<IReadOnlyList<MattermostPost>?> GetThreadPostsAsync(
        string threadId,
        CancellationToken ct = default)
    {
        try
        {
            var thread = await _clients.GetThreadPostsAsync(threadId, ct).ConfigureAwait(false);
            if (thread?.Posts is null)
            {
                return null;
            }

            // Сохраняем порядок сообщений так, как отдаёт API.
            IReadOnlyList<MattermostPost> ordered =
            [
                .. thread.Order
                    .Select(id => thread.Posts.GetValueOrDefault(id))
                    .Where(static p => p is not null)
                    .Cast<MattermostPost>()
            ];

            return ordered.Count > 0 ? ordered : [.. thread.Posts.Values];
        }
        catch (Exception ex)
        {
            LogThreadPostsFailed(_logger, ex, threadId);
            return null;
        }
    }

    /// <summary>
    /// Строит пермалинк на тред в UI MmDesktop (используется при выводе ссылок на треки).
    /// Возвращает пустую строку, если UI-базовый адрес не задан.
    /// </summary>
    public string BuildThreadUrl(string threadId)
        => string.IsNullOrEmpty(_uiBaseUrl)
            ? string.Empty
            : $"{_uiBaseUrl}/pl/{threadId}";

    [LoggerMessage(Level = LogLevel.Information, Message = "MmDesktop automatic login succeeded")]
    private static partial void LogAuthenticated(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to resolve MmDesktop user, login: {Login}")]
    private static partial void LogResolveFailed(ILogger logger, Exception exception, string login);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Fetched {Count} followed threads ({ApiVersion}) for user {UserId}")]
    private static partial void LogFetched(
        ILogger logger, int count, string apiVersion, string userId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "failed to list teams for user {UserId} (v11)")]
    private static partial void LogListTeamsFailed(
        ILogger logger, Exception exception, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to get channel {ChannelId}")]
    private static partial void LogChannelFailed(
        ILogger logger, Exception exception, string channelId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to fetch thread posts for {ThreadId}")]
    private static partial void LogThreadPostsFailed(
        ILogger logger, Exception exception, string threadId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Search found {Count} results for query: {Query}")]
    private static partial void LogSearchFound(ILogger logger, int count, string query);
}
