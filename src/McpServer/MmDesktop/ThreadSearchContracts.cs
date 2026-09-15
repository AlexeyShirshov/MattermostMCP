namespace MattermostMCP.MmDesktop;

/// <summary>
/// Параметры поиска для followed threads.
/// </summary>
public sealed record ThreadSearchRequest
{
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Максимальное количество тредов для поиска.
    /// </summary>
    public int Limit { get; init; } = 100;

    /// <summary>
    /// Поиск только по заголовку треда (первому сообщению).
    /// </summary>
    public bool SearchTitleOnly { get; init; }
}

/// <summary>
/// Результат поиска по тредам.
/// </summary>
public sealed record ThreadSearchResult
{
    public string ThreadId { get; init; } = string.Empty;

    public string ChannelName { get; init; } = string.Empty;

    public string ChannelId { get; init; } = string.Empty;

    public string RootMessage { get; init; } = string.Empty;

    /// <summary>
    /// Текст конкретного сообщения, в котором найдено совпадение (не обязательно корень треда).
    /// </summary>
    public string MatchedMessage { get; init; } = string.Empty;

    public int ReplyCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastReplyAt { get; init; }

    public string MattermostUrl { get; init; } = string.Empty;
}
