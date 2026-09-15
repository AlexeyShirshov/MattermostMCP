using System.Text.Json.Serialization;

namespace MattermostMCP.MmDesktop.Models;

public sealed record MattermostPost(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("channel_id")] string ChannelId,
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("create_at")] long CreateAt,
    [property: JsonPropertyName("root_id")] string? RootId)
{
    public DateTimeOffset CreatedAt => DateTimeOffset.FromUnixTimeMilliseconds(CreateAt);
}

public sealed record MattermostThread(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("post")] MattermostPost Post,
    [property: JsonPropertyName("unread_replies")] int UnreadReplies);

public sealed record MattermostThreadsResponse(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("total_unread_threads")] int TotalUnreadThreads,
    [property: JsonPropertyName("threads")] IReadOnlyList<MattermostThread> Threads);

public sealed record MattermostUser(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("email")] string? Email) : IIdentifiable;

public sealed record MattermostTeam(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("display_name")] string DisplayName) : IIdentifiable;
