using System.Net;
using System.Text.Json;
using FluentAssertions;
using MattermostMCP.Handlers;
using MattermostMCP.MmDesktop;
using Microsoft.Extensions.Options;
using Xunit;

namespace MattermostMCP.UnitTests;

public sealed class ToolsHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;


    private const string Search = "mattermost_search_threads";
    private const string Followed = "mattermost_list_followed_threads";
    private const string GetThread = "mattermost_get_thread";
    private const string UserId = "uid-1";

    private static ToolsHandler CreateHandler(
        RoutingHttpMessageHandler handler,
        params (string Key, string? Value)[] config)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://test.local/") };
        var service = new ThreadSearchService(
            new MattermostClient(http),
            new MattermostTokenProvider(
                TestSubstitutes.HttpClientFactory(handler),
                Options.Create(new MmDesktopOptions { Token = "static-token" }),
                TestLoggers.For<MattermostTokenProvider>()),
            TestConfig.Create(config),
            TestLoggers.For<ThreadSearchService>());

        return new ToolsHandler(service, TestLoggers.For<ToolsHandler>());
    }

    private static (ToolsHandler Handler, RoutingHttpMessageHandler Http) CreateWithRouter(
        MmApiRouter router,
        params (string Key, string? Value)[] config)
    {
        var handler = new RoutingHttpMessageHandler(router.Respond);
        return (CreateHandler(handler, config), handler);
    }

    private static ToolsHandler HandlerOk(params (string Key, string? Value)[] config)
        => CreateHandler(
            new RoutingHttpMessageHandler(TestHttp.Status(HttpStatusCode.OK)), config);

    private static bool IsToolError(JsonDocument doc)
        => doc.RootElement.GetProperty("result").GetProperty("isError").GetBoolean();

    private static JsonElement Call(string tool, string argumentsJson)
        => TestJson.Element(
            "{\"name\":\"" + tool + "\",\"arguments\":" + argumentsJson + "}");

    private static MmApiRouter SearchRouter(string message = "needle")
        => new MmApiRouter()
            .AddJson("users/uid-1/threads", MmFixtures.Threads("thread-1", message))
            .AddPosts("thread-1", ("post-root", message))
            .AddChannel("channel-1");

    private static string ResultText(JsonDocument doc)
        => doc.RootElement.GetProperty("result").GetProperty("content")[0]
            .GetProperty("text").GetString()!;

    // ---------- GetTools ----------

    [Fact]
    public void GetTools_ExposesThreeReadOnlyTools()
    {
        var handler = HandlerOk();

        var tools = handler.GetTools();

        tools.Select(t => t.Name).Should().Equal(Search, Followed, GetThread);
        tools.Should().OnlyContain(t => t.Annotations!.ReadOnlyHint);
        tools.Should().OnlyContain(t => !t.Annotations!.DestructiveHint);
        tools.Should().OnlyContain(t => t.OutputSchema != null);
    }

    // ---------- HandleCallAsync validation ----------

    [Fact]
    public async Task HandleCallAsync_NonObjectParameters_ReturnsInvalidParams()
    {
        var handler = HandlerOk();

        using var result = await handler.HandleCallAsync(
            TestJson.Element("[1,2]"), 1, "u", UserId, Ct);

        result.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32602);
    }

    [Fact]
    public async Task HandleCallAsync_MissingName_ReturnsInvalidParams()
    {
        var handler = HandlerOk();

        using var result = await handler.HandleCallAsync(
            TestJson.Empty, 1, "u", UserId, Ct);

        result.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32602);
    }

    [Fact]
    public async Task HandleCallAsync_NonStringName_ReturnsInvalidParams()
    {
        var handler = HandlerOk();

        using var result = await handler.HandleCallAsync(
            TestJson.Element("{\"name\":123}"), 1, "u", UserId, Ct);

        result.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32602);
    }

    [Fact]
    public async Task HandleCallAsync_UnknownTool_ReturnsToolError()
    {
        var handler = HandlerOk();

        using var result = await handler.HandleCallAsync(
            Call("unknown_tool", "{}"), 1, "u", UserId, Ct);

        IsToolError(result).Should().BeTrue();
        ResultText(result).Should().Contain("Неизвестный инструмент");
    }

    [Fact]
    public async Task HandleCallAsync_NonObjectArguments_TreatedAsEmpty()
    {
        var handler = HandlerOk();
        var parameters = TestJson.Element(
            "{\"name\":\"" + Search + "\",\"arguments\":\"not-an-object\"}");

        using var result = await handler.HandleCallAsync(
            parameters, 1, "u", UserId, Ct);

        ResultText(result).Should().Contain("'query' обязателен");
    }

    // ---------- search tool ----------

    [Fact]
    public async Task Search_MissingUserId_ReturnsToolError()
    {
        var handler = HandlerOk();

        using var result = await handler.HandleCallAsync(
            Call(Search, "{\"query\":\"x\"}"), 1, "u", null, Ct);

        ResultText(result).Should().Contain("Mattermost-пользователь не найден");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"query\":\"\"}")]
    [InlineData("{\"query\":123}")]
    public async Task Search_InvalidQuery_ReturnsToolError(string arguments)
    {
        var handler = HandlerOk();

        using var result = await handler.HandleCallAsync(
            Call(Search, arguments), 1, "u", UserId, Ct);

        ResultText(result).Should().Contain("'query' обязателен");
    }

    [Theory]
    [InlineData("{\"query\":\"x\",\"limit\":\"nope\"}")]
    [InlineData("{\"query\":\"x\",\"limit\":0}")]
    [InlineData("{\"query\":\"x\",\"limit\":1001}")]
    public async Task Search_InvalidLimit_ReturnsToolError(string arguments)
    {
        var handler = HandlerOk();

        using var result = await handler.HandleCallAsync(
            Call(Search, arguments), 1, "u", UserId, Ct);

        ResultText(result).Should().Contain("'limit'");
    }

    [Fact]
    public async Task Search_InvalidSearchTitleOnly_ReturnsToolError()
    {
        var handler = HandlerOk();

        using var result = await handler.HandleCallAsync(
            Call(Search, "{\"query\":\"x\",\"searchTitleOnly\":\"yes\"}"),
            1, "u", UserId, Ct);

        ResultText(result).Should().Contain("'searchTitleOnly'");
    }

    [Fact]
    public async Task Search_InvalidResponseFormat_ReturnsToolError()
    {
        var handler = HandlerOk();

        using var result = await handler.HandleCallAsync(
            Call(Search, "{\"query\":\"x\",\"response_format\":\"xml\"}"),
            1, "u", UserId, Ct);

        ResultText(result).Should().Contain("response_format");
    }

    [Fact]
    public async Task Search_NonStringResponseFormat_ReturnsToolError()
    {
        var handler = HandlerOk();

        using var result = await handler.HandleCallAsync(
            Call(Search, "{\"query\":\"x\",\"response_format\":5}"),
            1, "u", UserId, Ct);

        ResultText(result).Should().Contain("response_format");
    }

    [Fact]
    public async Task Search_Markdown_Success()
    {
        var (handler, _) = CreateWithRouter(
            SearchRouter(), ("MmDesktop:UiBaseUrl", "https://mm.example.com/test-team"));

        using var result = await handler.HandleCallAsync(
            Call(Search, "{\"query\":\"needle\"}"), 1, "u", UserId, Ct);

        var text = ResultText(result);
        text.Should().Contain("Найдено: 1");
        text.Should().Contain("Test Channel");
        text.Should().Contain("pl/post-root");
        result.RootElement.GetProperty("result").TryGetProperty("structuredContent", out _)
            .Should().BeTrue();
    }

    [Fact]
    public async Task Search_Json_Success()
    {
        var (handler, _) = CreateWithRouter(SearchRouter());

        using var result = await handler.HandleCallAsync(
            Call(Search, "{\"query\":\"needle\",\"response_format\":\"json\"}"),
            1, "u", UserId, Ct);

        using var structured = JsonDocument.Parse(ResultText(result));
        structured.RootElement.GetProperty("total").GetInt32().Should().Be(1);
        structured.RootElement.GetProperty("threads")[0].GetProperty("threadId").GetString()
            .Should().Be("thread-1");
    }

    [Fact]
    public async Task Search_TruncatesToLimit()
    {
        var router = new MmApiRouter()
            .AddJson("users/uid-1/threads", MmFixtures.Threads("thread-1", "needle"))
            .AddPosts("thread-1", ("post-root", "needle"))
            .AddChannel("channel-1");
        var (handler, _) = CreateWithRouter(router);

        using var result = await handler.HandleCallAsync(
            Call(Search, "{\"query\":\"needle\",\"limit\":1}"),
            1, "u", UserId, Ct);

        ResultText(result).Should().Contain("Найдено: 1");
    }

    [Fact]
    public async Task Search_UpstreamFailure_ReturnsToolError()
    {
        var router = new MmApiRouter()
            .AddStatus("users/uid-1/threads", HttpStatusCode.InternalServerError);
        var (handler, _) = CreateWithRouter(router);

        using var result = await handler.HandleCallAsync(
            Call(Search, "{\"query\":\"needle\"}"), 1, "u", UserId, Ct);

        IsToolError(result).Should().BeTrue();
        ResultText(result).Should().Contain("Не удалось выполнить инструмент");
    }

    [Fact]
    public async Task Search_Cancellation_Propagates()
    {
        var (handler, _) = CreateWithRouter(SearchRouter());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => handler.HandleCallAsync(
            Call(Search, "{\"query\":\"needle\"}"), 1, "u", UserId, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---------- followed tool ----------

    [Fact]
    public async Task Followed_MissingUserId_ReturnsToolError()
    {
        var handler = HandlerOk();

        using var result = await handler.HandleCallAsync(
            Call(Followed, "{}"), 1, "u", null, Ct);

        ResultText(result).Should().Contain("Mattermost-пользователь не найден");
    }

    [Theory]
    [InlineData("{\"limit\":0}")]
    [InlineData("{\"limit\":\"x\"}")]
    public async Task Followed_InvalidLimit_ReturnsToolError(string arguments)
    {
        var handler = HandlerOk();

        using var result = await handler.HandleCallAsync(
            Call(Followed, arguments), 1, "u", UserId, Ct);

        ResultText(result).Should().Contain("'limit'");
    }

    [Theory]
    [InlineData("{\"page\":0}")]
    [InlineData("{\"page\":10001}")]
    [InlineData("{\"page\":\"x\"}")]
    public async Task Followed_InvalidPage_ReturnsToolError(string arguments)
    {
        var handler = HandlerOk();

        using var result = await handler.HandleCallAsync(
            Call(Followed, arguments), 1, "u", UserId, Ct);

        ResultText(result).Should().Contain("'page'");
    }

    [Fact]
    public async Task Followed_InvalidFormat_ReturnsToolError()
    {
        var handler = HandlerOk();

        using var result = await handler.HandleCallAsync(
            Call(Followed, "{\"response_format\":\"xml\"}"),
            1, "u", UserId, Ct);

        ResultText(result).Should().Contain("response_format");
    }

    [Fact]
    public async Task Followed_Markdown_Success_WithHasMore()
    {
        var router = new MmApiRouter()
            .AddJson("users/uid-1/threads", MmFixtures.Threads("thread-1", "summary text"));
        var (handler, _) = CreateWithRouter(
            router, ("MmDesktop:UiBaseUrl", "https://mm.example.com/t"));

        using var result = await handler.HandleCallAsync(
            Call(Followed, "{\"page\":1,\"limit\":1}"), 1, "u", UserId, Ct);

        var text = ResultText(result);
        text.Should().Contain("Followed threads: 1");
        text.Should().Contain("thread-1");
        text.Should().Contain("pl/thread-1");
    }

    [Fact]
    public async Task Followed_Json_Success()
    {
        var router = new MmApiRouter()
            .AddJson("users/uid-1/threads", MmFixtures.Threads("thread-1", "hi"));
        var (handler, _) = CreateWithRouter(router);

        using var result = await handler.HandleCallAsync(
            Call(Followed, "{\"response_format\":\"json\"}"),
            1, "u", UserId, Ct);

        using var structured = JsonDocument.Parse(ResultText(result));
        structured.RootElement.GetProperty("total").GetInt32().Should().Be(1);
        structured.RootElement.GetProperty("threads")[0].GetProperty("threadId").GetString()
            .Should().Be("thread-1");
    }

    [Fact]
    public async Task Followed_LongSummary_IsTruncated()
    {
        var longMessage = new string('x', 300);
        var router = new MmApiRouter()
            .AddJson("users/uid-1/threads", MmFixtures.Threads("thread-1", longMessage));
        var (handler, _) = CreateWithRouter(router);

        using var result = await handler.HandleCallAsync(
            Call(Followed, "{\"response_format\":\"json\"}"),
            1, "u", UserId, Ct);

        using var structured = JsonDocument.Parse(ResultText(result));
        structured.RootElement.GetProperty("threads")[0].GetProperty("summary").GetString()
            .Should().EndWith("...");
    }

    // ---------- get thread tool ----------

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"threadId\":\"\"}")]
    [InlineData("{\"threadId\":5}")]
    public async Task GetThread_InvalidId_ReturnsToolError(string arguments)
    {
        var handler = HandlerOk();

        using var result = await handler.HandleCallAsync(
            Call(GetThread, arguments), 1, "u", UserId, Ct);

        ResultText(result).Should().Contain("'threadId'");
    }

    [Fact]
    public async Task GetThread_InvalidFormat_ReturnsToolError()
    {
        var handler = HandlerOk();

        using var result = await handler.HandleCallAsync(
            Call(GetThread, "{\"threadId\":\"t\",\"response_format\":\"xml\"}"),
            1, "u", UserId, Ct);

        ResultText(result).Should().Contain("response_format");
    }

    [Fact]
    public async Task GetThread_NotFound_ReturnsToolError()
    {
        var router = new MmApiRouter().AddStatus("posts/", HttpStatusCode.NotFound);
        var (handler, _) = CreateWithRouter(router);

        using var result = await handler.HandleCallAsync(
            Call(GetThread, "{\"threadId\":\"missing\"}"),
            1, "u", UserId, Ct);

        ResultText(result).Should().Contain("не найден");
    }

    [Fact]
    public async Task GetThread_Markdown_Success()
    {
        var router = new MmApiRouter()
            .AddJson("posts/thread-1/thread", MmFixtures.PostThread(
                "thread-1", ("post-root", "hello"), ("post-reply", "world")))
            .AddChannel("channel-1");
        var (handler, _) = CreateWithRouter(
            router, ("MmDesktop:UiBaseUrl", "https://mm.example.com/t"));

        using var result = await handler.HandleCallAsync(
            Call(GetThread, "{\"threadId\":\"thread-1\"}"),
            1, "u", UserId, Ct);

        var text = ResultText(result);
        text.Should().Contain("Тред thread-1: сообщений 2");
        text.Should().Contain("post-root");
    }

    [Fact]
    public async Task GetThread_Json_Success()
    {
        var router = new MmApiRouter()
            .AddPosts("thread-1", ("post-root", "hello"));
        var (handler, _) = CreateWithRouter(router);

        using var result = await handler.HandleCallAsync(
            Call(GetThread, "{\"threadId\":\"thread-1\",\"response_format\":\"json\"}"),
            1, "u", UserId, Ct);

        using var structured = JsonDocument.Parse(ResultText(result));
        structured.RootElement.GetProperty("threadId").GetString().Should().Be("thread-1");
        structured.RootElement.GetProperty("messageCount").GetInt32().Should().Be(1);
    }
}
