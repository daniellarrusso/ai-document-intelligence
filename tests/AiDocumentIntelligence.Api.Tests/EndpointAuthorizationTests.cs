using System.Net;
using System.Net.Http.Json;
using AiDocumentIntelligence.Domain;
using Xunit;

namespace AiDocumentIntelligence.Api.Tests;

public class EndpointAuthorizationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public EndpointAuthorizationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    // Every endpoint, the access it needs, and a way to call it. Bodies are valid so that an allowed
    // caller gets past model binding and the test only ever sees the authorization outcome.
    public enum Access { Read, Write }

    public static TheoryData<string, string, Access> Endpoints => new()
    {
        { "GET", "/api/me", Access.Read },
        { "GET", "/api/claims", Access.Read },
        { "GET", "/api/claims/11111111-1111-1111-1111-111111111111", Access.Read },
        { "POST", "/api/claims", Access.Write },
        { "POST", "/api/claims/11111111-1111-1111-1111-111111111111/documents", Access.Write },
        { "GET", "/api/documents", Access.Read },
        { "GET", "/api/documents/11111111-1111-1111-1111-111111111111", Access.Read },
        { "POST", "/api/documents", Access.Write },
        { "DELETE", "/api/documents/11111111-1111-1111-1111-111111111111", Access.Write },
        { "POST", "/api/documents/11111111-1111-1111-1111-111111111111/ask", Access.Read },
        { "GET", "/weatherforecast", Access.Read },
    };

    private static HttpRequestMessage Build(string method, string url, string? user = null, string? roles = null, string? scope = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (user != null) request.Headers.Add(TestAuthHandler.UserHeader, user);
        if (roles != null) request.Headers.Add(TestAuthHandler.RolesHeader, roles);
        if (scope != null) request.Headers.Add(TestAuthHandler.ScopeHeader, scope);

        if (method == "POST")
        {
            request.Content = url.EndsWith("/ask") ? JsonContent.Create(new { question = "q" })
                : url.EndsWith("/api/claims") ? JsonContent.Create(new
                {
                    policyNumber = "POL-1",
                    claimantName = "Jane",
                    incidentDate = "2026-01-01",
                    amountClaimed = 10,
                })
                : new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "a.txt" } };
        }

        return request;
    }

    private async Task<HttpStatusCode> Send(string method, string url, string? user = null, string? roles = null, string? scope = null)
    {
        using var client = _factory.CreateClient();
        using var response = await client.SendAsync(Build(method, url, user, roles, scope));
        return response.StatusCode;
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_Anonymous_Returns401(string method, string url, Access _)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await Send(method, url));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_SignedInWithoutAnyRole_Returns403(string method, string url, Access _)
    {
        Assert.Equal(HttpStatusCode.Forbidden, await Send(method, url, user: "nobody"));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_RoleWithoutApiScope_Returns403(string method, string url, Access _)
    {
        Assert.Equal(HttpStatusCode.Forbidden, await Send(method, url, user: "u", roles: AppRoles.Admin, scope: "something.else"));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_UnknownRole_Returns403(string method, string url, Access _)
    {
        Assert.Equal(HttpStatusCode.Forbidden, await Send(method, url, user: "u", roles: "Superuser"));
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_Reader_IsAllowedOnlyToRead(string method, string url, Access access)
    {
        var status = await Send(method, url, user: "r", roles: AppRoles.Reader);

        if (access == Access.Read) AssertAllowed(status); else Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_HandlerAndAdmin_AreAllowedEverywhere(string method, string url, Access _)
    {
        AssertAllowed(await Send(method, url, user: "h", roles: AppRoles.Handler));
        AssertAllowed(await Send(method, url, user: "a", roles: AppRoles.Admin));
    }

    [Fact]
    public async Task Me_ReturnsNameAndOnlyKnownRoles()
    {
        using var client = _factory.CreateClient();
        using var request = Build("GET", "/api/me", user: "Jane Doe", roles: "Handler,Reader,Superuser");

        using var response = await client.SendAsync(request);
        var me = await response.Content.ReadFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Jane Doe", me!["name"].GetString());
        Assert.Equal(new[] { "Handler", "Reader" }, me["roles"].EnumerateArray().Select(r => r.GetString()!).ToArray());
    }

    // Past authorization the endpoint may legitimately answer 200/201/404/400 depending on the mocked data;
    // all that matters here is that the caller was neither rejected nor challenged.
    private static void AssertAllowed(HttpStatusCode status)
    {
        Assert.NotEqual(HttpStatusCode.Unauthorized, status);
        Assert.NotEqual(HttpStatusCode.Forbidden, status);
    }
}
