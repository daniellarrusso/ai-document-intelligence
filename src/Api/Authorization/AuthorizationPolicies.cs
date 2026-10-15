using AiDocumentIntelligence.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;

namespace AiDocumentIntelligence.Api.Authorization;

public static class AuthorizationPolicies
{
    // Delegated scope the UI requests; tokens without it are rejected.
    public const string ApiScope = "access_as_user";

    // View claims and documents, ask questions about a document.
    public const string CanRead = nameof(CanRead);

    // Create claims, upload and delete documents.
    public const string CanWrite = nameof(CanWrite);

    public static IServiceCollection AddAppAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            // Secure by default: an endpoint without its own policy still needs the API scope and an app role,
            // so a forgotten [Authorize] never exposes anything to a role-less user.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireScope(ApiScope)
                .RequireRole(AppRoles.Reader, AppRoles.Handler, AppRoles.Admin)
                .Build())
            .AddPolicy(CanRead, policy => policy
                .RequireAuthenticatedUser()
                .RequireScope(ApiScope)
                .RequireRole(AppRoles.Reader, AppRoles.Handler, AppRoles.Admin))
            .AddPolicy(CanWrite, policy => policy
                .RequireAuthenticatedUser()
                .RequireScope(ApiScope)
                .RequireRole(AppRoles.Handler, AppRoles.Admin));

        return services;
    }
}
