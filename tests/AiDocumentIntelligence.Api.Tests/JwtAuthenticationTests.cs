using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AiDocumentIntelligence.Api.Tests;

// End-to-end through the real JWT bearer handler: these fail if tokens shaped like Entra's are not turned
// into the roles and scope the policies check.
public class JwtAuthenticationTests : IClassFixture<JwtApiFactory>
{
    private readonly JwtApiFactory _factory;

    public JwtAuthenticationTests(JwtApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string url, string? token, HttpContent? content = null)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(method, url) { Content = content };
        if (token != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }

    private static JsonContent NewClaim() => JsonContent.Create(new
    {
        policyNumber = "POL-1",
        claimantName = "Jane",
        incidentDate = "2026-01-01",
        amountClaimed = 10,
    });

    [Theory]
    [InlineData("Admin")]
    [InlineData("Handler")]
    [InlineData("Reader")]
    public async Task Me_TokenWithAppRole_ReturnsThatRoleAndTheName(string role)
    {
        using var response = await Send(HttpMethod.Get, "/api/me", JwtApiFactory.CreateToken(roles: [role]));
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Jane Doe", me.GetProperty("name").GetString());
        Assert.Equal([role], me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToArray());
    }

    [Fact]
    public async Task Me_TokenWithSeveralRoles_ReturnsAll()
    {
        using var response = await Send(HttpMethod.Get, "/api/me", JwtApiFactory.CreateToken(roles: ["Reader", "Handler"]));
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Handler", "Reader"], me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToArray());
    }

    [Fact]
    public async Task CreateClaim_HandlerToken_IsAllowed()
    {
        using var response = await Send(HttpMethod.Post, "/api/claims", JwtApiFactory.CreateToken(roles: ["Handler"]), NewClaim());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateClaim_AdminToken_IsAllowed()
    {
        using var response = await Send(HttpMethod.Post, "/api/claims", JwtApiFactory.CreateToken(roles: ["Admin"]), NewClaim());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateClaim_ReaderToken_IsForbidden()
    {
        using var response = await Send(HttpMethod.Post, "/api/claims", JwtApiFactory.CreateToken(roles: ["Reader"]), NewClaim());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListClaims_ReaderToken_IsAllowed()
    {
        using var response = await Send(HttpMethod.Get, "/api/claims", JwtApiFactory.CreateToken(roles: ["Reader"]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListClaims_TokenWithoutRoles_IsForbidden()
    {
        using var response = await Send(HttpMethod.Get, "/api/claims", JwtApiFactory.CreateToken(roles: null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListClaims_UnknownRole_IsForbidden()
    {
        using var response = await Send(HttpMethod.Get, "/api/claims", JwtApiFactory.CreateToken(roles: ["Superuser"]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListClaims_RoleButWrongScope_IsForbidden()
    {
        using var response = await Send(HttpMethod.Get, "/api/claims", JwtApiFactory.CreateToken(roles: ["Admin"], scope: "other.scope"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListClaims_TokenForAnotherAudience_IsUnauthorized()
    {
        using var response = await Send(HttpMethod.Get, "/api/claims", JwtApiFactory.CreateToken(roles: ["Admin"], audience: "someone-else"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListClaims_ExpiredToken_IsUnauthorized()
    {
        using var response = await Send(HttpMethod.Get, "/api/claims", JwtApiFactory.CreateToken(roles: ["Admin"], lifetime: TimeSpan.FromMinutes(-30)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListClaims_TokenSignedWithAnotherKey_IsUnauthorized()
    {
        using var response = await Send(HttpMethod.Get, "/api/claims", JwtApiFactory.CreateToken(roles: ["Admin"], signWithOtherKey: true));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListClaims_NoToken_IsUnauthorized()
    {
        using var response = await Send(HttpMethod.Get, "/api/claims", token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
