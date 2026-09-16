using System.Net;
using FluentAssertions;
using MattermostMCP.MmDesktop;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MattermostMCP.UnitTests;

public sealed class MattermostTokenProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MattermostTokenProvider Create(
        RoutingHttpMessageHandler handler,
        MmDesktopOptions options)
        => new(
            TestSubstitutes.HttpClientFactory(handler),
            Options.Create(options),
            TestLoggers.For<MattermostTokenProvider>());

    [Fact]
    public async Task GetTokenAsync_WithConfiguredToken_ReturnsItWithoutHttp()
    {
        var handler = new RoutingHttpMessageHandler(
            _ => TestHttp.Status(HttpStatusCode.InternalServerError));
        var provider = Create(handler, new MmDesktopOptions { Token = "static-token" });

        var token = await provider.GetTokenAsync(Ct);
        var again = await provider.GetTokenAsync(Ct);

        token.Should().Be("static-token");
        again.Should().Be("static-token");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTokenAsync_WithCredentials_LogsInAndCaches()
    {
        var handler = new RoutingHttpMessageHandler(_ => TestHttp.LoginWithToken("session-token"));
        var provider = Create(
            handler,
            new MmDesktopOptions { Username = "testuser", Password = "secret" });

        var token = await provider.GetTokenAsync(Ct);
        var again = await provider.GetTokenAsync(Ct);

        token.Should().Be("session-token");
        again.Should().Be("session-token");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.AbsolutePath.Should().Be("/api/v4/users/login");
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task GetTokenAsync_WithCredentials_LooksUpAuthClient()
    {
        var handler = new RoutingHttpMessageHandler(_ => TestHttp.LoginWithToken("session-token"));
        var factory = TestSubstitutes.HttpClientFactory(handler);
        var provider = new MattermostTokenProvider(
            factory,
            Options.Create(new MmDesktopOptions { Username = "u", Password = "p" }),
            TestLoggers.For<MattermostTokenProvider>());

        await provider.GetTokenAsync(Ct);

        factory.Received(1).CreateClient(MattermostTokenProvider.AuthClientName);
    }

    [Fact]
    public async Task GetTokenAsync_WithoutCredentials_Throws()
    {
        var handler = new RoutingHttpMessageHandler(_ => TestHttp.Status(HttpStatusCode.OK));
        var provider = Create(handler, new MmDesktopOptions());

        var act = () => provider.GetTokenAsync(Ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*credentials are not configured*");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTokenAsync_LoginWithoutTokenHeader_Throws()
    {
        var handler = new RoutingHttpMessageHandler(_ => TestHttp.Json("{}"));
        var provider = Create(
            handler,
            new MmDesktopOptions { Username = "testuser", Password = "secret" });

        var act = () => provider.GetTokenAsync(Ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no Token header*");
    }

    [Fact]
    public async Task GetTokenAsync_LoginFails_Throws()
    {
        var handler = new RoutingHttpMessageHandler(
            _ => TestHttp.Status(HttpStatusCode.Unauthorized));
        var provider = Create(
            handler,
            new MmDesktopOptions { Username = "testuser", Password = "secret" });

        var act = () => provider.GetTokenAsync(Ct);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetTokenAsync_ConcurrentCalls_LoginOnce()
    {
        var handler = new RoutingHttpMessageHandler(_ => TestHttp.LoginWithToken("session-token"));
        var provider = Create(
            handler,
            new MmDesktopOptions { Username = "testuser", Password = "secret" });

        var tokens = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => provider.GetTokenAsync(Ct)));

        tokens.Should().OnlyContain(t => t == "session-token");
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public void AuthClientName_IsStable()
    {
        MattermostTokenProvider.AuthClientName.Should().Be("MmDesktop.Auth");
    }
}
