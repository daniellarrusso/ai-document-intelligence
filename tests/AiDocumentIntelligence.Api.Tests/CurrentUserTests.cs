using System.Security.Claims;
using Claim = System.Security.Claims.Claim;
using AiDocumentIntelligence.Api.Authorization;
using AiDocumentIntelligence.Domain;
using Xunit;

namespace AiDocumentIntelligence.Api.Tests;

public class CurrentUserTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Test"));

    [Fact]
    public void GetCurrentUser_FullToken_ReturnsIdentityAndKnownRoles()
    {
        var id = Guid.NewGuid();

        var user = Principal(
            new Claim("oid", id.ToString()),
            new Claim("name", "Jane Doe"),
            new Claim("preferred_username", "jane@example.com"),
            new Claim("roles", AppRoles.Handler),
            new Claim("roles", "NotARealRole")).GetCurrentUser();

        Assert.NotNull(user);
        Assert.Equal(id, user.Id);
        Assert.Equal("Jane Doe", user.DisplayName);
        Assert.Equal("jane@example.com", user.Email);
        Assert.Equal([AppRoles.Handler], user.Roles);
        Assert.True(user.IsHandler);
        Assert.False(user.IsAdmin);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    public void GetCurrentUser_MissingOrInvalidObjectId_ReturnsNull(string? oid)
    {
        var claims = oid == null ? [] : new[] { new Claim("oid", oid) };

        Assert.Null(Principal(claims).GetCurrentUser());
    }

    [Fact]
    public void GetCurrentUser_NoName_FallsBackToEmail()
    {
        var user = Principal(
            new Claim("oid", Guid.NewGuid().ToString()),
            new Claim("preferred_username", "jane@example.com")).GetCurrentUser();

        Assert.Equal("jane@example.com", user!.DisplayName);
    }
}
