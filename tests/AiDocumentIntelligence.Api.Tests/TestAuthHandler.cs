using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiDocumentIntelligence.Api.Tests;

// Stands in for Entra token validation in tests only (the real JWT validation is not exercised here).
// The caller is described by headers: no X-Test-User means anonymous; X-Test-Roles is a comma-separated
// list of app roles; X-Test-Scope overrides the delegated scope (default access_as_user). The object id (oid)
// is derived from the user name so the same name is always the same user; X-Test-Oid overrides it, and
// X-Test-NoOid omits it to mimic a token without one.
public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string RolesHeader = "X-Test-Roles";
    public const string ScopeHeader = "X-Test-Scope";
    public const string OidHeader = "X-Test-Oid";
    public const string NoOidHeader = "X-Test-NoOid";

    public static Guid OidFor(string user) =>
        new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(user)));

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new("name", user.ToString()),
            new("scp", Request.Headers.TryGetValue(ScopeHeader, out var scope) ? scope.ToString() : "access_as_user"),
        };

        if (!Request.Headers.ContainsKey(NoOidHeader))
        {
            var oid = Request.Headers.TryGetValue(OidHeader, out var explicitOid)
                ? explicitOid.ToString()
                : OidFor(user.ToString()).ToString();
            claims.Add(new("oid", oid));
        }

        if (Request.Headers.TryGetValue(RolesHeader, out var roles))
        {
            claims.AddRange(roles.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(role => new Claim("roles", role)));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, nameType: "name", roleType: "roles");

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
