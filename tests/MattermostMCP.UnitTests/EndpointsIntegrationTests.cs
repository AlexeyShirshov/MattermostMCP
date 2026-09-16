using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using FluentAssertions;
using MattermostMCP.MmDesktop;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MattermostMCP.UnitTests;

/// <summary>
/// Тестовый хост с подменённым Mattermost-клиентом и всегда аутентифицированным пользователем.
/// </summary>
internal sealed class McpAppFactory : WebApplicationFactory<MattermostMCP.Startup>
{
    private readonly string _environment;
    private readonly Dictionary<string, string?> _config;

    public McpAppFactory(
        MmApiRouter? router = null,
        string environment = "Local",
        params (string Key, string? Value)[] config)
    {
        Router = router ?? new MmApiRouter()
            .AddJson("users/username/", MmFixtures.User("uid-1"))
            .AddJson("users/uid-1/threads", MmFixtures.Threads("thread-1", "needle"))
            .AddPosts("thread-1", ("post-root", "needle"))
            .AddChannel("channel-1");
        Handler = new RoutingHttpMessageHandler(Router.Respond);
        _environment = environment;
        _config = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["MmDesktop:ThreadsApiVersion"] = "v7",
            ["MmDesktop:DeveloperUserName"] = "testuser"
        };
        foreach (var (key, value) in config)
        {
            _config[key] = value;
        }
    }

    public MmApiRouter Router { get; }

    public RoutingHttpMessageHandler Handler { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(_config));

        builder.ConfigureTestServices(services =>
        {
            var http = new HttpClient(Handler) { BaseAddress = new Uri("http://test.local/") };
            services.AddSingleton(new MattermostClient(http));
            services.AddSingleton(new MattermostTokenProvider(
                TestSubstitutes.HttpClientFactory(Handler),
                Options.Create(new MmDesktopOptions { Token = "static-token" }),
                TestLoggers.For<MattermostTokenProvider>()));

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", null);
            services.Configure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = "Test";
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
            });
        });
    }

    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim("preferred_username", "testuser"),
                    new Claim("email", "testuser@example.com")
                ],
                authenticationType: "Test");
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}

public sealed class EndpointsIntegrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;


    private static StringContent Json(string body)
        => new(body, Encoding.UTF8, "application/json");

    [Fact]
    public async Task Root_ReturnsOk()
    {
        using var factory = new McpAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct))
            .Should().Contain("mattermost-mcp-server");
    }

    [Fact]
    public async Task WellKnown_OAuthMetadata_HiddenInLocal()
    {
        using var factory = new McpAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/.well-known/oauth-authorization-server", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WellKnown_OAuthMetadata_ServedWhenAuthEnabled()
    {
        using var factory = new McpAppFactory(
            environment: "Development",
            config:
            [
                ("AuthOptions:AuthorityHost", "https://sso.example.com/realms/dev"),
                ("AuthOptions:ClientId", "mcp-client")
            ]);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/.well-known/oauth-authorization-server", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().Contain("authorization_endpoint")
            .And.Contain("https://sso.example.com/realms/dev/protocol/openid-connect/auth");
    }

    [Fact]
    public async Task GetMcp_WithJsonAccept_ReturnsServerInfo()
    {
        using var factory = new McpAppFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.Add("Accept", "application/json");

        var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("Mcp-Session-Id");
        (await response.Content.ReadAsStringAsync(Ct))
            .Should().Contain("mattermost-mcp-server");
    }

    [Fact]
    public async Task GetMcp_WithoutJsonAccept_RedirectsToSse()
    {
        using var factory = new McpAppFactory();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/mcp", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/mcp/sse");
    }

    [Fact]
    public async Task PostMcp_Initialize_ReturnsResultAndSession()
    {
        using var factory = new McpAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/mcp", Json("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}"),
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("Mcp-Session-Id");
        (await response.Content.ReadAsStringAsync(Ct))
            .Should().Contain("2024-11-05");
    }

    [Fact]
    public async Task PostMcp_ToolsList_ReturnsTools()
    {
        using var factory = new McpAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/mcp", Json("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}"),
            Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().Contain("mattermost_search_threads");
    }

    [Fact]
    public async Task PostMcp_ToolsCall_Search_ReturnsThread()
    {
        using var factory = new McpAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/mcp",
            Json("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":" +
                 "\"mattermost_search_threads\",\"arguments\":{\"query\":\"needle\"}}}"),
            Ct);

        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().Contain("thread-1");
    }

    [Fact]
    public async Task PostMcp_EmptyBody_ReturnsBadRequest()
    {
        using var factory = new McpAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/mcp", Json(string.Empty), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostMcp_InvalidJson_ReturnsParseError()
    {
        using var factory = new McpAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/mcp", Json("{ not json"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct))
            .Should().Contain("-32700");
    }

    [Fact]
    public async Task PostLegacyMessage_Initialize_Works()
    {
        using var factory = new McpAppFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/mcp/message", Json("{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"initialize\"}"),
            Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DisallowedOrigin_ReturnsForbidden()
    {
        using var factory = new McpAppFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "http://evil.example.com");

        var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LoopbackOrigin_IsAllowed()
    {
        using var factory = new McpAppFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "http://localhost:5173");

        var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RestSearch_ReturnsResults()
    {
        using var factory = new McpAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/threads/search?query=needle&limit=50", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().Contain("testuser").And.Contain("thread-1");
    }

    [Fact]
    public async Task RestFollowed_ReturnsPage()
    {
        using var factory = new McpAppFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/threads/followed?limit=5&page=1", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct))
            .Should().Contain("thread-1");
    }

    [Fact]
    public async Task RestSearch_UserNotFound_ReturnsNotFound()
    {
        using var factory = new McpAppFactory(new MmApiRouter());
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/threads/search?query=needle", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RestSearch_UpstreamFailure_ReturnsBadGateway()
    {
        var router = new MmApiRouter()
            .AddJson("users/username/", MmFixtures.User("uid-1"))
            .AddStatus("users/uid-1/threads", HttpStatusCode.InternalServerError);
        using var factory = new McpAppFactory(router);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/threads/search?query=needle", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task RestFollowed_UpstreamFailure_ReturnsBadGateway()
    {
        var router = new MmApiRouter()
            .AddJson("users/username/", MmFixtures.User("uid-1"))
            .AddStatus("users/uid-1/threads", HttpStatusCode.InternalServerError);
        using var factory = new McpAppFactory(router);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/threads/followed", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task Sse_StreamsEndpointEvent()
    {
        using var factory = new McpAppFactory();
        using var client = factory.CreateClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using var response = await client.GetAsync(
            "/mcp/sse", HttpCompletionOption.ResponseHeadersRead, cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        var buffer = new byte[512];
        var read = await stream.ReadAsync(buffer, cts.Token);

        read.Should().BeGreaterThan(0);
        Encoding.UTF8.GetString(buffer, 0, read).Should().Contain("/mcp/message");
    }
}
