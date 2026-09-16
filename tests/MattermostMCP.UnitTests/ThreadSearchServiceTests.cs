using System.Net;
using FluentAssertions;
using MattermostMCP.MmDesktop;
using Microsoft.Extensions.Options;
using Xunit;

namespace MattermostMCP.UnitTests;

public sealed class ThreadSearchServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;


    private const string UserId = "uid-1";

    private static ThreadSearchService CreateService(
        RoutingHttpMessageHandler handler,
        params (string Key, string? Value)[] config)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://test.local/") };
        var client = new MattermostClient(http);
        var tokenProvider = new MattermostTokenProvider(
            TestSubstitutes.HttpClientFactory(handler),
            Options.Create(new MmDesktopOptions { Token = "static-token" }),
            TestLoggers.For<MattermostTokenProvider>());

        return new ThreadSearchService(
            client,
            tokenProvider,
            TestConfig.Create(config),
            TestLoggers.For<ThreadSearchService>());
    }

    private static (ThreadSearchService Service, RoutingHttpMessageHandler Handler)
        CreateWithRouter(
        MmApiRouter router,
        params (string Key, string? Value)[] config)
    {
        var handler = new RoutingHttpMessageHandler(router.Respond);
        return (CreateService(handler, config), handler);
    }

    private static ThreadSearchService ServiceOk(params (string Key, string? Value)[] config)
        => CreateService(
            new RoutingHttpMessageHandler(TestHttp.Status(HttpStatusCode.OK)), config);

    // ---------- BuildThreadUrl ----------

    [Fact]
    public void BuildThreadUrl_WithUiBaseUrl_TrimsTrailingSlash()
    {
        var service = CreateService(
            new RoutingHttpMessageHandler(TestHttp.Status(HttpStatusCode.OK)),
            ("MmDesktop:UiBaseUrl", "https://mm.example.com/test-team/"));

        service.BuildThreadUrl("post-1").Should().Be("https://mm.example.com/test-team/pl/post-1");
    }

    [Fact]
    public void BuildThreadUrl_WithoutUiBaseUrl_ReturnsEmpty()
    {
        var service = ServiceOk();

        service.BuildThreadUrl("post-1").Should().BeEmpty();
    }

    // ---------- ResolveUserIdAsync ----------

    [Fact]
    public async Task ResolveUserIdAsync_PrefersLoginLookup()
    {
        var router = new MmApiRouter()
            .AddJson("users/username/testuser", MmFixtures.User("uid-1"))
            .AddJson("users/email/", MmFixtures.User("uid-email"));
        var (service, handler) = CreateWithRouter(router);

        var id = await service.ResolveUserIdAsync(
            "testuser", "testuser@example.com", Ct);

        id.Should().Be("uid-1");
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task ResolveUserIdAsync_FallsBackToEmail()
    {
        var router = new MmApiRouter()
            .AddStatus("users/username/", HttpStatusCode.NotFound)
            .AddJson("users/email/", MmFixtures.User("uid-email"));
        var (service, _) = CreateWithRouter(router);

        var id = await service.ResolveUserIdAsync(
            "ghost", "testuser@example.com", Ct);

        id.Should().Be("uid-email");
    }

    [Fact]
    public async Task ResolveUserIdAsync_NotFound_ReturnsNull()
    {
        var router = new MmApiRouter()
            .AddStatus("users/username/", HttpStatusCode.NotFound)
            .AddStatus("users/email/", HttpStatusCode.NotFound);
        var (service, _) = CreateWithRouter(router);

        var id = await service.ResolveUserIdAsync(
            "ghost", "ghost@example.com", Ct);

        id.Should().BeNull();
    }

    [Fact]
    public async Task ResolveUserIdAsync_UpstreamError_ReturnsNull()
    {
        var router = new MmApiRouter()
            .AddStatus("users/username/", HttpStatusCode.InternalServerError);
        var (service, _) = CreateWithRouter(router);

        var id = await service.ResolveUserIdAsync(
            "testuser", null, Ct);

        id.Should().BeNull();
    }

    [Fact]
    public async Task ResolveUserIdAsync_NoLoginOrEmail_ReturnsNullWithoutHttp()
    {
        var (service, handler) = CreateWithRouter(new MmApiRouter());

        var id = await service.ResolveUserIdAsync(null, null, Ct);

        id.Should().BeNull();
        handler.Requests.Should().BeEmpty();
    }

    // ---------- GetFollowedThreadsAsync (v7 / v11) ----------

    [Fact]
    public async Task GetFollowedThreadsAsync_DefaultsToV7GlobalRoute()
    {
        var router = new MmApiRouter()
            .AddJson("users/uid-1/threads", MmFixtures.Threads("t1", "hi"));
        var (service, handler) = CreateWithRouter(router);

        var result = await service.GetFollowedThreadsAsync(
            UserId, 50, 1, Ct);

        result.Total.Should().Be(1);
        result.Threads.Should().ContainSingle();
        handler.Requests.Single().RequestUri!.AbsolutePath
            .Should().Be("/api/v4/users/uid-1/threads");
    }

    [Fact]
    public async Task GetFollowedThreadsAsync_V11_AggregatesTeams()
    {
        var router = new MmApiRouter()
            .AddJson("teams/team-1/threads", MmFixtures.Threads("t1", "one", unread: 2))
            .AddJson("teams/team-2/threads", MmFixtures.Threads("t2", "two", unread: 3))
            .AddJson(
                "users/uid-1/teams",
                $"[{MmFixtures.Team("team-1")},{MmFixtures.Team("team-2")}]");
        var (service, _) = CreateWithRouter(router, ("MmDesktop:ThreadsApiVersion", "v11"));

        var result = await service.GetFollowedThreadsAsync(
            UserId, 50, 1, Ct);

        result.Threads.Should().HaveCount(2);
        result.Total.Should().Be(2);
        result.TotalUnread.Should().Be(5);
    }

    [Fact]
    public async Task GetFollowedThreadsAsync_V11_IsCaseInsensitive()
    {
        var router = new MmApiRouter()
            .AddJson("teams/team-1/threads", MmFixtures.EmptyThreads)
            .AddJson("users/uid-1/teams", $"[{MmFixtures.Team("team-1")}]");
        var (service, _) = CreateWithRouter(router, ("MmDesktop:ThreadsApiVersion", "V11"));

        var result = await service.GetFollowedThreadsAsync(
            UserId, 50, 1, Ct);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetFollowedThreadsAsync_V11_TeamsFailure_Throws()
    {
        var router = new MmApiRouter()
            .AddStatus("users/uid-1/teams", HttpStatusCode.InternalServerError);
        var (service, _) = CreateWithRouter(router, ("MmDesktop:ThreadsApiVersion", "v11"));

        var act = () => service.GetFollowedThreadsAsync(
            UserId, 50, 1, Ct);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    // ---------- SearchFollowedThreadsAsync ----------

    private static MmApiRouter SearchRouter(
        string rootMessage,
        params (string Id, string Message)[] posts)
        => new MmApiRouter()
            .AddJson("users/uid-1/threads", MmFixtures.Threads("thread-1", rootMessage))
            .AddJson("posts/thread-1/thread", MmFixtures.PostThread("thread-1", posts))
            .AddChannel("channel-1");

    [Fact]
    public async Task SearchFollowedThreadsAsync_EmptyTerms_ReturnsEmptyWithoutHttp()
    {
        var (service, handler) = CreateWithRouter(new MmApiRouter());

        var results = await service.SearchFollowedThreadsAsync(
            UserId, new ThreadSearchRequest { Query = "!!!" }, Ct);

        results.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchFollowedThreadsAsync_MatchesPostAndBuildsResult()
    {
        var router = SearchRouter(
            "Hello World",
            ("post-root", "hello world"),
            ("post-reply", "second"));
        var (service, _) = CreateWithRouter(
            router, ("MmDesktop:UiBaseUrl", "https://mm.example.com/test-team"));

        var results = await service.SearchFollowedThreadsAsync(
            UserId, new ThreadSearchRequest { Query = "HELLO" }, Ct);

        results.Should().ContainSingle();
        var r = results[0];
        r.ThreadId.Should().Be("thread-1");
        r.ChannelName.Should().Be("Test Channel");
        r.ChannelId.Should().Be("channel-1");
        r.MatchedMessage.Should().Be("hello world");
        r.RootMessage.Should().Be("Hello World");
        r.MattermostUrl.Should().Be("https://mm.example.com/test-team/pl/post-root");
    }

    [Fact]
    public async Task SearchFollowedThreadsAsync_MatchesReplyMessage()
    {
        var router = SearchRouter(
            "root text",
            ("post-root", "root text"),
            ("post-reply", "contains the needle here"));
        var (service, _) = CreateWithRouter(router);

        var results = await service.SearchFollowedThreadsAsync(
            UserId, new ThreadSearchRequest { Query = "needle" }, Ct);

        results.Should().ContainSingle();
        results[0].MatchedMessage.Should().Be("contains the needle here");
    }

    [Fact]
    public async Task SearchFollowedThreadsAsync_RequiresAllTerms()
    {
        var router = SearchRouter("root", ("post-root", "alpha beta"));
        var (service, _) = CreateWithRouter(router);

        var results = await service.SearchFollowedThreadsAsync(
            UserId,
            new ThreadSearchRequest { Query = "alpha gamma" },
            Ct);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchFollowedThreadsAsync_NoMatch_ReturnsEmpty()
    {
        var router = SearchRouter("root", ("post-root", "nothing relevant"));
        var (service, _) = CreateWithRouter(router);

        var results = await service.SearchFollowedThreadsAsync(
            UserId, new ThreadSearchRequest { Query = "absent" }, Ct);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchFollowedThreadsAsync_TitleOnly_SkipsReplyFetch()
    {
        var router = SearchRouter(
            "needle root", ("post-root", "needle root"), ("post-reply", "needle reply"));
        var (service, handler) = CreateWithRouter(router);

        var results = await service.SearchFollowedThreadsAsync(
            UserId,
            new ThreadSearchRequest { Query = "needle", SearchTitleOnly = true },
            Ct);

        results.Should().ContainSingle();
        results[0].MatchedMessage.Should().Be("needle root");
        handler.Requests.Should().NotContain(r =>
            r.RequestUri!.AbsolutePath.StartsWith("/api/v4/posts/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchFollowedThreadsAsync_TitleOnly_ReplyOnlyMatch_ReturnsEmpty()
    {
        var router = SearchRouter(
            "root text", ("post-root", "root text"), ("post-reply", "needle"));
        var (service, _) = CreateWithRouter(router);

        var results = await service.SearchFollowedThreadsAsync(
            UserId,
            new ThreadSearchRequest { Query = "needle", SearchTitleOnly = true },
            Ct);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchFollowedThreadsAsync_ThreadPostsMissing_FallsBackToRootPost()
    {
        var router = new MmApiRouter()
            .AddJson("users/uid-1/threads", MmFixtures.Threads("thread-1", "fallback needle"))
            .AddStatus("posts/thread-1/thread", HttpStatusCode.NotFound)
            .AddChannel("channel-1");
        var (service, _) = CreateWithRouter(router);

        var results = await service.SearchFollowedThreadsAsync(
            UserId, new ThreadSearchRequest { Query = "needle" }, Ct);

        results.Should().ContainSingle();
        results[0].MatchedMessage.Should().Be("fallback needle");
    }

    [Fact]
    public async Task SearchFollowedThreadsAsync_ChannelNameFallsBackToName()
    {
        var router = new MmApiRouter()
            .AddJson("users/uid-1/threads", MmFixtures.Threads("thread-1", "needle"))
            .AddPosts("thread-1", ("post-root", "needle"))
            .AddChannel("channel-1", displayName: "", name: "chan-name");
        var (service, _) = CreateWithRouter(router);

        var results = await service.SearchFollowedThreadsAsync(
            UserId, new ThreadSearchRequest { Query = "needle" }, Ct);

        results[0].ChannelName.Should().Be("chan-name");
    }

    [Fact]
    public async Task SearchFollowedThreadsAsync_ChannelLookupFails_ReturnsChannelId()
    {
        var router = new MmApiRouter()
            .AddJson("users/uid-1/threads", MmFixtures.Threads("thread-1", "needle"))
            .AddPosts("thread-1", ("post-root", "needle"))
            .AddStatus("channels/channel-1", HttpStatusCode.InternalServerError);
        var (service, _) = CreateWithRouter(router);

        var results = await service.SearchFollowedThreadsAsync(
            UserId, new ThreadSearchRequest { Query = "needle" }, Ct);

        results[0].ChannelName.Should().Be("channel-1");
    }

    [Fact]
    public async Task SearchFollowedThreadsAsync_NullChannel_UsesEmptyChannelName()
    {
        const string threadsJson = """
        {
          "total": 1,
          "total_unread_threads": 0,
          "threads": [
            {
              "id": "thread-1",
              "unread_replies": 0,
              "last_reply_at": 1700000000000,
              "post": {
                "id": "post-root",
                "channel_id": null,
                "user_id": "user-1",
                "message": "needle",
                "create_at": 1700000000000,
                "root_id": ""
              }
            }
          ]
        }
        """;
        var router = new MmApiRouter()
            .AddJson("users/uid-1/threads", threadsJson)
            .AddPosts("thread-1", ("post-root", "needle"));
        var (service, _) = CreateWithRouter(router);

        var results = await service.SearchFollowedThreadsAsync(
            UserId, new ThreadSearchRequest { Query = "needle" }, Ct);

        results.Should().ContainSingle();
        results[0].ChannelName.Should().BeEmpty();
        results[0].ChannelId.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchFollowedThreadsAsync_TruncatesLongRootAndMatchedMessages()
    {
        var longRoot = "needle " + new string('x', 600);
        var longReply = "needle " + new string('y', 600);
        var router = SearchRouter("short", ("post-root", longRoot), ("post-reply", longReply));
        // root message from fixtures is "short"; make root long via custom thread json
        router = new MmApiRouter()
            .AddJson("users/uid-1/threads", MmFixtures.Threads("thread-1", longRoot))
            .AddPosts("thread-1", ("post-root", longRoot))
            .AddChannel("channel-1");
        var (service, _) = CreateWithRouter(router);

        var results = await service.SearchFollowedThreadsAsync(
            UserId, new ThreadSearchRequest { Query = "needle" }, Ct);

        results.Should().ContainSingle();
        results[0].RootMessage.Should().EndWith("...");
        results[0].RootMessage.Length.Should().Be(503);
        results[0].MatchedMessage.Should().StartWith("needle ");
        results[0].MatchedMessage.Should().EndWith("...");
        results[0].MatchedMessage.Length.Should().Be(43);
    }

    // ---------- GetThreadPostsAsync ----------

    [Fact]
    public async Task GetThreadPostsAsync_PreservesOrder()
    {
        var router = new MmApiRouter()
            .AddJson("posts/thread-1/thread", MmFixtures.PostThread(
                "thread-1", ("p2", "b"), ("p1", "a")));
        var (service, _) = CreateWithRouter(router);

        var posts = await service.GetThreadPostsAsync("thread-1", Ct);

        posts.Should().NotBeNull();
        posts!.Select(p => p.Id).Should().Equal("p2", "p1");
    }

    [Fact]
    public async Task GetThreadPostsAsync_EmptyOrder_FallsBackToValues()
    {
        const string json = """
        {
          "order": [],
          "posts": {
            "p1": {
              "id": "p1", "channel_id": "c", "user_id": "u",
              "message": "a", "create_at": 1, "root_id": ""
            }
          }
        }
        """;
        var router = new MmApiRouter().AddJson("posts/thread-1/thread", json);
        var (service, _) = CreateWithRouter(router);

        var posts = await service.GetThreadPostsAsync("thread-1", Ct);

        posts.Should().ContainSingle();
        posts![0].Id.Should().Be("p1");
    }

    [Fact]
    public async Task GetThreadPostsAsync_MissingThread_ReturnsNull()
    {
        var router = new MmApiRouter().AddStatus("posts/", HttpStatusCode.NotFound);
        var (service, _) = CreateWithRouter(router);

        var posts = await service.GetThreadPostsAsync("missing", Ct);

        posts.Should().BeNull();
    }

    [Fact]
    public async Task GetThreadPostsAsync_UpstreamError_ReturnsNull()
    {
        var router = new MmApiRouter().AddStatus("posts/", HttpStatusCode.InternalServerError);
        var (service, _) = CreateWithRouter(router);

        var posts = await service.GetThreadPostsAsync("err", Ct);

        posts.Should().BeNull();
    }
}
