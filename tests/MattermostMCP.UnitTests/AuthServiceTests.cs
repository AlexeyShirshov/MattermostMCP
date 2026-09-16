using System.Security.Claims;
using FluentAssertions;
using MattermostMCP.Auth;
using Xunit;

namespace MattermostMCP.UnitTests;

public sealed class AuthServiceTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "test"));

    [Fact]
    public void GetUsername_PrefersPreferredUsername()
    {
        var user = Principal(
            new Claim("user_id", "uid"),
            new Claim("preferred_username", "preferred"),
            new Claim(ClaimTypes.Name, "name"));

        AuthService.GetUsername(user).Should().Be("preferred");
    }

    [Fact]
    public void GetUsername_FallsBackToUserId()
    {
        var user = Principal(new Claim("user_id", "uid"), new Claim(ClaimTypes.Name, "name"));

        AuthService.GetUsername(user).Should().Be("uid");
    }

    [Fact]
    public void GetUsername_FallsBackToIdentityName()
    {
        var user = Principal(new Claim(ClaimTypes.Name, "name"));

        AuthService.GetUsername(user).Should().Be("name");
    }

    [Fact]
    public void GetUsername_FallsBackToNameIdentifier()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "sub")], authenticationType: "test");
        var user = new ClaimsPrincipal(identity);

        AuthService.GetUsername(user).Should().Be("sub");
    }

    [Fact]
    public void GetUsername_WithoutClaims_ReturnsUnknown()
    {
        AuthService.GetUsername(Principal()).Should().Be("unknown");
    }

    [Fact]
    public void HasRole_MatchesRealmAccessCaseInsensitively()
    {
        var user = Principal(new Claim("realm_access", """{"roles":["Admin"]}"""));

        AuthService.HasRole(user, "admin").Should().BeTrue();
    }

    [Fact]
    public void HasRole_WithoutClaim_ReturnsFalse()
    {
        AuthService.HasRole(Principal(), "admin").Should().BeFalse();
    }

    [Fact]
    public void GetEmail_PrefersClaimTypesEmail()
    {
        var user = Principal(
            new Claim(ClaimTypes.Email, "a@example.com"),
            new Claim("email", "b@example.com"));

        AuthService.GetEmail(user).Should().Be("a@example.com");
    }

    [Fact]
    public void GetEmail_FallsBackToShortClaim()
    {
        var user = Principal(new Claim("email", "b@example.com"));

        AuthService.GetEmail(user).Should().Be("b@example.com");
    }

    [Fact]
    public void GetEmail_WithoutEmail_ReturnsNull()
    {
        AuthService.GetEmail(Principal()).Should().BeNull();
    }

    [Fact]
    public void GetMattermostLogin_TakesLocalPartOfEmail()
    {
        var user = Principal(new Claim("email", "testuser@example.com"));

        AuthService.GetMattermostLogin(user).Should().Be("testuser");
    }

    [Fact]
    public void GetMattermostLogin_WithoutEmail_FallsBackToUsername()
    {
        var user = Principal(new Claim("preferred_username", "testuser"));

        AuthService.GetMattermostLogin(user).Should().Be("testuser");
    }

    [Fact]
    public void GetMattermostLogin_EmailWithoutAtSign_FallsBackToUsername()
    {
        var user = Principal(
            new Claim("email", "no-at-sign"),
            new Claim("preferred_username", "fallback"));

        AuthService.GetMattermostLogin(user).Should().Be("fallback");
    }
}
