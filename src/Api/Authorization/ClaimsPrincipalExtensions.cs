using System.Security.Claims;
using AiDocumentIntelligence.Domain;

namespace AiDocumentIntelligence.Api.Authorization;

public static class ClaimsPrincipalExtensions
{
    // Null when the token has no usable object id (the caller can't be recorded as an owner).
    // Claim names are read as Entra issues them; this relies on MapInboundClaims = false.
    public static CurrentUser? GetCurrentUser(this ClaimsPrincipal principal)
    {
        var oid = principal.FindFirstValue("oid");
        if (!Guid.TryParse(oid, out var id))
        {
            return null;
        }

        var name = principal.FindFirstValue("name") ?? principal.Identity?.Name;
        var email = principal.FindFirstValue("preferred_username") ?? principal.FindFirstValue("email");
        var roles = principal.Claims
            .Where(c => c.Type is ClaimTypes.Role or "roles")
            .Select(c => c.Value)
            .Where(AppRoles.All.Contains)
            .Distinct()
            .Order()
            .ToList();

        return new CurrentUser(
            id,
            Truncate(string.IsNullOrWhiteSpace(name) ? email ?? id.ToString() : name, AppUser.MaxNameLength),
            email is null ? null : Truncate(email, AppUser.MaxEmailLength),
            roles);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
