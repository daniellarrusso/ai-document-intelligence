using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AiDocumentIntelligence.Api.Tests;

// Keeps the API's real JWT bearer pipeline (including how Microsoft.Identity.Web maps token claims to roles
// and scopes) and only swaps where trust comes from: tokens signed with a local test key instead of Entra's
// published keys. Lets tests prove that a token shaped like Entra's really authorizes the right people.
public class JwtApiFactory : ApiFactory
{
    public const string TestTenantId = "11111111-1111-1111-1111-111111111111";
    public const string TestClientId = "22222222-2222-2222-2222-222222222222";
    public const string Issuer = $"https://login.microsoftonline.com/{TestTenantId}/v2.0";

    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes(new string('k', 64)));
    private static readonly SymmetricSecurityKey OtherKey = new(Encoding.UTF8.GetBytes(new string('x', 64)));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = TestTenantId,
            ["AzureAd:ClientId"] = TestClientId,
        }));

        base.ConfigureWebHost(builder);
    }

    protected override void ConfigureAuthentication(IServiceCollection services)
    {
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            // A pre-set configuration stops the handler downloading Entra's metadata and signing keys.
            options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            options.TokenValidationParameters.IssuerSigningKey = SigningKey;
            options.TokenValidationParameters.ValidIssuer = Issuer;
            options.TokenValidationParameters.IssuerValidator = null;
        });
    }

    // Builds a v2 access token shaped like the ones Entra issues: "roles" is an array, "scp" a space-separated string.
    public static string CreateToken(
        string[]? roles = null,
        string scope = "access_as_user",
        string audience = TestClientId,
        TimeSpan? lifetime = null,
        bool signWithOtherKey = false)
    {
        var claims = new Dictionary<string, object>
        {
            ["name"] = "Jane Doe",
            ["scp"] = scope,
            ["ver"] = "2.0",
            ["tid"] = TestTenantId,
        };
        if (roles != null)
        {
            claims["roles"] = roles;
        }

        var now = DateTime.UtcNow;
        var lifetimeValue = lifetime ?? TimeSpan.FromMinutes(30);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Claims = claims,
            NotBefore = now.AddMinutes(-60),
            IssuedAt = now.AddMinutes(-60),
            Expires = now.Add(lifetimeValue),
            SigningCredentials = new SigningCredentials(signWithOtherKey ? OtherKey : SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }
}
