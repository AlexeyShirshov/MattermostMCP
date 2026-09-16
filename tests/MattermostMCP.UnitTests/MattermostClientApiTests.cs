using System.Net;
using FluentAssertions;
using MattermostMCP.MmDesktop;
using MattermostMCP.MmDesktop.Models;
using Xunit;

namespace MattermostMCP.UnitTests;

public sealed class MattermostClientApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;


    private static (MattermostClient Client, RoutingHttpMessageHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new RoutingHttpMessageHandler(responder);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://test.local/") };
        return (new MattermostClient(http), handler);
    }

    [Fact]
    public async Task GetMeAsync_RequestsMeEndpoint()
    {
        var (client, handler) = Create(_ => TestHttp.Json(MmFixtures.User("user-1")));

        var me = await client.GetMeAsync(Ct);

        me.Id.Should().Be("user-1");
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/v4/users/me");
    }

    [Fact]
    public async Task FindUserByNameAsync_EscapesUsername()
    {
        var (client, handler) = Create(_ => TestHttp.Json(MmFixtures.User("user-1", "a b")));

        var user = await client.FindUserByNameAsync("a b", Ct);

        user!.Id.Should().Be("user-1");
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/v4/users/username/a%20b");
    }

    [Fact]
    public async Task FindUserByNameAsync_NotFound_ReturnsNull()
    {
        var (client, _) = Create(_ => TestHttp.Status(HttpStatusCode.NotFound));

        var user = await client.FindUserByNameAsync("missing", Ct);

        user.Should().BeNull();
    }

    [Fact]
    public async Task FindUserByEmailAsync_ReturnsUser()
    {
        var (client, handler) = Create(_ => TestHttp.Json(MmFixtures.User("user-2")));

        var user = await client.FindUserByEmailAsync(
            "a@b.com", Ct);

        user!.Id.Should().Be("user-2");
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/v4/users/email/a%40b.com");
    }

    [Fact]
    public async Task FindChannelByIdAsync_ReturnsChannel()
    {
        var (client, handler) = Create(_ => TestHttp.Json(MmFixtures.Channel("chan-1")));

        var channel = await client.FindChannelByIdAsync(
            "chan-1", Ct);

        channel!.Id.Should().Be("chan-1");
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/v4/channels/chan-1");
    }

    [Fact]
    public async Task GetUserTeamsAsync_ReturnsArray()
    {
        var json = $"[{MmFixtures.Team("team-1")},{MmFixtures.Team("team-2")}]";
        var (client, handler) = Create(_ => TestHttp.Json(json));

        var teams = await client.GetUserTeamsAsync("user-1", Ct);

        teams.Should().HaveCount(2);
        teams[1].Id.Should().Be("team-2");
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/v4/users/user-1/teams");
    }

    [Fact]
    public async Task GetUserThreadsAsync_V7_UsesGlobalRouteAndMapsFields()
    {
        var (client, handler) = Create(
            _ => TestHttp.Json(MmFixtures.Threads("thread-1", "hi", 1700000000000, 2)));

        var result = await client.GetUserThreadsAsync(
            "user-1", 50, teamId: null, 1, Ct);

        result.Total.Should().Be(1);
        result.TotalUnreadThreads.Should().Be(2);
        result.Threads.Should().ContainSingle();
        result.Threads[0].Id.Should().Be("thread-1");
        result.Threads[0].ChannelId.Should().Be("channel-1");
        result.Threads[0].UnreadReplies.Should().Be(2);
        result.Threads[0].LastReplyAt.ToUnixTimeMilliseconds().Should().Be(1700000000000);
        result.Threads[0].Post!.Message.Should().Be("hi");

        var uri = handler.Requests[0].RequestUri!;
        uri.AbsolutePath.Should().Be("/api/v4/users/user-1/threads");
        uri.Query.Should().Contain("per_page=50").And.Contain("page=1");
    }

    [Fact]
    public async Task GetUserThreadsAsync_V11_UsesPerTeamRoute()
    {
        var (client, handler) = Create(_ => TestHttp.Json(MmFixtures.EmptyThreads));

        await client.GetUserThreadsAsync(
            "user-1", 25, "team-9", 3, Ct);

        var uri = handler.Requests[0].RequestUri!;
        uri.AbsolutePath.Should().Be("/api/v4/users/user-1/teams/team-9/threads");
        uri.Query.Should().Contain("per_page=25").And.Contain("page=3");
    }

    [Fact]
    public async Task GetThreadPostsAsync_ReturnsThread()
    {
        var (client, handler) = Create(_ => TestHttp.Json(
            MmFixtures.PostThread("thread-1", ("post-1", "root"), ("post-2", "reply"))));

        var thread = await client.GetThreadPostsAsync(
            "thread-1", Ct);

        thread.Should().NotBeNull();
        thread!.Order.Should().Equal("post-1", "post-2");
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/v4/posts/thread-1/thread");
    }

    [Fact]
    public async Task GetThreadPostsAsync_NotFound_ReturnsNull()
    {
        var (client, _) = Create(_ => TestHttp.Status(HttpStatusCode.NotFound));

        var thread = await client.GetThreadPostsAsync(
            "missing", Ct);

        thread.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_NonSuccessThrows()
    {
        var (client, _) = Create(_ => TestHttp.Status(HttpStatusCode.InternalServerError));

        var act = () => client.GetMeAsync(Ct);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetAsync_NullBodyThrowsInvalidOperation()
    {
        var (client, _) = Create(_ => TestHttp.Json("null"));

        var act = () => client.GetMeAsync(Ct);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetOrDefaultAsync_NonNotFoundErrorThrows()
    {
        var (client, _) = Create(_ => TestHttp.Status(HttpStatusCode.BadRequest));

        var act = () => client.FindUserByNameAsync("x", Ct);

        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
