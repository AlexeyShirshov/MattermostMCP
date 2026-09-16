using System.Text.Json;
using FluentAssertions;
using MattermostMCP.Auth;
using Xunit;

namespace MattermostMCP.UnitTests;

public sealed class OAuthMetadataTests
{
    [Fact]
    public void Resolve_UsesOAuthSectionAndDerivesEndpointsFromIssuer()
    {
        var config = TestConfig.Create(
            ("OAuth:Issuer", "https://sso.example.com/realms/dev/"),
            ("OAuth:ClientId", "mcp-server"));

        var result = OAuthMetadata.Resolve(config);

        result.Issuer.Should().Be("https://sso.example.com/realms/dev");
        result.AuthorizationEndpoint.Should().Be(
            "https://sso.example.com/realms/dev/protocol/openid-connect/auth");
        result.TokenEndpoint.Should().Be(
            "https://sso.example.com/realms/dev/protocol/openid-connect/token");
        result.JwksUri.Should().Be(
            "https://sso.example.com/realms/dev/protocol/openid-connect/certs");
        result.ClientId.Should().Be("mcp-server");
    }

    [Fact]
    public void Resolve_FallsBackToAuthorityHostWhenNoOAuthSection()
    {
        var config = TestConfig.Create(
            ("AuthOptions:AuthorityHost", "https://sso.example.com/realm"));

        var result = OAuthMetadata.Resolve(config);

        result.Issuer.Should().Be("https://sso.example.com/realm");
        result.AuthorizationEndpoint.Should().Be(
            "https://sso.example.com/realm/protocol/openid-connect/auth");
    }

    [Fact]
    public void Resolve_FallsBackToAuthorityWhenAuthorityHostMissing()
    {
        var config = TestConfig.Create(
            ("AuthOptions:Authority", "https://sso.example.com/authority"));

        var result = OAuthMetadata.Resolve(config);

        result.Issuer.Should().Be("https://sso.example.com/authority");
    }

    [Fact]
    public void Resolve_KeepsExplicitEndpoints()
    {
        var config = TestConfig.Create(
            ("OAuth:Issuer", "https://issuer"),
            ("OAuth:AuthorizationEndpoint", "https://custom/auth"),
            ("OAuth:TokenEndpoint", "https://custom/token"),
            ("OAuth:JwksUri", "https://custom/certs"));

        var result = OAuthMetadata.Resolve(config);

        result.AuthorizationEndpoint.Should().Be("https://custom/auth");
        result.TokenEndpoint.Should().Be("https://custom/token");
        result.JwksUri.Should().Be("https://custom/certs");
    }

    [Fact]
    public void Resolve_ClientIdFallsBackToAuthOptions()
    {
        var config = TestConfig.Create(
            ("OAuth:Issuer", "https://issuer"),
            ("AuthOptions:ClientId", "fallback-client"));

        var result = OAuthMetadata.Resolve(config);

        result.ClientId.Should().Be("fallback-client");
    }

    [Fact]
    public void Resolve_WithoutAnything_ReturnsEmptyIssuer()
    {
        var result = OAuthMetadata.Resolve(TestConfig.Create());

        result.Issuer.Should().BeEmpty();
        result.AuthorizationEndpoint.Should().BeEmpty();
        result.TokenEndpoint.Should().BeEmpty();
        result.JwksUri.Should().BeEmpty();
        result.ClientId.Should().BeEmpty();
    }

    [Fact]
    public void BuildJson_EmitsRfc8414Fields()
    {
        var options = new OAuthMetadataOptions
        {
            Issuer = "https://issuer",
            AuthorizationEndpoint = "https://issuer/auth",
            TokenEndpoint = "https://issuer/token",
            JwksUri = "https://issuer/certs",
            ClientId = "client",
            Scopes = ["openid", "profile"]
        };

        using var doc = JsonDocument.Parse(OAuthMetadata.BuildJson(options));
        var root = doc.RootElement;

        root.GetProperty("issuer").GetString().Should().Be("https://issuer");
        root.GetProperty("authorization_endpoint").GetString().Should().Be("https://issuer/auth");
        root.GetProperty("token_endpoint").GetString().Should().Be("https://issuer/token");
        root.GetProperty("jwks_uri").GetString().Should().Be("https://issuer/certs");
        root.GetProperty("response_types_supported")[0].GetString().Should().Be("code");
        root.GetProperty("grant_types_supported")[1].GetString().Should().Be("refresh_token");
        root.GetProperty("code_challenge_methods_supported")[0].GetString().Should().Be("S256");
        root.GetProperty("scopes_supported").GetArrayLength().Should().Be(2);
        root.TryGetProperty("registration_endpoint", out _).Should().BeFalse();
    }

    [Fact]
    public void BuildJson_IncludesRegistrationEndpointWhenSet()
    {
        var options = new OAuthMetadataOptions
        {
            Issuer = "https://issuer",
            RegistrationEndpoint = "https://issuer/register"
        };

        using var doc = JsonDocument.Parse(OAuthMetadata.BuildJson(options));

        doc.RootElement.GetProperty("registration_endpoint").GetString()
            .Should().Be("https://issuer/register");
    }

    [Fact]
    public void OAuthMetadataOptions_HasSensibleDefaults()
    {
        var options = new OAuthMetadataOptions();

        options.Scopes.Should().Equal("openid", "profile", "email", "offline_access");
        options.RegistrationEndpoint.Should().BeEmpty();
    }
}
