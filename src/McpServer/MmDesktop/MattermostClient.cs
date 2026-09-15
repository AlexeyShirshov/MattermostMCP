using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MattermostMCP.MmDesktop.Models;

namespace MattermostMCP.MmDesktop;

/// <summary>
/// Тонкий typed-клиент MmDesktop REST API (ExternalServices.MmDesktop).
/// Авторизация — через <see cref="MattermostAuthHandler"/>.
/// </summary>
public sealed class MattermostClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient = httpClient;

    public Task<MattermostUser> GetMeAsync(CancellationToken ct)
        => GetAsync<MattermostUser>("api/v4/users/me", ct);

    public Task<MattermostUser?> FindUserByNameAsync(string username, CancellationToken ct)
        => GetOrDefaultAsync<MattermostUser>(
            $"api/v4/users/username/{Uri.EscapeDataString(username)}", ct);

    public Task<MattermostUser?> FindUserByEmailAsync(string email, CancellationToken ct)
        => GetOrDefaultAsync<MattermostUser>(
            $"api/v4/users/email/{Uri.EscapeDataString(email)}", ct);

    public Task<MattermostChannel?> FindChannelByIdAsync(string channelId, CancellationToken ct)
        => GetOrDefaultAsync<MattermostChannel>(
            $"api/v4/channels/{Uri.EscapeDataString(channelId)}", ct);

    public async Task<IReadOnlyList<MattermostTeam>> GetUserTeamsAsync(
        string userId, CancellationToken ct)
        => await GetAsync<MattermostTeam[]>(
            $"api/v4/users/{Uri.EscapeDataString(userId)}/teams", ct).ConfigureAwait(false);

    /// <summary>
    /// Followed threads пользователя. При <paramref name="teamId"/> == null — глобальный
    /// роут v7 (<c>GET /users/{id}/threads</c>), иначе per-team v11
    /// (<c>GET /users/{id}/teams/{team_id}/threads</c>).
    /// </summary>
    public async Task<UserThreadsResponse> GetUserThreadsAsync(
        string userId,
        int limit,
        string? teamId,
        int page,
        CancellationToken ct)
    {
        var uri = string.IsNullOrEmpty(teamId)
            ? $"api/v4/users/{Uri.EscapeDataString(userId)}/threads" +
              $"?per_page={limit}&page={page}&deleted=false&unread=false"
            : $"api/v4/users/{Uri.EscapeDataString(userId)}/teams/{Uri.EscapeDataString(teamId)}" +
              $"/threads?per_page={limit}&page={page}&deleted=false&unread=false";

        var wire = await GetAsync<ThreadsWire>(uri, ct).ConfigureAwait(false);

        return new UserThreadsResponse
        {
            Total = wire.Total,
            TotalUnreadThreads = wire.TotalUnreadThreads,
            Threads =
            [
                .. wire.Threads.Select(static thread => new UserThread
                {
                    Id = thread.Id,
                    Post = thread.Post,
                    ChannelId = thread.Post?.ChannelId,
                    UnreadReplies = thread.UnreadReplies,
                    LastReplyAt = DateTimeOffset.FromUnixTimeMilliseconds(thread.LastReplyAt)
                })
            ]
        };
    }

    /// <summary>
    /// Все посты треда (корень + ответы).
    /// </summary>
    public Task<MattermostPostThread?> GetThreadPostsAsync(string threadId, CancellationToken ct)
        => GetOrDefaultAsync<MattermostPostThread>(
            $"api/v4/posts/{Uri.EscapeDataString(threadId)}/thread", ct);

    private async Task<T> GetAsync<T>(string requestUri, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(requestUri, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<T>(SerializerOptions, ct)
            .ConfigureAwait(false);

        return result ?? throw new InvalidOperationException(
            $"MmDesktop returned empty body for {requestUri}.");
    }

    private async Task<T?> GetOrDefaultAsync<T>(string requestUri, CancellationToken ct)
        where T : class
    {
        using var response = await _httpClient.GetAsync(requestUri, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<T>(SerializerOptions, ct)
            .ConfigureAwait(false);
    }

    private sealed record ThreadsWire
    {
        [JsonPropertyName("total")]
        public int Total { get; init; }

        [JsonPropertyName("total_unread_threads")]
        public int TotalUnreadThreads { get; init; }

        [JsonPropertyName("threads")]
        public List<ThreadWire> Threads { get; init; } = [];
    }

    private sealed record ThreadWire
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("post")]
        public MattermostPost? Post { get; init; }

        [JsonPropertyName("unread_replies")]
        public int UnreadReplies { get; init; }

        [JsonPropertyName("last_reply_at")]
        public long LastReplyAt { get; init; }
    }
}
