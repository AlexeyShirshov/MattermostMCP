using MattermostMCP.Auth;
using MattermostMCP.MmDesktop;
using MattermostMCP.MmDesktop.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MattermostMCP.Handlers;

/// <summary>
/// REST-зеркало MCP-инструмента поиска по followed threads (Minimal API).
/// </summary>
public static partial class ThreadEndpoints
{
    public static IEndpointRouteBuilder MapThreadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var threads = endpoints
            .MapGroup("/api/threads")
            .RequireAuthorization(Startup.AUTHENTICATED_USER_POLICY);

        threads.MapGet("/search", SearchAsync);
        threads.MapGet("/followed", GetFollowedThreadsAsync);

        return endpoints;
    }

    /// <summary>
    /// GET /api/threads/search?query=something&amp;limit=50
    /// </summary>
    private static async Task<IResult> SearchAsync(
        string query,
        int? limit,
        HttpContext httpContext,
        ThreadSearchService threadSearchService,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(nameof(ThreadEndpoints));
        var username = AuthService.GetMattermostLogin(httpContext.User);
        if (string.IsNullOrEmpty(username))
        {
            return Results.Json(
                new { error = "Cannot extract username from token" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var userId = await threadSearchService.ResolveUserIdAsync(
            username, AuthService.GetEmail(httpContext.User), ct).ConfigureAwait(false);
        if (userId is null)
        {
            return Results.NotFound(new { error = $"MmDesktop user '{username}' not found" });
        }

        var request = new ThreadSearchRequest
        {
            Query = query,
            Limit = limit ?? 100
        };

        IReadOnlyList<ThreadSearchResult> results;
        try
        {
            results = await threadSearchService.SearchFollowedThreadsAsync(
                userId, request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogUpstreamFailed(logger, ex, username);
            return Results.Json(
                new { error = "MmDesktop request failed. Please retry later." },
                statusCode: StatusCodes.Status502BadGateway);
        }

        LogRestSearch(logger, username, query, results.Count);

        return Results.Ok(new
        {
            username,
            query,
            total = results.Count,
            threads = results
        });
    }

    /// <summary>
    /// GET /api/threads/followed?limit=50
    /// </summary>
    private static async Task<IResult> GetFollowedThreadsAsync(
        int? limit,
        int? page,
        HttpContext httpContext,
        ThreadSearchService threadSearchService,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(nameof(ThreadEndpoints));
        var username = AuthService.GetMattermostLogin(httpContext.User);
        if (string.IsNullOrEmpty(username))
        {
            return Results.Unauthorized();
        }

        var userId = await threadSearchService.ResolveUserIdAsync(
            username, AuthService.GetEmail(httpContext.User), ct).ConfigureAwait(false);
        if (userId is null)
        {
            return Results.NotFound(new { error = $"MmDesktop user '{username}' not found" });
        }

        var pageNumber = page ?? 1;
        var pageSize = limit ?? 50;

        UserThreadsResponse threads;
        try
        {
            threads = await threadSearchService.GetFollowedThreadsAsync(
                userId, pageSize, pageNumber, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogUpstreamFailed(logger, ex, username);
            return Results.Json(
                new { error = "MmDesktop request failed. Please retry later." },
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(new
        {
            username,
            total = threads.Total,
            totalUnread = threads.TotalUnread,
            page = pageNumber,
            limit = pageSize,
            hasMore = threads.Threads.Count < threads.Total,
            threads = threads.Threads.Select(static t => new
            {
                t.Id,
                t.ChannelId,
                summary = t.Post?.Message?.Length > 200
                    ? t.Post.Message[..200] + "..."
                    : t.Post?.Message ?? "",
                t.UnreadReplies,
                t.LastReplyAt
            })
        });
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "REST search by {User}: {Query} -> {Count} results")]
    private static partial void LogRestSearch(ILogger logger, string user, string query, int count);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "MmDesktop upstream request failed for {User}")]
    private static partial void LogUpstreamFailed(ILogger logger, Exception exception, string user);
}
