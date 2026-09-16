using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using MattermostMCP;
using MattermostMCP.Handlers;
using MattermostMCP.MmDesktop;
using Microsoft.Extensions.Options;
using Xunit;

namespace MattermostMCP.UnitTests;

public sealed class McpServerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;


    private static McpServer CreateServer(
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
        var tools = new ToolsHandler(service, TestLoggers.For<ToolsHandler>());

        return new McpServer(
            tools, service, TestConfig.Create(config), TestLoggers.For<McpServer>());
    }

    private static McpServer ServerOk()
        => CreateServer(
            new RoutingHttpMessageHandler(TestHttp.Status(HttpStatusCode.OK)));

    private static bool IsToolError(JsonDocument doc)
        => doc.RootElement.GetProperty("result").GetProperty("isError").GetBoolean();

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ClaimsPrincipal Authenticated(string email = "testuser@example.com")
        => new(new ClaimsIdentity(
            [new Claim("preferred_username", "testuser"), new Claim("email", email)],
            authenticationType: "test"));

    private static McpServer ServerWithSearch(params (string Key, string? Value)[] config)
    {
        var router = new MmApiRouter()
            .AddJson("users/username/", MmFixtures.User("uid-1"))
            .AddJson("users/uid-1/threads", MmFixtures.Threads("thread-1", "needle"))
            .AddPosts("thread-1", ("post-root", "needle"))
            .AddChannel("channel-1");
        return CreateServer(new RoutingHttpMessageHandler(router.Respond), config);
    }

    [Fact]
    public async Task Initialize_ReturnsServerInfo()
    {
        var server = ServerOk();

        using var result = await server.HandleRequestAsync(
            TestJson.Element("{\"method\":\"initialize\",\"id\":1}"),
            Anonymous(),
            Ct);

        var info = result.RootElement.GetProperty("result");
        info.GetProperty("protocolVersion").GetString().Should().Be("2024-11-05");
        info.GetProperty("serverInfo").GetProperty("name").GetString()
            .Should().Be("mattermost-mcp-server");
    }

    [Fact]
    public async Task ToolsList_ReturnsThreeTools()
    {
        var server = ServerOk();

        using var result = await server.HandleRequestAsync(
            TestJson.Element("{\"method\":\"tools/list\",\"id\":\"a\"}"),
            Anonymous(),
            Ct);

        result.RootElement.GetProperty("id").GetString().Should().Be("a");
        result.RootElement.GetProperty("result").GetProperty("tools")
            .GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task UnknownMethod_ReturnsMethodNotFound()
    {
        var server = ServerOk();

        using var result = await server.HandleRequestAsync(
            TestJson.Element("{\"method\":\"bogus\",\"id\":1}"),
            Anonymous(),
            Ct);

        result.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32601);
    }

    [Fact]
    public async Task ToolsCall_LocalUser_ResolvesDeveloperUser()
    {
        var server = ServerWithSearch(("MmDesktop:DeveloperUserName", "testuser"));

        using var result = await server.HandleRequestAsync(
            TestJson.Element(
                "{\"method\":\"tools/call\",\"id\":1,\"params\":" +
                "{\"name\":\"mattermost_search_threads\"," +
                "\"arguments\":{\"query\":\"needle\"}}}"),
            Anonymous(),
            Ct);

        var text = result.RootElement.GetProperty("result").GetProperty("content")[0]
            .GetProperty("text").GetString();
        text.Should().Contain("Найдено: 1");
    }

    [Fact]
    public async Task ToolsCall_AuthenticatedUser_UsesClaims()
    {
        var server = ServerWithSearch();

        using var result = await server.HandleRequestAsync(
            TestJson.Element(
                "{\"method\":\"tools/call\",\"id\":1,\"params\":" +
                "{\"name\":\"mattermost_search_threads\"," +
                "\"arguments\":{\"query\":\"needle\"}}}"),
            Authenticated(),
            Ct);

        var text = result.RootElement.GetProperty("result").GetProperty("content")[0]
            .GetProperty("text").GetString();
        text.Should().Contain("Найдено: 1");
    }

    [Fact]
    public async Task ToolsCall_WithoutUser_ReturnsToolError()
    {
        var server = ServerOk();

        using var result = await server.HandleRequestAsync(
            TestJson.Element(
                "{\"method\":\"tools/call\",\"id\":1,\"params\":" +
                "{\"name\":\"mattermost_search_threads\"," +
                "\"arguments\":{\"query\":\"needle\"}}}"),
            Anonymous(),
            Ct);

        IsToolError(result).Should().BeTrue();
    }

    [Fact]
    public async Task MissingId_EchoesNull()
    {
        var server = ServerOk();

        using var result = await server.HandleRequestAsync(
            TestJson.Element("{\"method\":\"initialize\"}"),
            Anonymous(),
            Ct);

        result.RootElement.GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void GetServerInfo_ExposesToolsCapability()
    {
        var server = ServerOk();

        var info = JsonSerializer.Serialize(server.GetServerInfo());

        info.Should().Contain("2024-11-05").And.Contain("mattermost-mcp-server");
    }
}
