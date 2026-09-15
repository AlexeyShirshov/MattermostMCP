using System.Text.Json.Serialization;

namespace MattermostMCP.MmDesktop.Models;

/// <summary>
/// Сущность, у которой есть идентификатор (адаптация внешнего контракта MmDesktop).
/// </summary>
public interface IIdentifiable
{
    string Id { get; }
}

public sealed record UserThread
{
    public string Id { get; init; } = string.Empty;

    public MattermostPost? Post { get; init; }

    public string? ChannelId { get; init; }

    public int UnreadReplies { get; init; }

    public DateTimeOffset LastReplyAt { get; init; }
}

public sealed class UserThreadsResponse
{
    public List<UserThread> Threads { get; set; } = [];

    public int Total { get; set; }

    public int TotalUnread { get; set; }

    public int TotalUnreadThreads { get; set; }
}

public sealed record MattermostChannel
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;
}

public sealed record MattermostPostThread
{
    [JsonPropertyName("order")]
    public List<string> Order { get; init; } = [];

    [JsonPropertyName("posts")]
    public Dictionary<string, MattermostPost> Posts { get; init; } = new(StringComparer.Ordinal);
}
