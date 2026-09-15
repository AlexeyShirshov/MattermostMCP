using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using MattermostMCP.MmDesktop;
using MattermostMCP.MmDesktop.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MattermostMCP.UnitTests;

public sealed class MattermostAuthHandlerTests
{
    [Fact]
    public async Task AddsBearerTokenHeader()
    {
        var tokenProvider = new MattermostTokenProvider(
            new UnusedHttpClientFactory(),
            Options.Create(new MmDesktopOptions { Token = "test-token" }),
            NullLogger<MattermostTokenProvider>.Instance);

        var innerHandler = new RecordingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK));
        var handler = new MattermostAuthHandler(tokenProvider)
        {
            InnerHandler = innerHandler
        };

        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://test.local")
        };

        await client.GetAsync("/api/v4/users/me", TestContext.Current.CancellationToken);

        innerHandler.LastRequest.Should().NotBeNull();
        innerHandler.LastRequest!.Headers.Authorization
            .Should().Be(new AuthenticationHeaderValue("Bearer", "test-token"));
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException(
                "Login client must not be created when a token is preconfigured.");
    }
}

public sealed class MattermostClientTests
{
    [Fact]
    public async Task GetTeamThreadsAsync_UsesV11PerTeamRoute()
    {
        var response = new UserThreadsResponse();
        var handler = new RecordingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response) });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://test.local")
        };

        var client = new MattermostClient(httpClient);

        await client.GetUserThreadsAsync(
            "user-1", 50, "team-1", 1, TestContext.Current.CancellationToken);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.PathAndQuery
            .Should().Be(
                "/api/v4/users/user-1/teams/team-1/threads?per_page=50&page=1&deleted=false&unread=false");
    }
}

internal sealed class RecordingHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(response);
    }
}
