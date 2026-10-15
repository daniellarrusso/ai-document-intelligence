namespace AiDocumentIntelligence.Domain;

// The signed-in caller as the API sees them, built from the validated token.
public record CurrentUser(Guid Id, string DisplayName, string? Email, IReadOnlyList<string> Roles)
{
    public bool IsAdmin => Roles.Contains(AppRoles.Admin);

    public bool IsHandler => Roles.Contains(AppRoles.Handler);

    public bool IsReader => Roles.Contains(AppRoles.Reader);
}
